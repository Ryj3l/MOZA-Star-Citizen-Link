using FluentAssertions;
using Moza.ScLink.Core.Safety;

namespace Moza.ScLink.Effects.Tests;

public sealed class GainStackTests
{
    // All-neutral factors (1.0) and a unit ceiling, so a test varies exactly one input.
    private static double Compute(
        double baseEffectIntensity = 0.5,
        double eventIntensityModifier = 1.0,
        double shipProfileMultiplier = 1.0,
        double categoryGain = 1.0,
        double masterGain = 1.0,
        double deviceGainMultiplier = 1.0,
        double deviceMaxIntensityRecommended = 1.0) =>
        GainStack.Compute(baseEffectIntensity, eventIntensityModifier, shipProfileMultiplier,
            categoryGain, masterGain, deviceGainMultiplier, deviceMaxIntensityRecommended);

    [Fact]
    public void MultipliesAllSixFactorsWhenWithinRange()
    {
        var result = GainStack.Compute(0.5, 0.8, 0.9, 0.5, 0.6, 1.0, 1.0);
        result.Should().BeApproximately(0.5 * 0.8 * 0.9 * 0.5 * 0.6 * 1.0, 1e-9);
    }

    [Fact]
    public void ClampsToDeviceCeilingWhenProductExceedsIt()
    {
        // 1.0 * 2.0 = 2.0, ceiling 0.85 -> 0.85.
        Compute(baseEffectIntensity: 1.0, eventIntensityModifier: 2.0, deviceMaxIntensityRecommended: 0.85)
            .Should().Be(0.85);
    }

    [Fact]
    public void ClampsToMinIntensityFloorWhenProductIsNegative()
    {
        // A negative factor drives the product below the floor -> clamped to MinIntensity (0.0).
        Compute(shipProfileMultiplier: -1.0).Should().Be(SafetyLimits.MinIntensity);
    }

    [Fact]
    public void ZeroFactorYieldsZero()
    {
        Compute(masterGain: 0.0).Should().Be(0.0);
    }

    [Fact]
    public void ProductAtExactCeilingIsNotClamped()
    {
        // 0.85 product, ceiling 0.85 -> 0.85 (boundary, in-range).
        Compute(baseEffectIntensity: 0.85, deviceMaxIntensityRecommended: 0.85).Should().Be(0.85);
    }

    [Theory]
    [InlineData(0.5, 1.0, 1.0, 1.0, 1.0, 1.0)]   // base drives
    [InlineData(1.0, 0.5, 1.0, 1.0, 1.0, 1.0)]   // event modifier drives
    [InlineData(1.0, 1.0, 0.5, 1.0, 1.0, 1.0)]   // ship profile drives
    [InlineData(1.0, 1.0, 1.0, 0.5, 1.0, 1.0)]   // category gain drives
    [InlineData(1.0, 1.0, 1.0, 1.0, 0.5, 1.0)]   // master gain drives
    [InlineData(1.0, 1.0, 1.0, 1.0, 1.0, 0.5)]   // device gain drives
    public void EachFactorParticipatesInTheProduct(
        double baseEffectIntensity, double eventIntensityModifier, double shipProfileMultiplier,
        double categoryGain, double masterGain, double deviceGainMultiplier)
    {
        // Exactly one factor is 0.5 and the rest 1.0, so the product is 0.5 — proving the halved factor
        // flows through. Distinguishing claim: each factor independently scales the result.
        GainStack.Compute(baseEffectIntensity, eventIntensityModifier, shipProfileMultiplier,
                categoryGain, masterGain, deviceGainMultiplier, deviceMaxIntensityRecommended: 1.0)
            .Should().BeApproximately(0.5, 1e-9);
    }
}
