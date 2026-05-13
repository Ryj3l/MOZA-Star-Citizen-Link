namespace Moza.ScLink.Core.Models;

/// <summary>Legacy pre-migration effect kind. Superseded by <see cref="Moza.ScLink.Core.Models.ForceEffectType"/> in T-06.</summary>
public enum ForceEffectKind
{
    /// <summary>Periodic vibration effect.</summary>
    PeriodicVibration,
    /// <summary>Short bump/transient effect.</summary>
    Bump,
    /// <summary>Sustained state vibration effect.</summary>
    StateVibration,
}

/// <summary>Legacy pre-migration force effect record. Superseded by <see cref="Moza.ScLink.Core.Effects.ForceEffect"/> in T-06.</summary>
public sealed record ForceEffect(
    ForceEffectKind Kind,
    string Name,
    double Intensity,
    TimeSpan Duration,
    double FrequencyHz,
    string? StateKey);
