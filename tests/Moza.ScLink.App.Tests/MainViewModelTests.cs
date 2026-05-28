using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.App.ForceFeedback;
using Moza.ScLink.App.GameLog;
using Moza.ScLink.App.ViewModels;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Safety;
using Moza.ScLink.Core.Sensors;
using Moza.ScLink.Diagnostics;
using Moza.ScLink.Effects;
using Moza.ScLink.Profiles.Settings;

namespace Moza.ScLink.App.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public MainViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "moza-sclink-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private AppSettingsStore NewStore() => new(_settingsPath);

    private MainViewModel Create(
        IEventBus bus,
        StubGameLogPathProvider? provider = null,
        IForceFeedbackDevice? device = null,
        AppSettingsStore? store = null,
        IEmergencyStop? emergencyStop = null) =>
        new(bus, provider ?? new StubGameLogPathProvider(), device ?? new RecordingCanonicalDevice(), store ?? NewStore(),
            emergencyStop ?? new EmergencyStop(NullLogger<EmergencyStop>.Instance));

    private static PreviewForceFeedbackDevice NewPreviewDevice() =>
        new(NullLogger<PreviewForceFeedbackDevice>.Instance);

    private static StopAllCommand StopAllAt(DateTimeOffset issuedAt) =>
        new() { CommandId = Guid.NewGuid().ToString(), IssuedAt = issuedAt };

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

    // ── T-17 preview-mode VM behavior ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsPreviewModeIsFalseForARealHardwareDevice()
    {
        // RecordingCanonicalDevice does not implement IPreviewCommandSource → banner hidden.
        using var vm = Create(new EventBus(), device: new RecordingCanonicalDevice());

        Assert.False(vm.IsPreviewMode);
    }

    [Fact]
    public void IsPreviewModeIsTrueForThePreviewDevice()
    {
        using var vm = Create(new EventBus(), device: NewPreviewDevice());

        Assert.True(vm.IsPreviewMode);
    }

    [Fact]
    public async Task PreviewCommandsPopulateNewestFirst()
    {
        var device = NewPreviewDevice();
        using var vm = Create(new EventBus(), device: device);

        await device.ExecuteAsync(StopAllAt(DateTimeOffset.UnixEpoch), CancellationToken.None);
        await device.ExecuteAsync(StopAllAt(DateTimeOffset.UnixEpoch.AddSeconds(1)), CancellationToken.None);
        await device.ExecuteAsync(StopAllAt(DateTimeOffset.UnixEpoch.AddSeconds(2)), CancellationToken.None);

        Assert.Equal(3, vm.PreviewCommands.Count);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(2), vm.PreviewCommands[0].WallClock); // newest first
        Assert.Equal(DateTimeOffset.UnixEpoch, vm.PreviewCommands[2].WallClock);
    }

    [Fact]
    public async Task PreviewCommandsAreCappedAtFifty()
    {
        var device = NewPreviewDevice();
        using var vm = Create(new EventBus(), device: device);

        for (var i = 0; i < 60; i++)
        {
            await device.ExecuteAsync(StopAllAt(DateTimeOffset.UnixEpoch.AddSeconds(i)), CancellationToken.None);
        }

        Assert.Equal(50, vm.PreviewCommands.Count);
        // Newest (i=59) at the head; the 50-deep tail is i=10.
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(59), vm.PreviewCommands[0].WallClock);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(10), vm.PreviewCommands[^1].WallClock);
    }

    [Fact]
    public void ForcePreviewModeInitializesFromStore()
    {
        var store = NewStore();
        store.Save(new AppSettings { ForcePreviewMode = true });

        using var vm = Create(new EventBus(), store: store);

        Assert.True(vm.ForcePreviewMode);
    }

    [Fact]
    public void SettingForcePreviewModePersistsAndPreservesGameLogPath()
    {
        var store = NewStore();
        store.Save(new AppSettings { GameLogPath = @"C:\sc\Game.log" });
        using var vm = Create(new EventBus(), store: store);

        vm.ForcePreviewMode = true;

        var reloaded = NewStore().Load();
        Assert.True(reloaded.ForcePreviewMode);
        Assert.Equal(@"C:\sc\Game.log", reloaded.GameLogPath); // sibling field preserved via Update
    }

    [Fact]
    public async Task EmergencyStopActivationFlipsStateAndGatesCommands()
    {
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        using var vm = Create(new EventBus(), emergencyStop: estop);

        Assert.False(vm.IsEmergencyStopActive);
        Assert.True(vm.ActivateEmergencyStopCommand.CanExecute(null));
        Assert.False(vm.ClearEmergencyStopCommand.CanExecute(null));

        await estop.ActivateAsync("test");

        Assert.True(vm.IsEmergencyStopActive);
        Assert.False(vm.ActivateEmergencyStopCommand.CanExecute(null)); // disabled while active
        Assert.True(vm.ClearEmergencyStopCommand.CanExecute(null));
    }

    [Fact]
    public async Task EmergencyStopClearResetsStateAndGatesCommands()
    {
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        using var vm = Create(new EventBus(), emergencyStop: estop);
        await estop.ActivateAsync("test");

        await estop.ClearAsync();

        Assert.False(vm.IsEmergencyStopActive);
        Assert.True(vm.ActivateEmergencyStopCommand.CanExecute(null));
        Assert.False(vm.ClearEmergencyStopCommand.CanExecute(null)); // disabled once cleared
    }

    [Fact]
    public async Task EmergencyStopActivationRaisesCanExecuteChangedOnBothCommands()
    {
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        using var vm = Create(new EventBus(), emergencyStop: estop);
        var activateRaised = 0;
        var clearRaised = 0;
        vm.ActivateEmergencyStopCommand.CanExecuteChanged += (_, _) => activateRaised++;
        vm.ClearEmergencyStopCommand.CanExecuteChanged += (_, _) => clearRaised++;

        await estop.ActivateAsync("test");

        Assert.True(activateRaised > 0);
        Assert.True(clearRaised > 0);
    }

    [Fact]
    public async Task EmergencyStopActivationAddsEventFeedEntry()
    {
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        using var vm = Create(new EventBus(), emergencyStop: estop);

        await estop.ActivateAsync("test");

        Assert.Contains(vm.Events, e => e.Contains("EMERGENCY STOP", StringComparison.Ordinal));
    }

    [Fact]
    public void ActivateEmergencyStopCommandDrivesTheAuthority()
    {
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        using var vm = Create(new EventBus(), emergencyStop: estop);

        vm.ActivateEmergencyStopCommand.Execute(null);

        Assert.True(estop.IsActive);
        Assert.True(vm.IsEmergencyStopActive);
    }
}
