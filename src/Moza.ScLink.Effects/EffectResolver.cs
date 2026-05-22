using Moza.ScLink.Core.Diagnostics;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Events;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Resolver;
using Moza.ScLink.Effects.Catalogs;

// Disambiguate the §5 domain ForceEffect (Core.Effects, wrapped by PlayEffectCommand) from the legacy
// Core.Models.ForceEffect that the soon-to-be-removed ForceFeedbackController uses (CS0104).
using ForceEffect = Moza.ScLink.Core.Effects.ForceEffect;

namespace Moza.ScLink.Effects;

/// <summary>
/// Resolves a <see cref="GameEvent"/> into <see cref="ForceCommand"/>s (PRP §5.7): looks up the Phase-1
/// effect mapping (<see cref="GameEventToEffectMap"/>), translates the catalog <see cref="EffectDefinition"/>
/// into a Core <see cref="ForceEffect"/>, applies the <see cref="GainStack"/>, and emits a
/// <see cref="PlayEffectCommand"/> or <see cref="StopEffectCommand"/>. Reads <see cref="EffectCatalog.Current"/>
/// fresh per call so catalog hot-reloads are picked up.
/// <para>
/// Every failure path degrades gracefully (logged via <see cref="AppLog"/> + empty list), never throws:
/// the resolver runs inside <c>EffectResolverService</c>'s channel-fed consume loop, where a throw would
/// kill the pipeline. Translation validates category/effectType defensively even though
/// <see cref="EffectCatalog"/> pre-validates them (defense-in-depth at the layer boundary).
/// </para>
/// </summary>
public sealed class EffectResolver : IEffectResolver
{
    private readonly EffectCatalog _catalog;

    public EffectResolver(EffectCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <inheritdoc />
    public IReadOnlyList<ForceCommand> Resolve(GameEvent gameEvent, ResolverContext context)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        ArgumentNullException.ThrowIfNull(context);

        var entry = GameEventToEffectMap.TryGet(gameEvent.EventType);
        if (entry is null)
        {
            return [];  // unmapped event type — no command (not an error)
        }

        var effect = FindEffect(entry.EffectId);
        if (effect is null)
        {
            AppLog.Write($"EffectResolver: effect '{entry.EffectId}' (mapped from {gameEvent.EventType}) not found in catalog; skipping.");
            return [];
        }

        // A StopEntry — or a Play mapping onto a catalog effect typed "Stop" (Finding 2, defensive) —
        // resolves to a StopEffectCommand for the effect's sustained state.
        if (entry is StopEntry || IsStopType(effect.EffectType))
        {
            if (string.IsNullOrEmpty(effect.StateKey))
            {
                AppLog.Write($"EffectResolver: stop for '{effect.EffectId}' ({gameEvent.EventType}) has no StateKey; skipping.");
                return [];
            }

            return [StopCommand(effect.StateKey)];
        }

        if (!TryTranslate(effect, out var forceEffect, out var reason))
        {
            AppLog.Write($"EffectResolver: cannot translate '{effect.EffectId}' ({gameEvent.EventType}): {reason}; skipping.");
            return [];
        }

        var finalIntensity = ComputeIntensity(effect, forceEffect.Category, gameEvent, context);
        return [PlayCommand(forceEffect, finalIntensity)];
    }

    private EffectDefinition? FindEffect(string effectId)
    {
        foreach (var effect in _catalog.Current)
        {
            if (string.Equals(effect.EffectId, effectId, StringComparison.Ordinal))
            {
                return effect;
            }
        }

        return null;
    }

    private static double ComputeIntensity(EffectDefinition effect, EffectCategory category, GameEvent gameEvent, ResolverContext context)
    {
        // Finding 3 zero-guard: an unset/zero/negative event intensity means "no modifier" (the catalog
        // baseIntensity drives), preserving the legacy `intensity <= 0 ? default` behavior.
        var eventModifier = gameEvent.Intensity > 0 ? gameEvent.Intensity : 1.0;

        return GainStack.Compute(
            effect.BaseIntensity,
            eventModifier,
            context.ActiveShipProfile.EffectMultipliers.GetValueOrDefault(effect.EffectId!, 1.0),
            context.UserGains.CategoryGains.GetValueOrDefault(category, 1.0),
            context.UserGains.MasterGain,
            context.UserGains.DeviceGainMultipliers.GetValueOrDefault(context.DeviceCapabilities.Model, 1.0),
            context.DeviceCapabilities.MaxIntensityRecommended);
    }

    // Catalog EffectDefinition -> Core ForceEffect (T-13-deferred translation). Returns false + a reason for
    // a shape the loader should have rejected (defense-in-depth).
    private static bool TryTranslate(EffectDefinition effect, out ForceEffect forceEffect, out string? reason)
    {
        forceEffect = null!;

        if (!Enum.TryParse<EffectCategory>(effect.Category, ignoreCase: true, out var category))
        {
            reason = $"invalid category '{effect.Category}'";
            return false;
        }

        if (!Enum.TryParse<ForceEffectType>(effect.EffectType, ignoreCase: true, out var effectType))
        {
            reason = $"invalid effectType '{effect.EffectType}'";
            return false;
        }

        forceEffect = new ForceEffect
        {
            EffectId = effect.EffectId!,
            EffectType = effectType,
            Category = category,
            BaseIntensity = effect.BaseIntensity,
            FrequencyHz = effect.FrequencyHz,
            Duration = TimeSpan.FromMilliseconds(effect.DurationMs),
            DirectionX = effect.DirectionX,
            DirectionY = effect.DirectionY,
            Envelope = TranslateEnvelope(effect.Envelope),
            IsSustained = effect.IsSustained,
            StateKey = effect.StateKey,
        };
        reason = null;
        return true;
    }

    private static ForceEnvelope? TranslateEnvelope(EffectEnvelopeDefinition? envelope) =>
        envelope is null
            ? null
            : new ForceEnvelope(
                TimeSpan.FromMilliseconds(envelope.AttackMs),
                TimeSpan.FromMilliseconds(envelope.HoldMs),
                TimeSpan.FromMilliseconds(envelope.DecayMs),
                TimeSpan.FromMilliseconds(envelope.ReleaseMs),
                envelope.AttackLevel,
                envelope.SustainLevel);

    private static bool IsStopType(string? effectType) =>
        string.Equals(effectType, "Stop", StringComparison.OrdinalIgnoreCase);

    private static PlayEffectCommand PlayCommand(ForceEffect effect, double finalIntensity) =>
        new(effect, finalIntensity)
        {
            CommandId = Guid.NewGuid().ToString(),
            IssuedAt = DateTimeOffset.UtcNow,
        };

    private static StopEffectCommand StopCommand(string stateKey) =>
        new(stateKey)
        {
            CommandId = Guid.NewGuid().ToString(),
            IssuedAt = DateTimeOffset.UtcNow,
        };
}
