using FluentAssertions;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Core.Tests.Devices;

public sealed class DeviceTypesTests
{
    // ── DeviceCapabilities ──────────────────────────────────────────────────────

    [Fact]
    public void DeviceCapabilitiesPositionalConstructionPopulatesAllProperties()
    {
        var caps = new DeviceCapabilities(
            AxisCount: 2,
            SimultaneousEffectCount: 4,
            SupportsConstantForce: true,
            SupportsPeriodic: true,
            SupportsEnvelope: false,
            MaxGain: 10000,
            MaxIntensityRecommended: 0.85);

        caps.AxisCount.Should().Be(2);
        caps.SimultaneousEffectCount.Should().Be(4);
        caps.SupportsConstantForce.Should().BeTrue();
        caps.SupportsPeriodic.Should().BeTrue();
        caps.SupportsEnvelope.Should().BeFalse();
        caps.MaxGain.Should().Be(10000);
        caps.MaxIntensityRecommended.Should().Be(0.85);
    }

    [Fact]
    public void DeviceCapabilitiesEqualityTwoInstancesWithSameValuesAreEqual()
    {
        var c1 = new DeviceCapabilities(2, 4, true, true, true, 10000, 0.85);
        var c2 = new DeviceCapabilities(2, 4, true, true, true, 10000, 0.85);

        c1.Should().Be(c2);
    }

    [Fact]
    public void DeviceCapabilitiesEqualityTwoInstancesWithDifferentValuesAreNotEqual()
    {
        var c1 = new DeviceCapabilities(2, 4, true, true, true, 10000, 0.85);
        var c2 = new DeviceCapabilities(1, 2, false, false, false, 5000, 0.70);

        c1.Should().NotBe(c2);
    }

    // ── DeviceStateChangedEventArgs ─────────────────────────────────────────────

    [Fact]
    public void DeviceStateChangedEventArgsStoresPreviousAndCurrent()
    {
        var args = new DeviceStateChangedEventArgs
        {
            Previous = DeviceState.Initializing,
            Current = DeviceState.Ready,
        };

        args.Previous.Should().Be(DeviceState.Initializing);
        args.Current.Should().Be(DeviceState.Ready);
    }
}
