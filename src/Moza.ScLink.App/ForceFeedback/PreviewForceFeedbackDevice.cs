using Microsoft.Extensions.Logging;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.App.ForceFeedback;

/// <summary>
/// Canonical no-hardware preview device (T-17). Implements <see cref="IForceFeedbackDevice"/> so the bus
/// pipeline runs end-to-end without a MOZA base, and <see cref="IPreviewCommandSource"/> so the UI can
/// show what <em>would</em> have been driven. Every command is logged in full detail and published to the
/// <see cref="Commands"/> stream as a <see cref="PreviewedCommand"/> (effect id / intensity / frequency /
/// duration / direction / active-effects-count). Selected by
/// <see cref="ForceFeedbackDeviceFactory.CreateCanonical"/> on both fallback paths: no allowlisted device,
/// or preview forced (<c>MOZA_SC_OUTPUT=Preview</c> or <see cref="Profiles.Settings.AppSettings.ForcePreviewMode"/>).
/// Supersedes the minimal <c>LoggingNullForceFeedbackDevice</c> it replaced.
/// </summary>
/// <remarks>
/// The active-effects count mirrors the real <c>VorticeDirectInputDevice</c> exactly: it is keyed on the
/// <see cref="ForceEffect.StateKey"/> of sustained effects. A <see cref="PlayEffectCommand"/> with a
/// non-null StateKey adds/replaces; a non-sustained play (null StateKey) is logged and published but does
/// not change the count; <see cref="StopEffectCommand"/> removes by StateKey (unknown = no-op);
/// <see cref="StopAllCommand"/> / <see cref="StopAllAsync"/> clear. The set is a plain
/// <see cref="HashSet{T}"/> under a private lock — the single <c>ForceCommandPipeline</c> reader thread is
/// the only writer, and the lock makes the count read consistent with the mutation that produced it.
/// </remarks>
public sealed class PreviewForceFeedbackDevice : IForceFeedbackDevice, IPreviewCommandSource
{
    private readonly ILogger<PreviewForceFeedbackDevice> _logger;
    private readonly PreviewCommandSubject _subject = new();
    private readonly object _gate = new();

    // The set of StateKeys of currently-active sustained effects. Mirrors the keyset of
    // VorticeDirectInputDevice._activeByStateKey (the device stores effect objects as values; the previewer
    // has no effect to store, so the key set alone is the faithful projection of "what is active").
    private readonly HashSet<string> _active = [];

    private DeviceState _state = DeviceState.Disconnected;

