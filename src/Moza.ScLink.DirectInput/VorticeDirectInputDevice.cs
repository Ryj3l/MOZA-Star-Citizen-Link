using System.Collections.Concurrent;
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

    // M9 retry-loop backoff seam. Production default is Task.Delay; tests inject a no-op or a recording
    // strategy so the re-acquire backoff (50/200/500 ms) is exercised without real wall-clock waits.
    private readonly Func<int, CancellationToken, Task> _delayStrategy;

    private IDirectInputDeviceAbstraction? _device;
    private DeviceState _state = DeviceState.Disconnected;
    private DeviceCapabilities? _capabilities;
    private bool _disposed;

    // Effect cache (T-07 M7). Values are stored raw — NOT Lazy<T>-wrapped. Lazy<T> in its default
    // ExecutionAndPublication mode caches the factory's exception, which would break the M9 retry loop's
    // ability to re-attempt CreateEffect after a NeedsReacquire / NeedsRedownload. The M8 GetOrAdd race is
    // instead handled by the atomic value-overload GetOrAdd + a !ReferenceEquals dispose-loser check.
    private readonly ConcurrentDictionary<DeviceCacheKey, IDirectInputEffectAbstraction> _effectCache = new();

    // Index of currently-active sustained effects by StateKey. Declared in M7; populated by HandlePlayAsync
    // in M8. Every entry is also an _effectCache entry (active effects are taken from the cache), so
    // DisposeAsync disposes via _effectCache and only clears this index.
    private readonly ConcurrentDictionary<string, IDirectInputEffectAbstraction> _activeByStateKey = new();

    /// <summary>
    /// Constructs the device against an abstraction seam and a classified <see cref="DirectInputDeviceIdentity"/>.
    /// Refuses <see cref="DeviceModel.Unknown"/> — only allowlisted MOZA hardware reaches this constructor.
    /// </summary>
    /// <param name="abstraction">Production DirectInput abstraction or a test seam.</param>
    /// <param name="identity">Pre-classified device identity from the factory call-site.</param>
    /// <param name="logger">Serilog-bridged logger; used by M5–M9 for cache-hit/miss, re-acquire, and lifecycle traces.</param>
    /// <param name="delayStrategy">
    /// M9 re-acquire backoff seam: receives a delay in milliseconds and the operation's
    /// <see cref="CancellationToken"/>, returns a <see cref="Task"/> that completes when the delay elapses.
    /// <see langword="null"/> (the default) installs the production strategy <c>Task.Delay(ms, ct)</c>;
    /// tests inject a no-op or a recording strategy. <see langword="null"/> is the documented "use
    /// production default" sentinel — not a misuse — so it is intentionally not <c>ThrowIfNull</c>-guarded.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument except <paramref name="delayStrategy"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="identity"/>'s <see cref="DirectInputDeviceIdentity.Model"/> is <see cref="DeviceModel.Unknown"/>.</exception>
    public VorticeDirectInputDevice(
        IDirectInputAbstraction abstraction,
        DirectInputDeviceIdentity identity,
        ILogger<VorticeDirectInputDevice> logger,
        Func<int, CancellationToken, Task>? delayStrategy = null)
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
        _delayStrategy = delayStrategy ?? ((ms, ct) => Task.Delay(ms, ct));
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

        private static readonly Action<ILogger, string, ForceEffectType, Exception?> _effectCacheHit =
            LoggerMessage.Define<string, ForceEffectType>(
                LogLevel.Debug,
                new EventId(2, nameof(EffectCacheHit)),
                "DI effect cache hit: {EffectId} ({EffectType})");

        public static void EffectCacheHit(ILogger logger, string effectId, ForceEffectType effectType)
            => _effectCacheHit(logger, effectId, effectType, null);

        private static readonly Action<ILogger, string, ForceEffectType, Exception?> _effectCacheMiss =
            LoggerMessage.Define<string, ForceEffectType>(
                LogLevel.Debug,
                new EventId(3, nameof(EffectCacheMiss)),
                "DI effect cache miss: {EffectId} ({EffectType}) — creating effect");

        public static void EffectCacheMiss(ILogger logger, string effectId, ForceEffectType effectType)
            => _effectCacheMiss(logger, effectId, effectType, null);

        private static readonly Action<ILogger, string, Exception?> _effectStopped =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(4, nameof(EffectStopped)),
                "DI effect stopped: {StateKey}");

        public static void EffectStopped(ILogger logger, string stateKey)
            => _effectStopped(logger, stateKey, null);

        private static readonly Action<ILogger, string, Exception?> _stopUnknownStateKey =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(5, nameof(StopUnknownStateKey)),
                "DI stop for unknown StateKey: {StateKey}; no-op");

        public static void StopUnknownStateKey(ILogger logger, string stateKey)
            => _stopUnknownStateKey(logger, stateKey, null);

        private static readonly Action<ILogger, string, Exception?> _stopAllEffectFailed =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(6, nameof(StopAllEffectFailed)),
                "DI Stop() failed during StopAll for {StateKey}; continuing");

        public static void StopAllEffectFailed(ILogger logger, string stateKey, Exception exception)
            => _stopAllEffectFailed(logger, stateKey, exception);

        private static readonly Action<ILogger, string, string, Exception?> _retryPathExhausted =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(7, nameof(RetryPathExhausted)),
                "DI retry exhausted for {Operation} ({Reason}); effect not played");

        public static void RetryPathExhausted(ILogger logger, string operation, string reason, Exception? exception)
            => _retryPathExhausted(logger, operation, reason, exception);

        private static readonly Action<ILogger, string, string, int, Exception?> _retryAttempt =
            LoggerMessage.Define<string, string, int>(
                LogLevel.Debug,
                new EventId(8, nameof(RetryAttempt)),
                "DI retry for {Operation} ({Reason}); delay {DelayMs}ms");

        public static void RetryAttempt(ILogger logger, string operation, string reason, int delayMs)
            => _retryAttempt(logger, operation, reason, delayMs, null);

        private static readonly Action<ILogger, string, Exception?> _reacquireCallThrew =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(9, nameof(ReacquireCallThrew)),
                "DI Unacquire/Acquire itself threw for {Operation}; continuing retry loop");

        public static void ReacquireCallThrew(ILogger logger, string operation, Exception exception)
            => _reacquireCallThrew(logger, operation, exception);

        private static readonly Action<ILogger, string, string, Exception?> _retryPathRecovered =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug,
                new EventId(10, nameof(RetryPathRecovered)),
                "DI {Operation} recovered after retry ({Reason})");

        public static void RetryPathRecovered(ILogger logger, string operation, string reason)
            => _retryPathRecovered(logger, operation, reason, null);

        private static readonly Action<ILogger, string, Exception?> _redownloadCallThrew =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(11, nameof(RedownloadCallThrew)),
                "DI Download() itself threw for {Operation}; continuing retry loop");

        public static void RedownloadCallThrew(ILogger logger, string operation, Exception exception)
            => _redownloadCallThrew(logger, operation, exception);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Dispatch: routes <see cref="PlayEffectCommand"/> / <see cref="StopEffectCommand"/> /
    /// <see cref="StopAllCommand"/> to their handlers.
    /// <para/>
    /// M9 contract — the returned <see cref="Task"/> <b>always completes successfully</b> on a classified,
    /// recoverable DirectInput failure (<see cref="DirectInputErrorClass.NeedsReacquire"/> /
    /// <see cref="DirectInputErrorClass.NeedsRedownload"/> / <see cref="DirectInputErrorClass.Transient"/>),
    /// including the retry-exhaustion and <see cref="DirectInputErrorClass.Fatal"/> cases: the retry
    /// wrapper logs a Warning and returns rather than throwing. Such failures are observable only via
    /// Warning-level logs ("effect not played"), never as a faulted Task. Non-classified exceptions
    /// (<see cref="ArgumentNullException"/>, <see cref="InvalidOperationException"/>,
    /// <see cref="ObjectDisposedException"/>, <see cref="OperationCanceledException"/> from a cancelled
    /// backoff delay, etc.) still propagate and fault the Task. The Stop paths are deliberately not routed
    /// through the retry wrapper — emergency stop is fast-fail-fast.
    /// </remarks>
    public Task ExecuteAsync(ForceCommand command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(command);

        return command switch
        {
            PlayEffectCommand play => HandlePlayAsync(play, cancellationToken),
            StopEffectCommand stop => HandleStopAsync(stop, cancellationToken),
            StopAllCommand => HandleStopAllAsync(cancellationToken),
            _ => Task.FromException(new InvalidOperationException(
                $"Unknown ForceCommand type: {command.GetType().Name}")),
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Emergency stop. Stops every active effect best-effort, then issues a device-wide
    /// <see cref="ForceFeedbackCommand.StopAll"/>. Shares <see cref="HandleStopAllAsync"/> with the
    /// <see cref="StopAllCommand"/> dispatch arm.
    /// </remarks>
    public Task StopAllAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return HandleStopAllAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves (cache-or-create) and starts the effect for a <see cref="PlayEffectCommand"/>. On a
    /// <see cref="ForceEffect.StateKey"/> collision the prior active effect is stopped and replaced. The
    /// <c>CreateEffect</c> and <c>Start</c> device calls are routed through
    /// <see cref="ExecuteWithReacquireAsync"/> (M9 re-acquire / re-download / transient retry); the
    /// prior-effect <c>Stop()</c> on a StateKey collision is a fast-fail-fast direct call, not retry-wrapped.
    /// </summary>
    private async Task HandlePlayAsync(PlayEffectCommand play, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // _device is nullable (set by InitializeAsync). Playback genuinely cannot proceed without it.
        var device = _device ?? throw new InvalidOperationException(
            "ExecuteAsync(PlayEffectCommand) requires InitializeAsync to have completed.");

        var effect = play.Effect;
        var cacheKey = ComputeCacheKey(effect, play.FinalIntensity);

        IDirectInputEffectAbstraction resolved;
        if (_effectCache.TryGetValue(cacheKey, out var cached))
        {
            Log.EffectCacheHit(_logger, effect.EffectId, effect.EffectType);
            resolved = cached;
        }
        else
        {
            Log.EffectCacheMiss(_logger, effect.EffectId, effect.EffectType);
            var parameters = BuildEffectParameters(effect, play.FinalIntensity);

            // BuildEffectParameters throws NotSupportedException for envelope-carrying effects and for
            // every EffectType outside { Periodic, ConstantForce }, so by this line the type is one of
            // those two — the ternary cannot mis-classify.
            var effectGuid = effect.EffectType == ForceEffectType.ConstantForce
                ? EffectGuid.ConstantForce
                : EffectGuid.Sine;

            // M9: CreateEffect returns a value, so capture it through a closure local. Nullable (NOT
            // `null!`): a genuine null after the wrapper means the wrapper exhausted its retry budget —
            // the post-call guard below depends on `created is null` being a reachable state.
            IDirectInputEffectAbstraction? created = null;
            await ExecuteWithReacquireAsync(
                () => created = device.CreateEffect(effectGuid, parameters),
                "CreateEffect",
                cancellationToken).ConfigureAwait(false);

            if (created is null)
            {
                // The retry wrapper exhausted its budget (or hit Fatal) and already logged a Warning
                // ("effect not played"). Must NOT fall through to GetOrAdd — publishing null into
                // _effectCache would corrupt the cache. Bail; the play is observably a no-op.
                return;
            }

            // Atomic publish. If another thread won this key's race, GetOrAdd returns the winner;
            // dispose our loser. !ReferenceEquals == we lost. Flow analysis has narrowed `created` to
            // non-null past the guard above.
            resolved = _effectCache.GetOrAdd(cacheKey, created);
            if (!ReferenceEquals(resolved, created))
            {
                created.Dispose();
            }
        }

        // StateKey-collision == replace. Stop + drop the prior active effect BEFORE starting the new one
        // (below) so two effects never share overlapping axes. Invariant: on a same-StateKey same-params
        // replay, the cache returned the same effect this `prior` references; a params change yields a
        // different effect (correct: stop old, start new). `prior != resolved` on a same-params replay
        // would indicate _effectCache corruption — documented expectation, not a guard.
        // The prior-effect Stop() is a fast-fail-fast direct call — NOT routed through the retry wrapper.
        if (effect.StateKey is not null
            && _activeByStateKey.TryRemove(effect.StateKey, out var prior))
        {
            prior.Stop();
        }

        // M9: Start routed through the retry wrapper. effectForRedownload: resolved — on a NeedsRedownload
        // classification the wrapper calls resolved.Download() before re-issuing Start. If Start exhausts
        // its retry budget the effect may not actually be playing, yet the code below still indexes it in
        // _activeByStateKey: the wrapper is non-throwing so HandlePlayAsync cannot observe the failure.
        // Accepted per §F ("effect not played is observable but non-fatal") — a stale _activeByStateKey
        // entry is harmless (a later Stop just stops a non-playing effect; a later same-key Play replaces it).
        await ExecuteWithReacquireAsync(
            () => resolved.Start(iterations: 1, EffectPlayFlags.None),
            "Start",
            cancellationToken,
            effectForRedownload: resolved).ConfigureAwait(false);

        if (effect.StateKey is not null)
        {
            _activeByStateKey[effect.StateKey] = resolved;
        }
    }

    /// <summary>
    /// Stops the sustained effect indexed under <see cref="StopEffectCommand.StateKey"/>. An unknown
    /// StateKey is a logged no-op — the resolver may emit a stop for an effect that already expired.
    /// </summary>
    private Task HandleStopAsync(StopEffectCommand stop, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_activeByStateKey.TryRemove(stop.StateKey, out var effect))
        {
            // Fast-fail-fast: a classified Stop() failure propagates by design — Stop is NOT routed
            // through the M9 retry wrapper (only HandlePlayAsync's CreateEffect/Start are).
            effect.Stop();
            Log.EffectStopped(_logger, stop.StateKey);
        }
        else
        {
            Log.StopUnknownStateKey(_logger, stop.StateKey);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Emergency stop. Snapshots and clears the active-effect index, stops each effect best-effort, then
    /// issues a device-wide <see cref="ForceFeedbackCommand.StopAll"/>. Effects stay in
    /// <see cref="_effectCache"/> for reuse / disposal — only the active index is cleared.
    /// </summary>
    private Task HandleStopAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // ConcurrentDictionary.ToArray() is an atomic snapshot (intrinsic, no LINQ). Snapshot, then clear.
        var active = _activeByStateKey.ToArray();
        _activeByStateKey.Clear();

        foreach (var (stateKey, effect) in active)
        {
            try
            {
                effect.Stop();
            }
            catch (Exception ex)
            {
                // Best-effort emergency stop: one effect failing to Stop() must not block the others or
                // the device-wide StopAll below. Stop() classified failures are NOT routed through the M9
                // retry wrapper — emergency stop is fast-fail-fast; the try/catch + Warning log is the
                // recovery posture here by design.
                Log.StopAllEffectFailed(_logger, stateKey, ex);
            }
        }

        // Hardware-level guarantee. _device is nullable; an emergency stop with no acquired device is a
        // benign no-op (null-conditional, lenient).
        _device?.SendForceFeedbackCommand(ForceFeedbackCommand.StopAll);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs <paramref name="operation"/> against the device, classifying any thrown DirectInput exception
    /// (via <see cref="DirectInputErrorClassifier"/>) and applying the M9 recovery strategy: re-acquire
    /// (count-based, max 3 attempts, 50/200/500 ms backoff), re-download (one-shot), or transient retry
    /// (one-shot, immediate). The three retry budgets are independent. On exhaustion of any path — and on
    /// a <see cref="DirectInputErrorClass.Fatal"/> classification — this method logs a Warning and
    /// <b>returns</b>; it never throws a classified exception to the caller (see <see cref="ExecuteAsync"/>
    /// remarks for the full contract). Non-classified exceptions (e.g.
    /// <see cref="OperationCanceledException"/> from a cancelled backoff delay) propagate.
    /// </summary>
    /// <param name="operation">The device call to run and, on a classified failure, re-issue.</param>
    /// <param name="operationName">Short operation label for log correlation (e.g. "CreateEffect", "Start").</param>
    /// <param name="cancellationToken">Cancellation for the re-acquire backoff delay.</param>
    /// <param name="effectForRedownload">
    /// The effect to <see cref="IDirectInputEffectAbstraction.Download"/> before re-issuing
    /// <paramref name="operation"/> on the <see cref="DirectInputErrorClass.NeedsRedownload"/> path. Pass
    /// the effect for a <c>Start</c> operation; pass <see langword="null"/> for <c>CreateEffect</c> — there
    /// is no effect yet to re-download, so <c>NeedsRedownload</c> is an unreachable classification for
    /// <c>CreateEffect</c>. If a <see langword="null"/> effect ever does hit the redownload path, the
    /// <c>?.</c> short-circuits the Download call and the wrapper logs a Warning and returns — consistent
    /// with the non-propagating contract; the failure is never silently swallowed.
    /// </param>
    private async Task ExecuteWithReacquireAsync(
        Action operation,
        string operationName,
        CancellationToken cancellationToken,
        IDirectInputEffectAbstraction? effectForRedownload = null)
    {
        var reacquireAttempts = 0;
        var redownloadTried = false;
        var transientTried = false;
        var anyRetryAttempted = false;
        var lastClassification = string.Empty;

        while (true)
        {
            try
            {
                operation();
                if (anyRetryAttempted)
                {
                    // The operation succeeded on a re-issue after a classified failure — record the
                    // transparent recovery so diagnostics can distinguish it from a never-failed call.
                    Log.RetryPathRecovered(_logger, operationName, lastClassification);
                }

                return;
            }
            catch (Exception primaryEx)
            {
                // Broad catch by design: the operation is an opaque device call; DirectInputErrorClassifier
                // is the single place that knows DirectInput HRESULTs and classifies anything (with a
                // _ => Fatal fallback). Fatal is logged at Warning and the Task completes successfully per
                // the M9 contract — narrowing the catch would instead fault the Task on unexpected
                // exceptions, contradicting §F's posture. CA1031 is .editorconfig 'suggestion' — this
                // comment is the justification; no [SuppressMessage] needed.
                var classification = DirectInputErrorClassifier.Classify(primaryEx);

                switch (classification)
                {
                    case DirectInputErrorClass.NeedsReacquire:
                        if (reacquireAttempts >= 3)
                        {
                            Log.RetryPathExhausted(_logger, operationName, "re-acquire exhausted after 3 attempts", primaryEx);
                            return;
                        }

                        var delayMs = reacquireAttempts switch { 0 => 50, 1 => 200, _ => 500 };
                        Log.RetryAttempt(
                            _logger,
                            operationName,
                            $"NeedsReacquire attempt {reacquireAttempts + 1} of 3: {primaryEx.Message}",
                            delayMs);

                        // Backoff. A cancelled token makes Task.Delay throw OperationCanceledException —
                        // NOT a classified DI exception, so it propagates out of this method and faults
                        // the Task. That is the intended M9 behavior (non-classified exceptions propagate).
                        await _delayStrategy(delayMs, cancellationToken).ConfigureAwait(false);

                        try
                        {
                            // _device is the field; non-null whenever a wrapped call runs (HandlePlayAsync's
                            // `device` local guard throws first). The ?. keeps the wrapper independently
                            // null-safe and warning-free without a !-bang.
                            _device?.Unacquire();
                            _device?.Acquire();
                        }
                        catch (Exception reacquireEx)
                        {
                            // The Unacquire/Acquire recovery action itself threw — log and continue, do NOT
                            // re-classify it. The loop re-issues operation() next, which re-triggers
                            // classification on the real failure; the bounded reacquireAttempts budget
                            // guarantees termination regardless.
                            Log.ReacquireCallThrew(_logger, operationName, reacquireEx);
                        }

                        reacquireAttempts++;
                        anyRetryAttempted = true;
                        lastClassification = "NeedsReacquire";
                        continue;

                    case DirectInputErrorClass.NeedsRedownload:
                        if (redownloadTried)
                        {
                            Log.RetryPathExhausted(_logger, operationName, "re-download already attempted", primaryEx);
                            return;
                        }

                        redownloadTried = true;
                        Log.RetryAttempt(_logger, operationName, $"NeedsRedownload: {primaryEx.Message}", delayMs: 0);

                        // §F gap fix: re-download before re-issuing. For CreateEffect, effectForRedownload is
                        // null (no effect yet) — the ?. short-circuits and the operation is still re-issued
                        // via the loop; the spent one-bite flag means a second NeedsRedownload exhausts
                        // cleanly. continue (not an inline retry) so a follow-on failure is re-classified
                        // and the reacquire budget stays independent of this bite.
                        try
                        {
                            effectForRedownload?.Download();
                        }
                        catch (Exception downloadEx)
                        {
                            // The Download() recovery action itself threw — log and continue. The loop
                            // re-issues operation() next, which re-triggers classification on whatever real
                            // failure follows. Symmetric with the Unacquire/Acquire catch in the reacquire
                            // path — M9 contract: classified DI failures (including failures in recovery
                            // actions) don't propagate.
                            Log.RedownloadCallThrew(_logger, operationName, downloadEx);
                        }

                        anyRetryAttempted = true;
                        lastClassification = "NeedsRedownload";
                        continue;

                    case DirectInputErrorClass.Transient:
                        if (transientTried)
                        {
                            Log.RetryPathExhausted(_logger, operationName, "transient retry already attempted", primaryEx);
                            return;
                        }

                        transientTried = true;
                        Log.RetryAttempt(_logger, operationName, $"Transient: {primaryEx.Message}", delayMs: 0);

                        // Transient retries IMMEDIATELY — no _delayStrategy call, no recovery action; the
                        // re-issue itself IS the recovery. continue (not an inline retry) so a follow-on
                        // failure is re-classified and the reacquire budget stays independent of this bite.
                        anyRetryAttempted = true;
                        lastClassification = "Transient";
                        continue;

                    case DirectInputErrorClass.Fatal:
                    default:
                        Log.RetryPathExhausted(_logger, operationName, "fatal error", primaryEx);
                        return;
                }
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Disposes every cached effect, then the underlying device adapter, then transitions to
    /// <see cref="DeviceState.Disconnected"/> as the terminal state so post-dispose <see cref="State"/> reads
    /// remain coherent. The <see cref="StateChanged"/> event fires one last time on the way down. Relies on
    /// the adapters' own narrow <see cref="SharpGen.Runtime.SharpGenException"/> swallow — no defensive
    /// double-swallow here.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        foreach (var effect in _effectCache.Values)
        {
            // No per-effect try/catch: VorticeDirectInputEffectAdapter.Dispose swallows SharpGenException
            // narrowly around its risky pre-dispose Stop() call — no double-swallow here (M5 pattern).
            effect.Dispose();
        }

        _effectCache.Clear();
        _activeByStateKey.Clear();

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

    // ── Effect cache key (T-07 M7) ───────────────────────────────────────────────────────────
    // ComputeCacheKey deliberately uses different rounding math than BuildEffectParameters above:
    // the cache key mirrors the legacy DirectInputForceFeedbackDevice.GetCacheKey (3-decimal rounding,
    // expressed as x1000 integers), while BuildEffectParameters uses device-unit math. The legacy
    // GetCacheKey and the legacy effect-param builders were always separate functions with different
    // rounding — this is legacy-faithful, not a coherence bug. Do not "unify" them.

    /// <summary>
    /// Composite cache key for the effect cache. Shape per PRP §15 (catalog-identity key): two plays of the
    /// same catalog effect at the same rounded parameters share one downloaded DirectInput effect. Mirrors
    /// the legacy <c>DirectInputForceFeedbackDevice.GetCacheKey</c> composite (CLAUDE.md hard-rule #6 —
    /// preserve PRP §14.2 effect-cache composite keying). Direction is intentionally not in the key: in T-07
    /// it is always <c>(1,1)</c> (the only caller passes <c>0,0</c>), and <see cref="EffectId"/> already
    /// pins a catalog effect with a fixed direction. T-14 revisits if its resolver introduces varying
    /// directions. This shape resolves the PRP §15 vs T-07.md cache-key contradiction flagged by the T-07 plan.
    /// </summary>
    /// <param name="EffectId">Catalog identity (<see cref="ForceEffect.EffectId"/>); legacy <c>Name</c>.</param>
    /// <param name="EffectType">Catalog effect-type discrimination; legacy <c>Kind</c>.</param>
    /// <param name="IntensityRoundedThousandths">Final intensity clamped to [0,1], x1000, rounded to nearest int.</param>
    /// <param name="DurationMilliseconds">Effect duration in whole milliseconds, clamped to [0, <see cref="int.MaxValue"/>].</param>
    /// <param name="FrequencyRoundedThousandths">Frequency in Hz, x1000, rounded to nearest int.</param>
    /// <param name="StateKey">The effect's state key, or the empty string when it has none.</param>
    internal readonly record struct DeviceCacheKey(
        string EffectId,
        ForceEffectType EffectType,
        int IntensityRoundedThousandths,
        int DurationMilliseconds,
        int FrequencyRoundedThousandths,
        string StateKey);

    /// <summary>
    /// Computes the <see cref="DeviceCacheKey"/> for an effect at its gain-resolved final intensity. Pure
    /// function — no device interaction. Rounding precision (3 decimals, expressed as x1000 integers)
    /// mirrors the legacy <c>GetCacheKey</c>. Exercised directly by the M7 cache tests and, from M8, by
    /// <c>HandlePlayAsync</c>.
    /// </summary>
    /// <param name="effect">The catalog effect descriptor.</param>
    /// <param name="finalIntensity">Gain-stack-resolved intensity in [0.0, 1.0] (from <c>PlayEffectCommand.FinalIntensity</c>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
    internal static DeviceCacheKey ComputeCacheKey(ForceEffect effect, double finalIntensity)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return new DeviceCacheKey(
            EffectId: effect.EffectId,
            EffectType: effect.EffectType,
            IntensityRoundedThousandths: (int)Math.Round(Math.Clamp(finalIntensity, 0.0, 1.0) * 1000.0),
            DurationMilliseconds: (int)Math.Clamp(effect.Duration.TotalMilliseconds, 0.0, int.MaxValue),
            FrequencyRoundedThousandths: (int)Math.Round(effect.FrequencyHz * 1000.0),
            StateKey: effect.StateKey ?? string.Empty);
    }
}
