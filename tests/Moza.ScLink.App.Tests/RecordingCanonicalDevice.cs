using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.App.Tests;

/// <summary>
/// Minimal canonical <see cref="IForceFeedbackDevice"/> test double for the T-27 App-layer tests:
/// records InitializeAsync/ExecuteAsync/StopAllAsync calls and raises StateChanged on initialization
/// (so the gutted MainViewModel's Output-row subscription can be exercised).
/// </summary>
internal sealed class RecordingCanonicalDevice : IForceFeedbackDevice
{
    public int InitializeCount { get; private set; }

    public List<ForceCommand> Executed { get; } = [];

    public int StopAllCount { get; private set; }

    public DeviceModel Model => DeviceModel.MozaAb6;

    public string DisplayName { get; init; } = "Test device";

    public string ProductName => "MOZA AB6 (test)";

    public Guid InstanceGuid { get; } = Guid.NewGuid();

    public DeviceCapabilities Capabilities { get; } =
        new(DeviceModel.MozaAb6, AxisCount: 2, SimultaneousEffectCount: 4,
            SupportsConstantForce: true, SupportsPeriodic: true, SupportsEnvelope: true,
            MaxGain: 10000, MaxIntensityRecommended: 0.85);

    public DeviceState State { get; private set; } = DeviceState.Disconnected;

    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        InitializeCount++;
        var previous = State;
        State = DeviceState.Ready;
        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs { Previous = previous, Current = State });
        return Task.CompletedTask;
    }

    public Task ExecuteAsync(ForceCommand command, CancellationToken cancellationToken)
    {
        Executed.Add(command);
        return Task.CompletedTask;
    }

    public Task StopAllAsync(CancellationToken cancellationToken)
    {
        StopAllCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
