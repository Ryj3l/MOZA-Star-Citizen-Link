using Moza.ScLink.Core;
using Moza.ScLink.Core.Diagnostics;
using Moza.ScLink.Core.Models;

// Cannot `using Moza.ScLink.Core.Devices;` — it would shadow the legacy IForceFeedbackDevice this
// class is built on (CS0104 ambiguity). Pass-2 references to that namespace's types (currently:
// IDeviceAvailabilityObserver; segment (c) adds ChainStateChangedEventArgs) are explicitly
// full-qualified inline. Mirrors the LegacyForceFeedbackDeviceAdapter pattern of full-qualifying
// the legacy contract on the class declaration to keep both namespaces accessible.

namespace Moza.ScLink.DirectInput;

public sealed class FallbackForceFeedbackDevice
    : IForceFeedbackDevice,
      Moza.ScLink.Core.Devices.IDeviceAvailabilityObserver,
      Moza.ScLink.Core.Devices.IChainStateChangedSource
{
    // T-07 Issue #27 Pass-2 C-fix: `_secondaryDevices` holds the chain's NON-DI tiers
    // (SDK / NativeBridge / Null). The DI tier lives EXCLUSIVELY in `_directInputSlot` —
    // single source of truth for the hot-pluggable slot, structurally eliminating the
    // dual-source bug class that V9 A6 surfaced (stale _devices[0] reference iterated
    // after hot-arrival → ObjectDisposedException). Post-construction iteration MUST go
    // through `AllDevices()` (prepends `_directInputSlot` when non-null); direct access
    // to `_secondaryDevices` is ctor-time only (Null-tier scan).
    private readonly IForceFeedbackDevice[] _secondaryDevices;
    private readonly HashSet<IForceFeedbackDevice> _initializedDevices = [];
    private readonly Func<IForceFeedbackDevice?>? _directInputProvider;
    private readonly IForceFeedbackDevice _nullSlot;
    private IForceFeedbackDevice? _directInputSlot;
    private IForceFeedbackDevice? _currentDevice;

    /// <summary>
    /// Canonical ctor — single source of truth for the DI tier. The factory uses this form.
    /// </summary>
    /// <remarks>
    /// All three parameters are explicit (no defaults) so 1-arg and 2-arg call sites resolve
    /// unambiguously to the <c>internal</c> convenience overload below, which is the test-fixture
    /// path. The factory always passes all three (the third is named for readability:
    /// <c>initialDirectInputSlot: directInput</c>).
    /// </remarks>
    public FallbackForceFeedbackDevice(
        IEnumerable<IForceFeedbackDevice> secondaryDevices,
        Func<IForceFeedbackDevice?>? directInputProvider,
        IForceFeedbackDevice? initialDirectInputSlot)
    {
        _secondaryDevices = secondaryDevices.ToArray();
        _directInputProvider = directInputProvider;
        _directInputSlot = initialDirectInputSlot;

        // Defensive ctor-time guard: the Null tier is the chain's always-present terminus that
        // OnDeviceRemoved restores _currentDevice to on DI hot-loss. A composition without it
        // would leave _currentDevice in an unrecoverable state after the first removal. Fail loud
        // and early at construction rather than at the first hot-loss event.
        _nullSlot = _secondaryDevices.OfType<NullForceFeedbackDevice>().SingleOrDefault()
            ?? throw new ArgumentException(
                "FallbackForceFeedbackDevice requires the chain to contain a NullForceFeedbackDevice " +
                "as its terminal tier (Pass-2 chain re-selection restores _currentDevice to it on " +
                "DirectInput hot-loss).",
                nameof(secondaryDevices));
    }

    /// <summary>
    /// Convenience overload for test fixtures composing chains as a flat device list. Splits the
    /// input into the canonical (secondaryDevices, initialDirectInputSlot) shape at ctor time;
    /// the split is a TRANSIENT scan, not a persistent second source. After ctor returns, state
    /// is single-source — `_directInputSlot` is the sole reference to the DI tier, and
    /// `_secondaryDevices` is filtered to exclude IDirectInputAdapterSlot instances. The `Where`
    /// and `FirstOrDefault` use the SAME `is IDirectInputAdapterSlot` predicate, so they agree
    /// on what's a DI tier (no test composes a multi-DI chain).
    /// </summary>
    /// <remarks>
    /// Back-compat scaffolding for existing D2/D3/D4 test fixtures that compose chains as a
    /// flat list. New tests should prefer the canonical 3-arg ctor for structural clarity.
    /// </remarks>
    internal FallbackForceFeedbackDevice(
        IEnumerable<IForceFeedbackDevice> devices,
        Func<IForceFeedbackDevice?>? directInputProvider = null)
        : this(
            secondaryDevices: devices.Where(d => d is not IDirectInputAdapterSlot),
            directInputProvider: directInputProvider,
            initialDirectInputSlot: devices.FirstOrDefault(d => d is IDirectInputAdapterSlot))
    { }

    public string Name => _currentDevice is null
        ? "Auto output"
        : $"Auto output ({_currentDevice.Name})";

    public string Status => string.Join(" -> ", AllDevices().Select(d => d.Name));

    /// <summary>
    /// Raised on every transition of the chain's currently-selected tier — initialization success,
    /// fall-through to a working tier on play failure, hot-arrival of the DirectInput tier, and
    /// hot-removal of the DirectInput tier. G3-Interpretation-B aggregate surface (T-07 Issue #27
    /// Pass-2).
    /// </summary>
    /// <remarks>
    /// Plan B2 #4's underlying-device StateChanged aggregation was descoped from Pass-2 (issue
    /// #34): empirical verification showed the aggregation had zero production firing points
    /// (Faulted is dormant; subscribe-after-init misses Ready; unsubscribe-before-dispose misses
    /// Disconnected). Re-entry point when Faulted transitions are wired.
    /// </remarks>
    public event EventHandler<Moza.ScLink.Core.Devices.ChainStateChangedEventArgs>? ChainStateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_currentDevice is not null)
        {
            return;
        }

        foreach (var device in AllDevices())
        {
            try
            {
                await InitializeDeviceAsync(device, cancellationToken);
                TransitionCurrentDevice(device);
                return;
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"{device.Name} initialize failed");
            }
        }
    }

    public async Task PrepareAsync(IEnumerable<ForceEffect> effects, CancellationToken cancellationToken)
    {
        if (_currentDevice is null)
        {
            return;
        }

        await _currentDevice.PrepareAsync(effects, cancellationToken);
    }

    public Task PlayAsync(ForceEffect effect, CancellationToken cancellationToken) =>
        TryDevicesAsync(device => device.PlayAsync(effect, cancellationToken), $"play {effect.Name}");

    public Task StopAsync(string stateKey, CancellationToken cancellationToken) =>
        StopInitializedAsync(device => device.StopAsync(stateKey, cancellationToken), $"stop {stateKey}");

    public Task StopAllAsync(CancellationToken cancellationToken) =>
        StopInitializedAsync(device => device.StopAllAsync(cancellationToken), "stop all");

    // ── IDeviceAvailabilityObserver ────────────────────────────────────────────────────────
    // T-07 Issue #27 Pass-2: WPF window's WM_DEVICECHANGE hook routes USB plug/unplug events
    // through these methods. Both are fire-and-forget: the chain re-selection work is async and
    // the OS notification path must return promptly. Provider-null guards keep the surface inert
    // for test compositions and non-DI output modes (factory passes a () => null delegate when
    // outputMode is not DirectInput/Auto — see E-B7).

    public void OnDeviceArrived()
    {
        DeviceChangeProbe.LogObserver(nameof(OnDeviceArrived));
        if (_directInputProvider is null) return;
        _ = DispatchArrivalAsync();
    }

    public void OnDeviceRemoved()
    {
        DeviceChangeProbe.LogObserver(nameof(OnDeviceRemoved));
        // Policy 3 DI-scope: only the DirectInput tier responds to hot-loss. If the currently
        // active tier is anything else, ignore — the SDK/Null tiers have no WM_DEVICECHANGE
        // observability and stay init-time-selected. _directInputSlot is the chain's reference
        // to the DI adapter (segment b establishes the field tracking).
        if (!ReferenceEquals(_currentDevice, _directInputSlot)) return;
        _ = DispatchRemovalAsync();
    }

    private async Task TryDevicesAsync(Func<IForceFeedbackDevice, Task> action, string operation)
    {
        Exception? lastException = null;

        foreach (var device in AllDevices())
        {
            try
            {
                await InitializeDeviceAsync(device, CancellationToken.None);
                await action(device);
                // T-07 Issue #27 Pass-2 hot-removal coordination: if DispatchRemovalAsync
                // disposed this device while we awaited its action (the race plan called out as
                // 'race-safe'), DisposeDirectInputSlotAsync removed it from _initializedDevices.
                // Verified at file-scan time: that helper is the ONLY removal path, so
                // "not in _initializedDevices" reliably means "disposed/invalid." Abort the
                // transition; the chain stays in the post-removal state DispatchRemovalAsync set.
                if (!_initializedDevices.Contains(device)) return;
                TransitionCurrentDevice(device);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                AppLog.Write(ex, $"{device.Name} {operation} failed");
            }
        }

        if (lastException is not null)
        {
            throw new InvalidOperationException($"All force feedback outputs failed to {operation}. Last failure: {lastException.Message}", lastException);
        }
    }

    private async Task StopInitializedAsync(Func<IForceFeedbackDevice, Task> action, string operation)
    {
        foreach (var device in _initializedDevices.ToArray())
        {
            try
            {
                await action(device);
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"{device.Name} {operation} failed");
            }
        }
    }

    private async Task InitializeDeviceAsync(IForceFeedbackDevice device, CancellationToken cancellationToken)
    {
        if (_initializedDevices.Contains(device))
        {
            return;
        }

        await device.InitializeAsync(cancellationToken);
        _initializedDevices.Add(device);
    }

    // ── Hot-plug async dispatch helpers (T-07 Issue #27 Pass-2) ────────────────────────────
    // Called fire-and-forget from OnDeviceArrived / OnDeviceRemoved. Marshal the chain
    // re-selection work onto the background ThreadPool — the chain layer is UI-thread-agnostic;
    // subscribers (e.g. MainViewModel) own their own thread-affinity routing on ChainStateChanged.

    private async Task DispatchArrivalAsync()
    {
        if (_directInputProvider is null) return;
        var fresh = _directInputProvider();
        if (fresh is null) return;  // USB-stack race: device not enumerable yet; next DBT_DEVICEARRIVAL retries.

        // Concern-1 fix: handle double-arrival (composite-HID parent/child enumeration, exactly
        // the F2a probe rationale). Without this, a second DBT_DEVICEARRIVAL before any removal
        // would orphan the first slot's subscription + underlying handle, leaving an unreachable
        // adapter still wired to OnAdapterStateChanged — a spurious-UI-notification hazard plus
        // a handle leak. Teardown-then-replace.
        if (_directInputSlot is not null)
        {
            await DisposeDirectInputSlotAsync();
        }

        try
        {
            await InitializeDeviceAsync(fresh, CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Hot-arrival DirectInput initialize failed");
            return;
        }

        _directInputSlot = fresh;
        TransitionCurrentDevice(fresh);
    }

    private async Task DispatchRemovalAsync()
    {
        if (_directInputSlot is null) return;
        await DisposeDirectInputSlotAsync();
        TransitionCurrentDevice(_nullSlot);
    }

    private async Task DisposeDirectInputSlotAsync()
    {
        var slot = _directInputSlot;
        if (slot is null) return;

        try
        {
            // Chain-owned disposal pathway per H5: adapter stays NOT IAsyncDisposable; the chain
            // calls DisposeWrappedAsync on the IDirectInputAdapterSlot marker (E-B3 disposition) —
            // chain owns timing, adapter owns mechanics. No obsolete-type naming here.
            await ((IDirectInputAdapterSlot)slot).DisposeWrappedAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "DirectInput slot dispose failed");
        }

        _initializedDevices.Remove(slot);
        _directInputSlot = null;
    }

    // ── ChainStateChanged plumbing (T-07 Issue #27 Pass-2 segment c) ───────────────────────

    /// <summary>
    /// Live iteration sequence for the chain's tiers. Prepends <see cref="_directInputSlot"/>
    /// when non-null; skips the DI position entirely when null (post-hot-removal). This is the
    /// CANONICAL iteration source for all post-construction consumers (Status, InitializeAsync,
    /// TryDevicesAsync). Direct iteration of <see cref="_secondaryDevices"/> is ctor-time only
    /// (Null-tier scan in the canonical ctor).
    /// </summary>
    private IEnumerable<IForceFeedbackDevice> AllDevices()
    {
        if (_directInputSlot is not null)
            yield return _directInputSlot;
        foreach (var device in _secondaryDevices)
            yield return device;
    }

    private void TransitionCurrentDevice(IForceFeedbackDevice next)
    {
        if (ReferenceEquals(_currentDevice, next)) return;
        _currentDevice = next;
        RaiseChainStateChanged(isReady: !ReferenceEquals(next, _nullSlot));
    }

    private void RaiseChainStateChanged(bool isReady)
    {
        var handler = ChainStateChanged;
        if (handler is null) return;
        handler(this, new Moza.ScLink.Core.Devices.ChainStateChangedEventArgs
        {
            // Asymmetric by design (OutputName = composite, OutputStatus = raw current-device):
            // OutputName is a label and the composite ("Auto output (DirectInput: AB9 FFB Base)")
            // is the right label shape for the UI. OutputStatus is a status line and the current
            // device's raw status ("Using Windows DirectInput force feedback." vs. preview text)
            // is what the user needs to see when the chain transitions. Fallback.Status
            // (chain composition) is invariant across Pass-2 transitions and would render
            // args.OutputStatus reactive-but-effectively-constant, which D4 test 2 caught as a
            // plan-conformance defect against plan B1's "Current device's Status string"
            // specification.
            OutputName = Name,
            OutputStatus = _currentDevice?.Status ?? Status,
            IsReady = isReady,
        });
    }

}
