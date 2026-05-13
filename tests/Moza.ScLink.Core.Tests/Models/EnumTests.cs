using FluentAssertions;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Core.Tests.Models;

public sealed class EnumTests
{
    [Fact]
    public void SensorKindHasExactlyFourValues()
    {
        var values = Enum.GetValues<SensorKind>();
        values.Should().HaveCount(4);
        values.Should().Contain(SensorKind.Log);
        values.Should().Contain(SensorKind.Audio);
        values.Should().Contain(SensorKind.Screen);
        values.Should().Contain(SensorKind.Input);
    }

    [Fact]
    public void GameEventTypeHasExactlyNineteenValues()
    {
        var values = Enum.GetValues<GameEventType>();
        values.Should().HaveCount(19);
        // Quantum (4)
        values.Should().Contain(GameEventType.QuantumSpoolStarted);
        values.Should().Contain(GameEventType.QuantumSpoolEnded);
        values.Should().Contain(GameEventType.QuantumJumpStarted);
        values.Should().Contain(GameEventType.QuantumJumpExit);
        // Atmosphere (3)
        values.Should().Contain(GameEventType.AtmosphereEntered);
        values.Should().Contain(GameEventType.AtmosphereExited);
        values.Should().Contain(GameEventType.AtmosphericBuffet);
        // Landing/Impact (3)
        values.Should().Contain(GameEventType.LandingGearContact);
        values.Should().Contain(GameEventType.HullImpact);
        values.Should().Contain(GameEventType.VehicleDestruction);
        // Combat (5)
        values.Should().Contain(GameEventType.WeaponFireBallistic);
        values.Should().Contain(GameEventType.WeaponFireEnergy);
        values.Should().Contain(GameEventType.MissileLaunch);
        values.Should().Contain(GameEventType.ShieldHit);
        values.Should().Contain(GameEventType.HullDamage);
        // Misc (1)
        values.Should().Contain(GameEventType.ThrusterActivation);
        // System (3)
        values.Should().Contain(GameEventType.SessionStarted);
        values.Should().Contain(GameEventType.SessionEnded);
        values.Should().Contain(GameEventType.EmergencyStop);
    }

    [Fact]
    public void EffectCategoryHasExactlyFiveValues()
    {
        var values = Enum.GetValues<EffectCategory>();
        values.Should().HaveCount(5);
        values.Should().Contain(EffectCategory.Combat);
        values.Should().Contain(EffectCategory.Flight);
        values.Should().Contain(EffectCategory.Environment);
        values.Should().Contain(EffectCategory.Ui);
        values.Should().Contain(EffectCategory.System);
    }

    [Fact]
    public void ForceEffectTypeHasExactlyFourValues()
    {
        var values = Enum.GetValues<ForceEffectType>();
        values.Should().HaveCount(4);
        values.Should().Contain(ForceEffectType.Periodic);
        values.Should().Contain(ForceEffectType.ConstantForce);
        values.Should().Contain(ForceEffectType.PeriodicWithEnvelope);
        values.Should().Contain(ForceEffectType.Composite);
    }

    [Fact]
    public void DeviceModelHasExactlyThreeValues()
    {
        var values = Enum.GetValues<DeviceModel>();
        values.Should().HaveCount(3);
        values.Should().Contain(DeviceModel.Unknown);
        values.Should().Contain(DeviceModel.MozaAb6);
        values.Should().Contain(DeviceModel.MozaAb9);
    }

    [Fact]
    public void SensorStateHasExactlyFiveValues()
    {
        var values = Enum.GetValues<SensorState>();
        values.Should().HaveCount(5);
        values.Should().Contain(SensorState.Stopped);
        values.Should().Contain(SensorState.Starting);
        values.Should().Contain(SensorState.Running);
        values.Should().Contain(SensorState.Faulted);
        values.Should().Contain(SensorState.Stopping);
    }

    [Fact]
    public void DeviceStateHasExactlyFiveValues()
    {
        var values = Enum.GetValues<DeviceState>();
        values.Should().HaveCount(5);
        values.Should().Contain(DeviceState.Disconnected);
        values.Should().Contain(DeviceState.Detecting);
        values.Should().Contain(DeviceState.Initializing);
        values.Should().Contain(DeviceState.Ready);
        values.Should().Contain(DeviceState.Faulted);
    }
}
