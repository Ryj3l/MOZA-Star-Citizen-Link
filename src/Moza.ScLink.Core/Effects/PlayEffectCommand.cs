namespace Moza.ScLink.Core.Effects;

/// <summary>Commands the output worker to play a force-feedback effect at the resolved intensity.</summary>
/// <param name="Effect">The effect descriptor from the catalog.</param>
/// <param name="FinalIntensity">The gain-stack-resolved intensity in [0.0, 1.0].</param>
public sealed record PlayEffectCommand(
    ForceEffect Effect,
    double FinalIntensity) : ForceCommand;
