using System.Diagnostics;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.App.Tests;

/// <summary>
/// Minimal canonical <see cref="IForceFeedbackDevice"/> test double for the T-27 App-layer tests:
/// records InitializeAsync/ExecuteAsync/StopAllAsync calls and raises StateChanged on initialization
/// (so the gutted MainViewModel's Output-row subscription can be exercised).
/// <para>
/// Thread-safe: the T-27 E3 integration tests start the real generic host, whose ForceCommandPipeline
/// background loop calls <see cref="ExecuteAsync"/>/<see cref="StopAllAsync"/> while the test thread reads.
/// All recording state is guarded by <c>_gate</c>; read it across threads via <see cref="ExecutedSnapshot"/>
/// and the lock-guarded counters. <see cref="StopAllAsync"/> stamps a high-resolution
/// <see cref="Stopwatch.GetTimestamp"/> tick so the live-pipeline e-stop latency test can measure
/// Activate→StopAll wall-clock time. Existing single-threaded unit-test consumers are unaffected.
/// </para>
/// </summary>
internal sealed class RecordingCanonicalDevice : IForceFeedbackDevice
{
    private readonly object _gate = new();
    private readonly List<long> _stopAllTimestamps = [];
    private int _initializeCount;
    private int _stopAllCount;

    public int InitializeCount
    {
        get { lock (_gate) { return _initializeCount; } }
    }

    public List<ForceCommand> Executed { get; } = [];

    public int StopAllCount
    {
        get { lock (_gate) { return _stopAllCount; } }
    }

    /// <summary>Snapshot copy of <see cref="Executed"/> taken under the lock, for hermetic cross-thread reads.</summary>
    public IReadOnlyList<ForceCommand> ExecutedSnapshot
    {
        get { lock (_gate) { return Executed.ToList(); } }
    }

    /// <summary>
    /// <see cref="Stopwatch.GetTimestamp"/> tick of the most recent <see cref="StopAllAsync"/> call.
    /// Throws if StopAllAsync has not been called — callers assert <see cref="StopAllCount"/> first.
    /// </summary>
    public long LastStopAllTimestamp
    {
        get { lock (_gate) { return _stopAllTimestamps[^1]; } }
    }

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
        DeviceState previous;
        lock (_gate)
        {
            _initializeCount++;
            previous = State;
            State = DeviceState.Ready;
        }

        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs { Previous = previous, Current = State });
        return Task.CompletedTask;
    }

    public Task ExecuteAsync(ForceCommand command, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Executed.Add(command);
        }

        return Task.CompletedTask;
    }

    public Task StopAllAsync(CancellationToken cancellationToken)
    {
        // Stamp as the very first action so the measured latency excludes the double's own bookkeeping.
        var stamp = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            _stopAllCount++;
            _stopAllTimestamps.Add(stamp);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