    public PreviewForceFeedbackDevice(ILogger<PreviewForceFeedbackDevice> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public DeviceModel Model => DeviceModel.Unknown;

    public string DisplayName => "No hardware (preview mode)";

    public string ProductName => "None";

    public Guid InstanceGuid => Guid.Empty;

    public DeviceCapabilities Capabilities { get; } =
        new(DeviceModel.Unknown, AxisCount: 0, SimultaneousEffectCount: 0,
            SupportsConstantForce: false, SupportsPeriodic: false, SupportsEnvelope: false,
            MaxGain: 0, MaxIntensityRecommended: 0);

    public DeviceState State => _state;

    /// <inheritdoc />
    public IObservable<PreviewedCommand> Commands => _subject;

    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        // A real Disconnected→Ready transition (not LoggingNull's empty add{}/remove{} CS0067 dodge) so the
        // VM's Output-row subscription honestly reflects "Ready" once the host inits the preview device.
        var previous = _state;
        _state = DeviceState.Ready;
        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs { Previous = previous, Current = _state });
        Log.Initialized(_logger);
        return Task.CompletedTask;
    }

    public Task ExecuteAsync(ForceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        int activeCount;
        lock (_gate)
        {
            switch (command)
            {
                // Sustained play (non-null StateKey): add/replace. HashSet.Add is a no-op on a duplicate
                // key, which is the correct "replace" semantics here — the key is the only state we hold.
                case PlayEffectCommand { Effect.StateKey: { } stateKey }:
                    _active.Add(stateKey);
                    break;

                // Non-sustained play (null StateKey) matches no case → no active-set change (logged/published below).
                case StopEffectCommand stop:
                    _active.Remove(stop.StateKey);
                    break;

                case StopAllCommand:
                    _active.Clear();
                    break;
            }

            activeCount = _active.Count; // Read under the same lock as the mutation that produced it.
        }

        LogCommand(command, activeCount);
        _subject.Publish(Project(command, activeCount));
        return Task.CompletedTask;
    }

    public Task StopAllAsync(CancellationToken cancellationToken)
    {
        int activeCount;
        lock (_gate)
        {
            _active.Clear();
            activeCount = _active.Count;
        }

        Log.StopAll(_logger, activeCount);
        // Project a synthetic StopAll so the diagnostics stream reflects the emergency-stop path too.
        _subject.Publish(new PreviewedCommand(
            DateTimeOffset.UtcNow, nameof(StopAllAsync), null, null, null, null, null, null, null, activeCount));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static PreviewedCommand Project(ForceCommand command, int activeCount) => command switch
    {
        PlayEffectCommand play => new PreviewedCommand(
            play.IssuedAt, nameof(PlayEffectCommand), play.Effect.EffectId, play.FinalIntensity,
            play.Effect.FrequencyHz, play.Effect.Duration, play.Effect.DirectionX, play.Effect.DirectionY,
            play.Effect.Envelope, activeCount),
        _ => new PreviewedCommand(
            command.IssuedAt, command.GetType().Name, null, null, null, null, null, null, null, activeCount),
    };

    private void LogCommand(ForceCommand command, int activeCount)
    {
        switch (command)
        {
            case PlayEffectCommand play:
                Log.Play(_logger, play.Effect.EffectId, play.FinalIntensity, play.Effect.FrequencyHz,
                    play.Effect.Duration, (play.Effect.DirectionX, play.Effect.DirectionY), activeCount);
                break;
            case StopEffectCommand stop:
                Log.Stop(_logger, stop.StateKey, activeCount);
                break;
            case StopAllCommand:
                Log.StopAll(_logger, activeCount);
                break;
        }
    }

    private static class Log
    {
        private static readonly Action<ILogger, Exception?> _initialized =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(1, nameof(Initialized)),
                "Preview device initialized; force commands will be logged and streamed, not played.");

        private static readonly Action<ILogger, string, double, double, TimeSpan, (double X, double Y), int, Exception?> _play =
            LoggerMessage.Define<string, double, double, TimeSpan, (double X, double Y), int>(
                LogLevel.Debug,
                new EventId(2, nameof(Play)),
                "Preview Play {EffectId} intensity={FinalIntensity} freq={FrequencyHz}Hz duration={Duration} direction={Direction} active={ActiveCount}.");

        private static readonly Action<ILogger, string, int, Exception?> _stop =
            LoggerMessage.Define<string, int>(
                LogLevel.Debug,
                new EventId(3, nameof(Stop)),
                "Preview Stop StateKey={StateKey} active={ActiveCount}.");

        private static readonly Action<ILogger, int, Exception?> _stopAll =
            LoggerMessage.Define<int>(
                LogLevel.Information,
                new EventId(4, nameof(StopAll)),
                "Preview StopAll (emergency-stop path); active cleared to {ActiveCount}.");

        public static void Initialized(ILogger logger) => _initialized(logger, null);

        public static void Play(ILogger logger, string effectId, double finalIntensity, double frequencyHz,
            TimeSpan duration, (double X, double Y) direction, int activeCount)
            => _play(logger, effectId, finalIntensity, frequencyHz, duration, direction, activeCount, null);

        public static void Stop(ILogger logger, string stateKey, int activeCount)
            => _stop(logger, stateKey, activeCount, null);

        public static void StopAll(ILogger logger, int activeCount) => _stopAll(logger, activeCount, null);
    }
}
