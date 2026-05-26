using Microsoft.Extensions.Logging;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.App.ForceFeedback;

/// <summary>
/// Minimal canonical no-hardware fallback device. Implements the canonical
/// <see cref="IForceFeedbackDevice"/> so the app starts and the bus pipeline runs end-to-end
/// without MOZA hardware (PRP §10.3 preview-safe start), logging the intent of each command
/// instead of driving hardware. Reports <see cref="DeviceState.Ready"/> after initialization and a
/// <see cref="DeviceModel.Unknown"/> capability profile (cosmetic — the resolver uses its own
/// placeholder via DefaultResolverContextProvider). Selected by
/// <see cref="ForceFeedbackDeviceFactory.CreateCanonical"/> when no allowlisted DirectInput device
/// enumerates (or when <c>MOZA_SC_OUTPUT=Preview</c>); superseded by T-17's richer
/// PreviewForceFeedbackDevice.
/// </summary>
public sealed class LoggingNullForceFeedbackDevice : IForceFeedbackDevice
{
    private readonly ILogger<LoggingNullForceFeedbackDevice> _logger;
    private DeviceState _state = DeviceState.Disconnected;

    public LoggingNullForceFeedbackDevice(ILogger<LoggingNullForceFeedbackDevice> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public DeviceModel Model => DeviceModel.Unknown;

    public string DisplayName => "No hardware (preview logging)";

    public string ProductName => "None";

    public Guid InstanceGuid => Guid.Empty;

    public DeviceCapabilities Capabilities { get; } =
        new(DeviceModel.Unknown, AxisCount: 0, SimultaneousEffectCount: 0,
            SupportsConstantForce: false, SupportsPeriodic: false, SupportsEnvelope: false,
            MaxGain: 0, MaxIntensityRecommended: 0);

    public DeviceState State => _state;

    // No transitions are broadcast; empty accessors mean no compiler-generated backing field, so no
    // CS0067 under TreatWarningsAsErrors (same fix-the-shape approach as RecordingForceFeedbackDevice).
    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged
    {
        add { }
        remove { }
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        _state = DeviceState.Ready;
        Log.Initialized(_logger);
        return Task.CompletedTask;
    }

    public Task ExecuteAsync(ForceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Log.CommandLogged(_logger, command.GetType().Name, command.CommandId);
        return Task.CompletedTask;
    }

    public Task StopAllAsync(CancellationToken cancellationToken)
    {
        Log.StopAll(_logger);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static class Log
    {
        private static readonly Action<ILogger, Exception?> _initialized =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(1, nameof(Initialized)),
                "No-hardware preview device initialized; force commands will be logged, not played.");

        private static readonly Action<ILogger, string, string, Exception?> _commandLogged =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug,
                new EventId(2, nameof(CommandLogged)),
                "Preview device received {CommandType} (CommandId={CommandId}); no hardware output.");

        private static readonly Action<ILogger, Exception?> _stopAll =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(3, nameof(StopAll)),
                "Preview device StopAll requested (emergency-stop path); no hardware output.");

        public static void Initialized(ILogger logger) => _initialized(logger, null);

        public static void CommandLogged(ILogger logger, string commandType, string commandId)
            => _commandLogged(logger, commandType, commandId, null);

        public static void StopAll(ILogger logger) => _stopAll(logger, null);
    }
}
