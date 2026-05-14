using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;
using Vortice.DirectInput;
// Disambiguate the T-06 effect record from the legacy Moza.ScLink.Core.Models.ForceEffect
// (the legacy type is consumed only by DirectInputForceFeedbackDevice.cs, deleted in T-07 M12).
using ForceEffect = Moza.ScLink.Core.Effects.ForceEffect;

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

    // ── Effect-parameter construction (T-07 M6) ──────────────────────────────────────────────
    // Pure translation from the domain ForceEffect to a Vortice EffectParameters. No device
    // interaction. Wired into ExecuteAsync / HandlePlayAsync in M8; the effect cache is M7 and the
    // re-acquire / re-download retry loop is M9. The magnitude / period / duration math mirrors the
    // legacy DirectInputForceFeedbackDevice exactly (T-07.md non-goal: "No behavioral changes").

    private const int NominalMaxGain = 10000;                       // legacy DiFfNominalMax
    private const int NoTriggerButton = -1;                         // DirectInput "no trigger button"
    private const double DefaultPeriodicFrequencyHz = 20.0;         // legacy HertzToPeriod fallback

    /// <summary>DirectInput INFINITE duration sentinel. 0xFFFFFFFF reinterpreted as signed int = -1.</summary>
    private const int InfiniteDuration = unchecked((int)0xFFFFFFFF);

    /// <summary>
    /// Scales a normalized force direction to DirectInput's Cartesian direction units. Each component is
    /// clamped to [-1, 1] then scaled by <see cref="NominalMaxGain"/>. When both inputs are exactly zero the
    /// direction is unspecified, and this returns the legacy hardcoded pair <c>(1, 1)</c> — the value the
    /// pre-Vortice device wrote into the direction array for every effect — preserving AB6/AB9-validated feel
    /// until T-14 begins populating real directions.
    /// </summary>
    /// <param name="directionX">Horizontal direction component, nominally in [-1.0, 1.0].</param>
    /// <param name="directionY">Vertical direction component, nominally in [-1.0, 1.0].</param>
    /// <returns>The scaled Cartesian direction pair for <see cref="EffectParameters.Directions"/>.</returns>
    internal static (int X, int Y) ScaleDirection(double directionX, double directionY)
    {
        if (directionX == 0.0 && directionY == 0.0)
        {
            // Legacy parity: the literal pair (1, 1), NOT (NominalMaxGain, NominalMaxGain).
            return (1, 1);
        }

        var x = (int)Math.Round(Math.Clamp(directionX, -1.0, 1.0) * NominalMaxGain);
        var y = (int)Math.Round(Math.Clamp(directionY, -1.0, 1.0) * NominalMaxGain);
        return (x, y);
    }

    /// <summary>
    /// Translates a <see cref="ForceEffect"/> and its gain-resolved final intensity into a Vortice
    /// <see cref="EffectParameters"/>. Pure function — no device interaction. Exercised by M7/M8 tests
    /// through <c>HandlePlayAsync</c>; not yet called by production code (<see cref="ExecuteAsync"/> is an
    /// M8 stub).
    /// </summary>
    /// <param name="effect">The catalog effect descriptor to render.</param>
    /// <param name="finalIntensity">Gain-stack-resolved intensity in [0.0, 1.0].</param>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">
    /// The effect carries a populated <see cref="ForceEffect.Envelope"/> (envelope mapping is deferred to
    /// T-14 — see issue #17), or its <see cref="ForceEffect.EffectType"/> is one M6 does not build
    /// (<see cref="ForceEffectType.PeriodicWithEnvelope"/> / <see cref="ForceEffectType.Composite"/>).
    /// </exception>
    internal static EffectParameters BuildEffectParameters(ForceEffect effect, double finalIntensity)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (effect.Envelope is not null)
        {
            throw new NotSupportedException(
                "Force-feedback envelopes are not implemented in T-07. T-14 introduces the " +
                "ADSR-to-DirectInput envelope mapping once envelope-carrying effects enter the " +
                "catalog. Tracked in issue #17.");
        }

        var magnitude = ScaleMagnitude(finalIntensity);
        var (directionX, directionY) = ScaleDirection(effect.DirectionX, effect.DirectionY);

        TypeSpecificParameters typeSpecific = effect.EffectType switch
        {
            ForceEffectType.Periodic => new PeriodicForce
            {
                Magnitude = magnitude,
                Offset = 0,
                Phase = 0,
                Period = HertzToPeriodMicroseconds(effect.FrequencyHz),
            },
            ForceEffectType.ConstantForce => new ConstantForce
            {
                Magnitude = magnitude,
            },
            _ => throw new NotSupportedException(
                $"Effect type '{effect.EffectType}' is not supported by the DirectInput output device. " +
                "T-07 supports Periodic and ConstantForce. PeriodicWithEnvelope is deferred to T-14; " +
                "Composite is out of scope per T-07.md non-goals."),
        };

        return new EffectParameters
        {
            Flags = EffectFlags.Cartesian | EffectFlags.ObjectIds,
            Duration = DurationToMicroseconds(effect.Duration),
            SamplePeriod = 0,
            Gain = NominalMaxGain,
            TriggerButton = NoTriggerButton,
            TriggerRepeatInterval = 0,
            StartDelay = 0,
            Axes = [JoystickAxisOffsets.DijofsX, JoystickAxisOffsets.DijofsY],
            Directions = [directionX, directionY],
            // Vortice 3.6.2 annotates Envelope as non-nullable, but DirectInput semantics require a null
            // envelope to mean "no envelope shaping" — the legacy device expressed this as lpEnvelope=NULL.
            // null! is the deliberate, correct value here; T-14 replaces it when envelope mapping lands.
            Envelope = null!,
            Parameters = typeSpecific,
        };
    }

    /// <summary>Scales an intensity in [0.0, 1.0] to a DirectInput magnitude in [0, <see cref="NominalMaxGain"/>].</summary>
    private static int ScaleMagnitude(double intensity)
        => (int)Math.Round(Math.Clamp(intensity, 0.0, 1.0) * NominalMaxGain);

    /// <summary>
    /// Converts a periodic-effect frequency to a DirectInput period in microseconds. Mirrors the legacy
    /// <c>HertzToPeriod</c>: a non-positive frequency falls back to <see cref="DefaultPeriodicFrequencyHz"/>
    /// rather than erroring, and the result is clamped to [1, <see cref="int.MaxValue"/>].
    /// </summary>
    private static int HertzToPeriodMicroseconds(double frequencyHz)
    {
        var frequency = frequencyHz <= 0.0 ? DefaultPeriodicFrequencyHz : frequencyHz;
        return (int)Math.Clamp(1_000_000.0 / frequency, 1.0, int.MaxValue);
    }

    /// <summary>
    /// Converts an effect duration to DirectInput microseconds. Mirrors the legacy <c>ToDirectInputDuration</c>:
    /// <see cref="TimeSpan.Zero"/> (a sustained effect) maps to <see cref="InfiniteDuration"/>; any other value
    /// is milliseconds × 1000, clamped to [1, <see cref="int.MaxValue"/>] — note the floor is 1, not 0.
    /// </summary>
    private static int DurationToMicroseconds(TimeSpan duration)
    {
        if (duration == TimeSpan.Zero)
        {
            return InfiniteDuration;
        }

        return (int)Math.Clamp(duration.TotalMilliseconds * 1000.0, 1.0, int.MaxValue);
    }
}
