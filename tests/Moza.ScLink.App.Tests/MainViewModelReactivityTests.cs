using System.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.App.ViewModels;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;
using Moza.ScLink.DirectInput;
using Moza.ScLink.Effects;
using LegacyDevice = Moza.ScLink.Core.IForceFeedbackDevice;
using NewDevice = Moza.ScLink.Core.Devices.IForceFeedbackDevice;

// LegacyForceFeedbackDeviceAdapter is [Obsolete] transitional; this file is its intended composition
// site for the test fixture.
#pragma warning disable CS0618

namespace Moza.ScLink.App.Tests;

/// <summary>
/// Pins T-07 Issue #27 Pass-2 §D4: <see cref="MainViewModel"/> must react to chain-state transitions
/// raised on the injected <see cref="ForceFeedbackController"/>, marshal the update onto the UI
/// dispatcher, and unsubscribe cleanly on <see cref="MainViewModel.Dispose"/>. The fixture composes
/// a real Fallback chain — fake DI adapter + Null tier — so the chain's actual
/// <c>OnDeviceRemoved</c> path drives the event, exercising the full end-to-end flow.
/// </summary>
[Collection(nameof(WpfApplicationFixtureScope))]
public sealed class MainViewModelReactivityTests
{
    /// <summary>
    /// Composes a Fallback chain + controller + view model. The Fallback starts initialized with the
    /// DI tier current. Returns the observer so tests can drive transitions.
    /// </summary>
    private static MainViewModel CreateVmWithRealChain(out IDeviceAvailabilityObserver observer)
    {
        var stub = new StubNewDevice("MOZA AB9 FFB Base");
        var adapter = new LegacyForceFeedbackDeviceAdapter(
            stub,
            NullLogger<LegacyForceFeedbackDeviceAdapter>.Instance);
        var nullTier = new NullForceFeedbackDevice("test-null-tier");
        var fallback = new FallbackForceFeedbackDevice(new LegacyDevice[] { adapter, nullTier });
        fallback.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var controller = new ForceFeedbackController(fallback);
        observer = controller.DeviceAvailabilityObserver
            ?? throw new InvalidOperationException("DeviceAvailabilityObserver passthrough returned null");
        return new MainViewModel(controller);
    }

    private static async Task<string> WaitForPropertyAsync(
        MainViewModel vm,
        string propertyName,
        Func<MainViewModel, string> read,
        int timeoutMs = 2000)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == propertyName)
            {
                tcs.TrySetResult(read(vm));
            }
        }

        vm.PropertyChanged += Handler;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            Assert.Equal(tcs.Task, completed);
            return await tcs.Task;
        }
        finally
        {
            vm.PropertyChanged -= Handler;
        }
    }

    [Fact]
    public async Task OutputNameUpdatesOnChainStateChanged()
    {
        using var vm = CreateVmWithRealChain(out var observer);
        var initialName = vm.OutputName;
        Assert.Contains("DirectInput", initialName);

        var waitTask = WaitForPropertyAsync(vm, nameof(MainViewModel.OutputName), v => v.OutputName);
        observer.OnDeviceRemoved();
        var updated = await waitTask;

        Assert.NotEqual(initialName, updated);
        Assert.Contains("Preview", updated);
    }

    [Fact]
    public async Task OutputStatusUpdatesOnChainStateChanged()
    {
        using var vm = CreateVmWithRealChain(out var observer);
        var initialStatus = vm.OutputStatus;

        var waitTask = WaitForPropertyAsync(vm, nameof(MainViewModel.OutputStatus), v => v.OutputStatus);
        observer.OnDeviceRemoved();
        var updated = await waitTask;

        Assert.NotEqual(initialStatus, updated);
    }

    [Fact]
    public async Task DisposeUnsubscribesFromChainStateChanged()
    {
        var vm = CreateVmWithRealChain(out var observer);
        var sawPropertyChange = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.OutputName)
                || e.PropertyName == nameof(MainViewModel.OutputStatus))
            {
                Interlocked.Increment(ref sawPropertyChange);
            }
        };

        vm.Dispose();
        await Task.Delay(50);  // let any pending dispose work settle

        // Reset the counter — Dispose may have triggered transitions of its own. The contract under
        // test is "no FURTHER updates after dispose," not "zero updates ever."
        Interlocked.Exchange(ref sawPropertyChange, 0);

        observer.OnDeviceRemoved();
        await Task.Delay(300);  // generous wait for any leaked subscription to fire

        Assert.Equal(0, sawPropertyChange);
    }

    [Fact]
    public async Task OnChainStateChangedDispatchesToUiThread()
    {
        // The WpfApplicationFixture sets up a dedicated STA Dispatcher on a background thread; the
        // xUnit test thread is different. MainViewModel.Dispatch routes to that Dispatcher when not
        // already on it. The test fires the transition from the xUnit thread and asserts the
        // PropertyChanged handler ran on the Dispatcher's thread (not the test thread).
        using var vm = CreateVmWithRealChain(out var observer);

        var testThreadId = Environment.CurrentManagedThreadId;
        var handlerThreadId = 0;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.OutputName))
            {
                Interlocked.Exchange(ref handlerThreadId, Environment.CurrentManagedThreadId);
                tcs.TrySetResult(true);
            }
        };

        observer.OnDeviceRemoved();
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Equal(tcs.Task, completed);

        Assert.NotEqual(0, handlerThreadId);
        Assert.NotEqual(testThreadId, handlerThreadId);
    }

    // ── Test doubles ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal hand-rolled <see cref="NewDevice"/> stub. App.Tests intentionally avoids NSubstitute to
    /// keep its dependency surface minimal; the stub covers the few interface members the chain
    /// exercises during init and disposal.
    /// </summary>
    private sealed class StubNewDevice : NewDevice
    {
        public StubNewDevice(string name)
        {
            DisplayName = name;
            ProductName = name;
        }

        public DeviceModel Model => DeviceModel.MozaAb9;
        public string DisplayName { get; }
        public string ProductName { get; }
        public Guid InstanceGuid { get; } = Guid.NewGuid();
        public DeviceCapabilities Capabilities { get; } =
            new(Model: DeviceModel.MozaAb9,
                AxisCount: 2,
                SimultaneousEffectCount: 4,
                SupportsConstantForce: true,
                SupportsPeriodic: true,
                SupportsEnvelope: false,
                MaxGain: 10000,
                MaxIntensityRecommended: 0.85);
        public DeviceState State => DeviceState.Ready;

#pragma warning disable CS0067  // event is part of the interface contract; not raised in this stub
        public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;
#pragma warning restore CS0067

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteAsync(ForceCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAllAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
