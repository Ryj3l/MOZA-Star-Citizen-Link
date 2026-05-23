using System.Diagnostics;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Effects.Tests;

/// <summary>
/// Test double for the canonical <see cref="IForceFeedbackDevice"/> (Core.Devices) used by the
/// ForceCommandPipeline tests. Records every command passed to <see cref="ExecuteAsync"/> and stamps each
/// <see cref="StopAllAsync"/> call with a high-resolution <see cref="Stopwatch.GetTimestamp"/> tick, so the
/// latency integration test (E5) can measure Activate→StopAll wall-clock time. Thread-safe: the pipeline's
/// background loop calls in while the test thread reads.
/// </summary>
public sealed class RecordingForceFeedbackDevice : IForceFeedbackDevice
{
    private readonly object _gate = new();
    private readonly List<ForceCommand> _executed = [];
    private readonly List<long> _stopAllTimestamps = [];

    public DeviceModel Model => DeviceModel.MozaAb6;

    public string DisplayName => "Recording AB6 (test)";

    public string ProductName => "MOZA AB6";

    public Guid InstanceGuid { get; } = Guid.NewGuid();

    public DeviceCapabilities Capabilities { get; } =
        new(DeviceModel.MozaAb6, 2, 4, true, true, true, 10_000, 0.85);

    public DeviceState State => DeviceState.Ready;

    // The canonical interface requires this event; a recording double never transitions state, so it is
    // implemented with empty accessors — no compiler-generated backing field, hence no CS0067 under
    // TreatWarningsAsErrors (fix-the-shape, not a suppression).
    event EventHandler<DeviceStateChangedEventArgs>? IForceFeedbackDevice.StateChanged
    {
        add { }
        remove { }
    }

    /// <summary>Commands recorded by <see cref="ExecuteAsync"/>, in arrival order (snapshot copy).</summary>
    public IReadOnlyList<ForceCommand> ExecutedCommands
    {
        get { lock (_gate) { return _executed.ToList(); } }
    }

    /// <summary>Number of <see cref="StopAllAsync"/> invocations so far.</summary>
    public int StopAllCount
    {
        get { lock (_gate) { return _stopAllTimestamps.Count; } }
    }

    /// <summary>
    /// <see cref="Stopwatch.GetTimestamp"/> tick of the most recent <see cref="StopAllAsync"/> call.
    /// Throws if StopAllAsync has not been called — callers assert <see cref="StopAllCount"/> first.
    /// </summary>
    public long LastStopAllTimestamp
    {
        get { lock (_gate) { return _stopAllTimestamps[^1]; } }
    }

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ExecuteAsync(ForceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_gate)
        {
            _executed.Add(command);
        }

        return Task.CompletedTask;
    }

    public Task StopAllAsync(CancellationToken cancellationToken)
    {
        // Stamp as the very first action so the measured latency excludes the double's own bookkeeping.
        var stamp = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            _stopAllTimestamps.Add(stamp);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
