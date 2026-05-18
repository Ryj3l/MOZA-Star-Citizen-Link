using Microsoft.Extensions.Logging;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;
using SharpGen.Runtime;
using LegacyForceEffect = Moza.ScLink.Core.Models.ForceEffect;
using NewForceEffect = Moza.ScLink.Core.Effects.ForceEffect;
using NewDevice = Moza.ScLink.Core.Devices.IForceFeedbackDevice;

namespace Moza.ScLink.DirectInput;

/// <summary>
/// Transitional shim that implements the legacy <see cref="Moza.ScLink.Core.IForceFeedbackDevice"/> by
/// wrapping a T-06 <see cref="Moza.ScLink.Core.Devices.IForceFeedbackDevice"/>. It translates legacy
/// <see cref="LegacyForceEffect"/> play/stop calls into the polymorphic <see cref="ForceCommand"/> surface
/// the new device consumes, so the App-layer factory (T-07 M11) can construct the new device without
/// touching <c>ForceFeedbackController</c> or any App call site.
/// </summary>
/// <remarks>
/// The adapter does <b>not</b> own the wrapped device's lifetime: it is deliberately not
/// <see cref="IAsyncDisposable"/>, matching the legacy <c>DirectInputForceFeedbackDevice</c>, which was
/// process-lifetime-scoped and never disposed. T-07 Issue #27 Pass-2 adds chain-owned disposal via
/// <see cref="IDirectInputAdapterSlot.DisposeWrappedAsync"/>:
/// <see cref="FallbackForceFeedbackDevice"/> calls into the adapter on DBT_DEVICEREMOVECOMPLETE
/// (hot-removal) and on double-arrival teardown-then-replace. The adapter owns the disposal
/// mechanics; the chain owns the timing. Real broader lifetime management arrives with the
/// T-10/T-14 channels-based pipeline, which also deletes this adapter and the
/// <see cref="IDirectInputAdapterSlot"/> interface (deletion tracked in issue #15).
/// <see cref="EffectCategory"/> assignment is likewise deferred — see issue #21 and the
/// <see cref="PlayAsync"/> remarks.
/// </remarks>
[Obsolete("Transitional adapter. Delete in T-10/T-14 when ForceFeedbackController is replaced by the channels-based pipeline.")]
public sealed class LegacyForceFeedbackDeviceAdapter
    : Moza.ScLink.Core.IForceFeedbackDevice, IDirectInputAdapterSlot
{
    private readonly NewDevice _device;
    private readonly ILogger<LegacyForceFeedbackDeviceAdapter> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Constructs the adapter around a new-interface <paramref name="device"/>.</summary>
    /// <param name="device">The T-06 device this adapter delegates to.</param>
    /// <param name="logger">
    /// Logger; used by <see cref="StopAsync"/> to record a swallowed classified DirectInput failure.
    /// </param>
    /// <param name="timeProvider">
    /// Stamps <see cref="ForceCommand.IssuedAt"/> on translated commands. <see langword="null"/> (the
    /// default) installs <see cref="TimeProvider.System"/> — the documented "use production default"
    /// sentinel, mirroring <c>VorticeDirectInputDevice</c>'s <c>delayStrategy</c> parameter, so it is
    /// intentionally not <c>ThrowIfNull</c>-guarded.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="device"/> or <paramref name="logger"/> is <see langword="null"/>.
    /// </exception>
    public LegacyForceFeedbackDeviceAdapter(
        NewDevice device,
        ILogger<LegacyForceFeedbackDeviceAdapter> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);

        _device = device;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Display name. Reproduces the legacy <c>DirectInputForceFeedbackDevice.Name</c> shape
    /// (<c>"DirectInput: {productName}"</c>) for migration-faithful UI display via
    /// <c>ForceFeedbackController.OutputName</c>.
    /// </summary>
    public string Name => $"DirectInput: {_device.ProductName}";

    /// <summary>
    /// Human-readable status string. The verbatim legacy <c>DirectInputForceFeedbackDevice.Status</c>
    /// constant, preserved for migration-faithful UI display.
    /// </summary>
    public string Status => "Using Windows DirectInput force feedback.";

    /// <summary>
    /// Initializes the wrapped device. Straight delegation to
    /// <see cref="Moza.ScLink.Core.Devices.IForceFeedbackDevice.InitializeAsync"/>.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken) =>
        _device.InitializeAsync(cancellationToken);

    /// <summary>
    /// No-op. The legacy interface pre-loaded effects onto the device here; the new device builds and
    /// caches DirectInput effects lazily on first <see cref="PlayAsync"/>, so there is nothing to
    /// pre-load and no work for this method to do.
    /// </summary>
    public Task PrepareAsync(IEnumerable<LegacyForceEffect> effects, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>
    /// Translates <paramref name="effect"/> into a <see cref="PlayEffectCommand"/> and executes it on the
    /// wrapped device.
    /// </summary>
    /// <remarks>
    /// Field mapping (legacy <see cref="LegacyForceEffect"/> → new <see cref="NewForceEffect"/>):
    /// <list type="bullet">
    /// <item><c>Name</c> → <see cref="NewForceEffect.EffectId"/>.</item>
    /// <item><c>Kind</c> → <see cref="NewForceEffect.EffectType"/>: <c>PeriodicVibration</c> and
    /// <c>StateVibration</c> → <see cref="ForceEffectType.Periodic"/>; <c>Bump</c> →
    /// <see cref="ForceEffectType.ConstantForce"/> (matching the legacy
    /// <c>DirectInputForceFeedbackDevice.CreateBumpEffect</c>).</item>
    /// <item><c>Intensity</c> → both <see cref="NewForceEffect.BaseIntensity"/> and the command's
    /// <see cref="PlayEffectCommand.FinalIntensity"/> — the legacy adapter has no gain stack, so the
    /// nominal and resolved intensities are identical.</item>
    /// <item><c>FrequencyHz</c> → <see cref="NewForceEffect.FrequencyHz"/>; <c>Duration</c> →
    /// <see cref="NewForceEffect.Duration"/>.</item>
    /// <item><c>StateKey</c> → <see cref="NewForceEffect.StateKey"/>;
    /// <see cref="NewForceEffect.IsSustained"/> ← <c>StateKey is not null</c>.</item>
    /// <item>Direction → <c>(0, 0)</c>: the legacy effect carries no direction, and
    /// <c>VorticeDirectInputDevice.ScaleDirection</c> falls back to the legacy <c>(1, 1)</c> pair for a
    /// zero direction at the device layer.</item>
    /// </list>
    /// <para/>
    /// <see cref="NewForceEffect.Category"/> is hard-coded to <see cref="EffectCategory.System"/> for
    /// every translated effect. The legacy interface carries no category concept, and the adapter sits
    /// below the event-aware layer that assigns game-domain category — by the time the adapter sees an
    /// effect, <c>ForceFeedbackController</c> has already flattened the originating game event.
    /// <see cref="EffectCategory.System"/> is the most semantically-neutral value ("category not
    /// meaningfully assigned at this layer"); a <c>Kind</c>-based mapping would index waveform shape, not
    /// game-domain category, and so be non-uniformly wrong. This is acceptable only because the adapter
    /// is transitional — T-10's channels pipeline assigns real categories at the event layer. Tracked in
    /// issue #21.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
    public Task PlayAsync(LegacyForceEffect effect, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return _device.ExecuteAsync(TranslateToPlayCommand(effect), cancellationToken);
    }

    /// <summary>Stops the sustained effect identified by <paramref name="stateKey"/>.</summary>
    /// <remarks>
    /// Stop-path exception contract: unlike the legacy <c>DirectInputForceFeedbackDevice.StopAsync</c>,
    /// which discarded every DirectInput HRESULT, this adapter catches <b>only</b> a classified
    /// <see cref="SharpGenException"/> — the type the M9 stop path raises on a classified DirectInput
    /// failure — logs a Warning, and returns. <see cref="ObjectDisposedException"/>,
    /// <see cref="OperationCanceledException"/>, <see cref="ArgumentNullException"/>, and every other
    /// non-classified exception propagate, matching the M8/M9 fast-fail-fast stop contract. The
    /// <c>await</c> is inside the <c>try</c> so the catch is robust whether the wrapped device throws
    /// synchronously or via a faulted task.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="stateKey"/> is <see langword="null"/>.</exception>
    public async Task StopAsync(string stateKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stateKey);

        var command = new StopEffectCommand(stateKey)
        {
            CommandId = Guid.NewGuid().ToString(),
            IssuedAt = _timeProvider.GetUtcNow(),
        };

        try
        {
            await _device.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (SharpGenException ex)
        {
            Log.StopSwallowedClassifiedFailure(_logger, stateKey, ex);
        }
    }

    /// <summary>
    /// Stops all active effects on the wrapped device. The new device's <c>StopAllAsync</c> entry point
    /// and its <c>StopAllCommand</c> dispatch arm funnel to the identical handler, so no command
    /// fabrication is needed here — the call is a thin pass-through plus a defensive narrow catch.
    /// </summary>
    /// <remarks>
    /// Stop-all-path exception contract: mirrors <see cref="StopAsync"/> at the single-effect layer
    /// (Issue #27 Pass 1). Catches <b>only</b> a classified <see cref="SharpGenException"/> — the type
    /// the M9 stop path raises on a classified DirectInput failure — logs a Warning, and returns.
    /// <see cref="ObjectDisposedException"/>, <see cref="OperationCanceledException"/>,
    /// <see cref="ArgumentNullException"/>, and every other non-classified exception propagate, matching
    /// the M8/M9 fast-fail-fast stop contract. The <c>await</c> is inside the <c>try</c> so the catch is
    /// robust whether the wrapped device throws synchronously or via a faulted task.
    /// <para/>
    /// Restores in-class symmetry: prior to Pass 1, single-effect <see cref="StopAsync"/> caught a
    /// classified failure while stop-all leaked it as a faulted Task — adapters above the
    /// <c>FallbackForceFeedbackDevice</c> outer catch never saw the discriminated disposition. Pass 1
    /// pairs them.
    /// </remarks>
    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _device.StopAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SharpGenException ex)
        {
            Log.StopAllSwallowedClassifiedFailure(_logger, ex);
        }
    }

    /// <summary>
    /// Disposes the wrapped DirectInput device. Called by the chain on hot-loss
    /// (DBT_DEVICEREMOVECOMPLETE) and on double-arrival teardown-then-replace; the chain owns
    /// the timing, the adapter owns the mechanics (reaching <see cref="_device"/>).
    /// </summary>
    public ValueTask DisposeWrappedAsync() => _device.DisposeAsync();

    private static NewForceEffect TranslateEffect(LegacyForceEffect legacy) => new()
    {
        EffectId = legacy.Name,
        EffectType = legacy.Kind switch
        {
            ForceEffectKind.PeriodicVibration => ForceEffectType.Periodic,
            ForceEffectKind.StateVibration => ForceEffectType.Periodic,
            ForceEffectKind.Bump => ForceEffectType.ConstantForce,
            // CS8509: an enum switch expression needs a default arm under WarningLevel 5. The periodic
            // fallback matches the legacy DirectInputForceFeedbackDevice.GetOrCreateEffect default.
            _ => ForceEffectType.Periodic,
        },
        Category = EffectCategory.System,
        BaseIntensity = legacy.Intensity,
        FrequencyHz = legacy.FrequencyHz,
        Duration = legacy.Duration,
        DirectionX = 0.0,
        DirectionY = 0.0,
        Envelope = null,
        IsSustained = legacy.StateKey is not null,
        StateKey = legacy.StateKey,
    };

    private PlayEffectCommand TranslateToPlayCommand(LegacyForceEffect legacy) =>
        new(TranslateEffect(legacy), legacy.Intensity)
        {
            CommandId = Guid.NewGuid().ToString(),
            IssuedAt = _timeProvider.GetUtcNow(),
        };

    private static class Log
    {
        private static readonly Action<ILogger, string, Exception?> _stopSwallowedClassifiedFailure =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(1, nameof(StopSwallowedClassifiedFailure)),
                "Legacy adapter swallowed a classified DirectInput failure stopping effect {StateKey}");

        public static void StopSwallowedClassifiedFailure(ILogger logger, string stateKey, Exception exception)
            => _stopSwallowedClassifiedFailure(logger, stateKey, exception);

        // ── Issue #27 Pass 1: StopAllAsync symmetry restoration ───────────────────────────────
        // Paired with StopSwallowedClassifiedFailure above. No StateKey parameter — StopAll is
        // device-wide, not per-effect. Same Warning level as the single-effect entry: a classified
        // failure at this layer always indicates the wrapped device's M9 path emitted a faulted Task
        // we discriminated as benign, but Warning preserves traceability up through the host's log
        // pipeline.

        private static readonly Action<ILogger, Exception?> _stopAllSwallowedClassifiedFailure =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(2, nameof(StopAllSwallowedClassifiedFailure)),
                "Legacy adapter swallowed a classified DirectInput failure stopping all effects");

        public static void StopAllSwallowedClassifiedFailure(ILogger logger, Exception exception)
            => _stopAllSwallowedClassifiedFailure(logger, exception);
    }
}
