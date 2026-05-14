using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;
using Vortice.DirectInput;

namespace Moza.ScLink.DirectInput;

/// <summary>
/// Vortice.DirectInput-backed implementation of <see cref="IForceFeedbackDevice"/>. Owns a single
/// <see cref="IDirectInputDeviceAbstraction"/> for the device's lifetime and orchestrates the DirectInput
/// cooperative-level / acquisition / force-feedback-command lifecycle.
/// </summary>
/// <remarks>
/// T-07 Milestone 5 ships the skeleton: constructor with guards, lifecycle state machine, and
/// <see cref="InitializeAsync"/> per plan §G2. Effect creation (M6), the dual-dictionary effect cache (M7),
/// <see cref="ExecuteAsync"/> dispatch (M8), and the re-acquire / re-download retry loop (M9) land in
/// subsequent milestones with their own hand-review pauses; until M11 wires the App-layer factory there is
/// no production caller that can reach the unimplemented surface.
/// </remarks>
public sealed class VorticeDirectInputDevice : IForceFeedbackDevice
{
    private readonly IDirectInputAbstraction _abstraction;
    private readonly DirectInputDeviceIdentity _identity;
    private readonly ILogger<VorticeDirectInputDevice> _logger;
    private IDirectInputDeviceAbstraction? _device;
    private DeviceState _state = DeviceState.Disconnected;
    private DeviceCapabilities? _capabilities;
    private bool _disposed;

    /// <summary>
    /// Constructs the device against an abstraction seam and a classified <see cref="DirectInputDeviceIdentity"/>.
    /// Refuses <see cref="DeviceModel.Unknown"/> — only allowlisted MOZA hardware reaches this constructor.
    /// </summary>
    /// <param name="abstraction">Production DirectInput abstraction or a test seam.</param>
    /// <param name="identity">Pre-classified device identity from the factory call-site.</param>
    /// <param name="logger">Serilog-bridged logger; used by M5–M9 for cache-hit/miss, re-acquire, and lifecycle traces.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="identity"/>'s <see cref="DirectInputDeviceIdentity.Model"/> is <see cref="DeviceModel.Unknown"/>.</exception>
    public VorticeDirectInputDevice(
        IDirectInputAbstraction abstraction,
        DirectInputDeviceIdentity identity,
        ILogger<VorticeDirectInputDevice> logger)
    {
        ArgumentNullException.ThrowIfNull(abstraction);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(logger);
        if (identity.Model == DeviceModel.Unknown)
        {
            throw new ArgumentException(
                "VorticeDirectInputDevice cannot be constructed for DeviceModel.Unknown.",
                nameof(identity));
        }

        _abstraction = abstraction;
        _identity = identity;
        _logger = logger;
    }

    /// <inheritdoc />
    public DeviceModel Model => _identity.Model;

    /// <inheritdoc />
    public string DisplayName => _identity.DisplayName;

    /// <inheritdoc />
    public string ProductName => _identity.ProductName;

    /// <inheritdoc />
    public Guid InstanceGuid => _identity.InstanceGuid;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> has populated capabilities.</exception>
    public DeviceCapabilities Capabilities =>
        _capabilities ?? throw new InvalidOperationException(
            "Capabilities is unavailable until InitializeAsync completes.");

    /// <inheritdoc />
    public DeviceState State => _state;

    /// <inheritdoc />
    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    /// <remarks>
    /// Idempotent: re-entering after <see cref="DeviceState.Ready"/> is a no-op. The cooperative-level
    /// flag bundle <see cref="CooperativeLevel.Exclusive"/>|<see cref="CooperativeLevel.Background"/> and the
    /// initial nominal gain of <c>10000</c> mirror the pre-Vortice behavior preserved per PRP §14.2.
    /// </remarks>
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_state != DeviceState.Disconnected)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        TransitionState(DeviceState.Initializing);

        _device = _abstraction.CreateDevice(_identity.InstanceGuid);
        _device.SetCooperativeLevel(
            GetMainWindowHandle(),
            CooperativeLevel.Exclusive | CooperativeLevel.Background);
        _device.SetJoystickDataFormat();
        _device.Acquire();
        _device.SendForceFeedbackCommand(ForceFeedbackCommand.Reset);
        _device.SendForceFeedbackCommand(ForceFeedbackCommand.SetActuatorsOn);
        _device.SetGain(10000);

        _capabilities = new DeviceCapabilities(
            AxisCount: 2,
            SimultaneousEffectCount: 4,
            SupportsConstantForce: true,
            SupportsPeriodic: true,
            SupportsEnvelope: true,
            MaxGain: 10000,
            MaxIntensityRecommended: 0.85);

        TransitionState(DeviceState.Ready);
        Log.DeviceInitialized(_logger, DisplayName, InstanceGuid);
        return Task.CompletedTask;
    }

    private static class Log
    {
        private static readonly Action<ILogger, string, Guid, Exception?> _deviceInitialized =
            LoggerMessage.Define<string, Guid>(
                LogLevel.Information,
                new EventId(1, nameof(DeviceInitialized)),
                "DirectInput device initialized: {DisplayName} ({InstanceGuid})");

        public static void DeviceInitialized(ILogger logger, string displayName, Guid instanceGuid)
            => _deviceInitialized(logger, displayName, instanceGuid, null);
    }

    /// <inheritdoc />
    public Task ExecuteAsync(ForceCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException("ExecuteAsync lands in T-07 M8.");

    /// <inheritdoc />
    public Task StopAllAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException("StopAllAsync lands in T-07 M8.");

    /// <inheritdoc />
    /// <remarks>
    /// Disposes the underlying adapter and transitions to <see cref="DeviceState.Disconnected"/> as the
    /// terminal state so post-dispose <see cref="State"/> reads remain coherent. The <see cref="StateChanged"/>
    /// event fires one last time on the way down. Relies on <see cref="VorticeDirectInputDeviceAdapter.Dispose"/>'s
    /// own narrow <see cref="SharpGen.Runtime.SharpGenException"/> swallow — no defensive double-swallow here.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _device?.Dispose();
        _device = null;
        TransitionState(DeviceState.Disconnected);
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private void TransitionState(DeviceState next)
    {
        if (_state == next)
        {
            return;
        }

        var previous = _state;
        _state = next;
        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs { Previous = previous, Current = next });
    }

    private static IntPtr GetMainWindowHandle()
    {
        var window = Application.Current?.MainWindow;
        return window is null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
    }
}
