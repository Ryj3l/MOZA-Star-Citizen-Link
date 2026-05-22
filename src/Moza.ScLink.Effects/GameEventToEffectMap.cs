using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Effects;

/// <summary>One Phase-1 mapping outcome for a <see cref="GameEventType"/> (T-14 deliverable #3).</summary>
/// <param name="EffectId">Catalog effect this entry refers to.</param>
public abstract record EffectMapEntry(string EffectId);

/// <summary>The event plays the catalog effect <see cref="EffectMapEntry.EffectId"/>.</summary>
public sealed record PlayEntry(string EffectId) : EffectMapEntry(EffectId);

/// <summary>
/// The event stops the sustained effect identified by <see cref="EffectMapEntry.EffectId"/>; the resolver
/// reads that effect's <c>StateKey</c> from the catalog and emits a <c>StopEffectCommand</c> for it.
/// Referencing the effect (not a raw state-key string) keeps every map entry catalog-validated.
/// </summary>
public sealed record StopEntry(string EffectId) : EffectMapEntry(EffectId);

/// <summary>Phase-1 <see cref="GameEventType"/> → catalog-effect mapping (PRP §5.7; T-14 deliverable #3).</summary>
public static class GameEventToEffectMap
{
    // Backing field is the concrete Dictionary (CA1859: avoid an interface-typed private field); the public
    // surface exposes IReadOnlyDictionary.
    private static readonly Dictionary<GameEventType, EffectMapEntry> Entries = new()
    {
        [GameEventType.QuantumSpoolStarted] = new PlayEntry("quantum-spool-v1"),
        [GameEventType.QuantumSpoolEnded] = new StopEntry("quantum-spool-v1"),
        [GameEventType.QuantumJumpExit] = new PlayEntry("quantum-jump-exit-v1"),
        [GameEventType.AtmosphereEntered] = new PlayEntry("atmosphere-entry-v1"),
        [GameEventType.AtmosphereExited] = new StopEntry("atmosphere-entry-v1"),
        [GameEventType.LandingGearContact] = new PlayEntry("landing-contact-v1"),
        [GameEventType.WeaponFireBallistic] = new PlayEntry("weapon-fire-generic-v1"),
        [GameEventType.WeaponFireEnergy] = new PlayEntry("weapon-fire-generic-v1"),
        [GameEventType.WeaponFireGeneric] = new PlayEntry("weapon-fire-generic-v1"),
        [GameEventType.VehicleDestruction] = new PlayEntry("vehicle-destruction-v1"),
    };

    /// <summary>Returns the mapping entry for <paramref name="eventType"/>, or <see langword="null"/> if unmapped.</summary>
    public static EffectMapEntry? TryGet(GameEventType eventType) => Entries.GetValueOrDefault(eventType);

    /// <summary>All mapped entries (diagnostics and map-vs-catalog validation).</summary>
    public static IReadOnlyDictionary<GameEventType, EffectMapEntry> All => Entries;
}
