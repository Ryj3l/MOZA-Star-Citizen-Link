using Moza.ScLink.App.GameLog;
using Moza.ScLink.App.ViewModels;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Sensors;
using Moza.ScLink.Diagnostics;

namespace Moza.ScLink.App.Tests;

public sealed class MainViewModelTests
{
    private static MainViewModel Create(
        IEventBus bus,
        StubGameLogPathProvider? provider = null,
        RecordingCanonicalDevice? device = null) =>
        new(bus, provider ?? new StubGameLogPathProvider(), device ?? new RecordingCanonicalDevice());

    [Fact]
    public void ConstructorSurfacesDeviceIdentityAndState()
    {
        var device = new RecordingCanonicalDevice { DisplayName = "MOZA AB6" };

        using var vm = Create(new EventBus(), device: device);

        Assert.Equal("MOZA AB6", vm.OutputName);
        Assert.Equal(DeviceState.Disconnected.ToUserFacingString(), vm.OutputStatus);
    }

    [Fact]
    public void ConstructorAppliesStartupPathResolution()
    {
        var provider = new StubGameLogPathProvider
        {
            StartupResolution = new GameLogPathResolution("C:/sc/Game.log", GameLogPathOrigin.Saved),
        };

        using var vm = Create(new EventBus(), provider);

        Assert.Equal("C:/sc/Game.log", vm.GameLogPath);
    }

    [Fact]
    public void TestImpactCommandPublishesLandingImpactSensorEventToTheBus()
    {
        var bus = new EventBus();
        using var vm = Create(bus);

        vm.TestImpactCommand.Execute(null);

        Assert.True(bus.SensorEventReader.TryRead(out var sensorEvent));
        Assert.Equal("log.landing_impact_candidate", sensorEvent!.EventType);
        Assert.Equal(SensorKind.Log, sensorEvent.SensorKind);
        Assert.True(sensorEvent.Features.ContainsKey("relativeVelocityMagnitude"));
    }

    [Fact]
    public void TestQuantumAndAtmosphereCommandsPublishMatchingEventTypes()
    {
        var bus = new EventBus();
        using var vm = Create(bus);

        vm.TestQuantumCommand.Execute(null);
        vm.TestAtmosphereCommand.Execute(null);

        Assert.True(bus.SensorEventReader.TryRead(out var quantum));
        Assert.Equal("log.quantum_spool_start", quantum!.EventType);
        Assert.True(bus.SensorEventReader.TryRead(out var atmosphere));
        Assert.Equal("log.atmosphere_entered", atmosphere!.EventType);
    }
}
