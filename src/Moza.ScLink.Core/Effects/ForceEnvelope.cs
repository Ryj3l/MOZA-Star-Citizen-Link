namespace Moza.ScLink.Core.Effects;

/// <summary>ADSR-style envelope parameters applied to a <see cref="ForceEffect"/> to shape its intensity over time.</summary>
/// <param name="Attack">Duration of the ramp-up from zero to <paramref name="AttackLevel"/>.</param>
/// <param name="Hold">Duration to hold at <paramref name="AttackLevel"/> before decay begins.</param>
/// <param name="Decay">Duration of the ramp from <paramref name="AttackLevel"/> to <paramref name="SustainLevel"/>.</param>
/// <param name="Release">Duration of the ramp-down from <paramref name="SustainLevel"/> to zero on stop.</param>
/// <param name="AttackLevel">Peak intensity level reached at the end of the attack phase, in [0.0, 1.0].</param>
/// <param name="SustainLevel">Steady-state intensity level held during the sustain phase, in [0.0, 1.0].</param>
public sealed record ForceEnvelope(
    TimeSpan Attack,
    TimeSpan Hold,
    TimeSpan Decay,
    TimeSpan Release,
    double AttackLevel,
    double SustainLevel);
