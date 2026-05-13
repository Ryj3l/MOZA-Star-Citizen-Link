namespace Moza.ScLink.Core.Resolver;

/// <summary>Per-ship intensity multipliers applied in the gain stack per PRP §5.9. Phase 1 uses the empty (all-ones) profile.</summary>
public sealed record ShipProfile
{
    /// <summary>Unique ship identifier, e.g. "default", "aurora-mr", "hornet-f7c".</summary>
    public required string ShipId { get; init; }

    /// <summary>Human-readable display name for UI presentation.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Per-effect intensity multipliers keyed by <see cref="Moza.ScLink.Core.Effects.ForceEffect.EffectId"/>.
    /// The resolver uses <c>GetValueOrDefault(effectId, 1.0)</c> — missing keys default to 1.0.
    /// </summary>
    public ImmutableDictionary<string, double> EffectMultipliers { get; init; }
        = ImmutableDictionary<string, double>.Empty;
}
