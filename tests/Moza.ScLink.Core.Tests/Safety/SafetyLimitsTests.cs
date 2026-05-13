using FluentAssertions;
using Moza.ScLink.Core.Safety;

namespace Moza.ScLink.Core.Tests.Safety;

// Intentionally brittle: if PRP §5.8 changes a constant this test fails. That is the point.
public sealed class SafetyLimitsTests
{
    [Fact]
    public void MinIntensityIsZero() =>
        SafetyLimits.MinIntensity.Should().Be(0.0);

    [Fact]
    public void MaxIntensityIsOne() =>
        SafetyLimits.MaxIntensity.Should().Be(1.0);

    [Fact]
    public void DefaultMasterGainIsPointSix() =>
        SafetyLimits.DefaultMasterGain.Should().Be(0.6);

    [Fact]
    public void MaxIntensityRateOfChangePerSecondIsFour() =>
        SafetyLimits.MaxIntensityRateOfChangePerSecond.Should().Be(4.0);

    [Fact]
    public void MaxSustainedIntensityIsPointSeven() =>
        SafetyLimits.MaxSustainedIntensity.Should().Be(0.7);

    [Fact]
    public void MaxSimultaneousEffectsIsFour() =>
        SafetyLimits.MaxSimultaneousEffects.Should().Be(4);

    [Fact]
    public void StartupRampMsIsTwoHundredFifty() =>
        SafetyLimits.StartupRampMs.Should().Be(250);

    [Fact]
    public void StopRampMsIsOneHundredFifty() =>
        SafetyLimits.StopRampMs.Should().Be(150);

    [Fact]
    public void EmergencyStopMaxLatencyMsIsFifty() =>
        SafetyLimits.EmergencyStopMaxLatencyMs.Should().Be(50);

    [Fact]
    public void AbsoluteMaxEffectDurationIsTenMinutes() =>
        SafetyLimits.AbsoluteMaxEffectDuration.Should().Be(TimeSpan.FromMinutes(10));
}
