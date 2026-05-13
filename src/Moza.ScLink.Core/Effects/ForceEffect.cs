using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Core.Effects;

/// <summary>Immutable descriptor for a force-feedback effect as defined in the effect catalog.</summary>
public sealed record ForceEffect
{
    /// <summary>Catalog key that uniquely identifies this effect definition.</summary>
    public required string EffectId { get; init; }

    /// <summary>DirectInput effect type used to render this effect.</summary>
    public required ForceEffectType EffectType { get; init; }

    /// <summary>Broad category used for gain scaling and suppression.</summary>
    public required EffectCategory Category { get; init; }

    /// <summary>Nominal intensity in [0.0, 1.0] before the gain stack is applied.</summary>
    public double BaseIntensity { get; init; }

    /// <summary>Frequency in Hz; 0 for constant-force effects.</summary>
    public double FrequencyHz { get; init; }

    /// <summary>Duration of the effect; <see cref="TimeSpan.Zero"/> means sustained until explicitly stopped.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Horizontal force direction component in [-1.0, 1.0].</summary>
    public double DirectionX { get; init; }

    /// <summary>Vertical force direction component in [-1.0, 1.0].</summary>
    public double DirectionY { get; init; }

    /// <summary>Optional ADSR envelope; <see langword="null"/> for effects without shaping.</summary>
    public ForceEnvelope? Envelope { get; init; }

    /// <summary><see langword="true"/> if the effect plays continuously until an explicit stop command.</summary>
    public bool IsSustained { get; init; }

    /// <summary>State key used to identify and stop sustained effects; <see langword="null"/> for non-sustained effects.</summary>
    public string? StateKey { get; init; }
}
