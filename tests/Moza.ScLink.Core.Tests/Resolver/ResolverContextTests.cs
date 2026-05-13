using FluentAssertions;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Resolver;
using Moza.ScLink.Core.Safety;

namespace Moza.ScLink.Core.Tests.Resolver;

public sealed class ResolverContextTests
{
    private static DeviceCapabilities BuildCapabilities() => new(
        AxisCount: 2,
        SimultaneousEffectCount: 4,
        SupportsConstantForce: true,
        SupportsPeriodic: true,
        SupportsEnvelope: true,
        MaxGain: 10000,
        MaxIntensityRecommended: 0.85);

    // ── UserGains ───────────────────────────────────────────────────────────────

    [Fact]
    public void UserGainsDefaultMasterGainIsDefaultMasterGainConstant()
    {
        var gains = new UserGains();
        gains.MasterGain.Should().Be(SafetyLimits.DefaultMasterGain);
    }

    [Fact]
    public void UserGainsDefaultCategoryGainsIsEmpty()
    {
        var gains = new UserGains();
        gains.CategoryGains.Should().BeEmpty();
    }

    [Fact]
    public void UserGainsDefaultDeviceGainMultipliersIsEmpty()
    {
        var gains = new UserGains();
        gains.DeviceGainMultipliers.Should().BeEmpty();
    }

    [Fact]
    public void UserGainsCategoryGainsStoresPerCategoryMultiplier()
    {
        var gains = new UserGains
        {
            CategoryGains = ImmutableDictionary<EffectCategory, double>.Empty
                .Add(EffectCategory.Combat, 0.8)
                .Add(EffectCategory.Flight, 1.2),
        };

        gains.CategoryGains[EffectCategory.Combat].Should().Be(0.8);
        gains.CategoryGains[EffectCategory.Flight].Should().Be(1.2);
        gains.CategoryGains.GetValueOrDefault(EffectCategory.Environment, 1.0).Should().Be(1.0);
    }

    [Fact]
    public void UserGainsDeviceGainMultipliersStoresPerDeviceCalibration()
    {
        var gains = new UserGains
        {
            DeviceGainMultipliers = ImmutableDictionary<DeviceModel, double>.Empty
                .Add(DeviceModel.MozaAb6, 0.9)
                .Add(DeviceModel.MozaAb9, 1.1),
        };

        gains.DeviceGainMultipliers[DeviceModel.MozaAb6].Should().Be(0.9);
        gains.DeviceGainMultipliers[DeviceModel.MozaAb9].Should().Be(1.1);
        gains.DeviceGainMultipliers.GetValueOrDefault(DeviceModel.Unknown, 1.0).Should().Be(1.0);
    }

    // ── ShipProfile ─────────────────────────────────────────────────────────────

    [Fact]
    public void ShipProfileRequiredFieldsAreRetained()
    {
        var profile = new ShipProfile
        {
            ShipId = "hornet-f7c",
            DisplayName = "F7C Hornet",
        };

        profile.ShipId.Should().Be("hornet-f7c");
        profile.DisplayName.Should().Be("F7C Hornet");
    }

    [Fact]
    public void ShipProfileDefaultEffectMultipliersIsEmpty()
    {
        var profile = new ShipProfile { ShipId = "default", DisplayName = "Default" };
        profile.EffectMultipliers.Should().BeEmpty();
    }

    [Fact]
    public void ShipProfileEffectMultipliersGetValueOrDefaultReturnsFallbackForMissingKey()
    {
        var profile = new ShipProfile
        {
            ShipId = "aurora-mr",
            DisplayName = "Aurora MR",
            EffectMultipliers = ImmutableDictionary<string, double>.Empty
                .Add("effect.weapon_fire_ballistic", 0.75),
        };

        profile.EffectMultipliers.GetValueOrDefault("effect.weapon_fire_ballistic", 1.0).Should().Be(0.75);
        profile.EffectMultipliers.GetValueOrDefault("effect.unknown", 1.0).Should().Be(1.0);
    }

    // ── ResolverContext ─────────────────────────────────────────────────────────

    [Fact]
    public void ResolverContextPositionalConstructionPopulatesAllProperties()
    {
        var profile = new ShipProfile { ShipId = "default", DisplayName = "Default" };
        var gains = new UserGains();
        var caps = BuildCapabilities();
        var now = new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);

        var ctx = new ResolverContext(profile, gains, caps, now);

        ctx.ActiveShipProfile.Should().BeSameAs(profile);
        ctx.UserGains.Should().Be(gains);
        ctx.DeviceCapabilities.Should().Be(caps);
        ctx.Now.Should().Be(now);
    }
}
