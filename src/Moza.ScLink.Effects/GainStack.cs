using System.Diagnostics.CodeAnalysis;
using Moza.ScLink.Core.Safety;

namespace Moza.ScLink.Effects;

/// <summary>
/// Pure gain-stack computation (PRP §5.9): the product of the six gain factors, clamped to the device's
/// recommended ceiling.
/// <para>
/// SCOPE: §5.9's pipeline also lists <c>applyRateOfChangeLimit()</c> and <c>applySustainedCap()</c>, but
/// those are <b>stateful</b> (they need the prior command, the effect duration, and the active-effect set)
/// and are owned by T-15's <c>SafetyLimiter</c> (§5.8 constants). This function is deliberately state-free —
/// its signature carries no history — so it implements only the §5.9 multiply-and-clamp. T-14 = gain stack;
/// T-15 = rate-of-change + sustained cap. The two compose: resolver → gain stack → safety limiter → channel.
/// </para>
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "\"Gain stack\" is the canonical domain term named verbatim in PRP §5.9 " +
                    "(\"Reference implementation lives in Moza.ScLink.Effects/GainStack.cs\") and T-14.md " +
                    "deliverable #2. It is a pure computation, not a stack collection; the rule's guard " +
                    "against confusion with System.Collections stack types does not apply.")]
public static class GainStack
{
    /// <summary>
    /// Computes final intensity as the product of the six gain factors, clamped to
    /// [<see cref="SafetyLimits.MinIntensity"/>, <paramref name="deviceMaxIntensityRecommended"/>].
    /// </summary>
    /// <param name="baseEffectIntensity">Catalog base intensity for the effect.</param>
    /// <param name="eventIntensityModifier">Per-event modifier (from <c>GameEvent.Intensity</c>; the resolver passes 1.0 when the event carries none).</param>
    /// <param name="shipProfileMultiplier">Active ship profile's per-effect multiplier.</param>
    /// <param name="categoryGain">User per-category gain.</param>
    /// <param name="masterGain">User master gain.</param>
    /// <param name="deviceGainMultiplier">Per-device calibration multiplier.</param>
    /// <param name="deviceMaxIntensityRecommended">Device-specific safety ceiling; the upper clamp bound.</param>
    public static double Compute(
        double baseEffectIntensity,
        double eventIntensityModifier,
        double shipProfileMultiplier,
        double categoryGain,
        double masterGain,
        double deviceGainMultiplier,
        double deviceMaxIntensityRecommended)
    {
        var raw = baseEffectIntensity
            * eventIntensityModifier
            * shipProfileMultiplier
            * categoryGain
            * masterGain
            * deviceGainMultiplier;

        return Math.Clamp(raw, SafetyLimits.MinIntensity, deviceMaxIntensityRecommended);
    }
}
