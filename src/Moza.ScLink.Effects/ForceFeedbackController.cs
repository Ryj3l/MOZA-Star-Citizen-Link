using Moza.ScLink.Core;
using Moza.ScLink.Core.Models;

// Cannot `using Moza.ScLink.Core.Devices;` — it would shadow the legacy IForceFeedbackDevice
// this controller wraps (CS0104 ambiguity, same as FallbackForceFeedbackDevice). Pass-2
// references to that namespace's types (IChainStateChangedSource, IDeviceAvailabilityObserver,
// ChainStateChangedEventArgs) are explicitly full-qualified inline. Mirrors the Fallback pattern.

namespace Moza.ScLink.Effects;

public sealed class ForceFeedbackController
{
    private static readonly TimeSpan DuplicateImpactWindow = TimeSpan.FromMilliseconds(750);
    private readonly IForceFeedbackDevice _device;
    private DateTimeOffset _lastLandingImpact = DateTimeOffset.MinValue;

    public ForceFeedbackController(IForceFeedbackDevice device)
    {
        _device = device;
    }

    public string OutputName => _device.Name;

    public string OutputStatus => _device.Status;

    public IForceFeedbackDevice Device => _device;

    // ── T-07 Issue #27 Pass-2 G3-Interpretation-B parallel surfaces ────────────────────────
    // Both members reach the underlying chain via Core.Devices interfaces (IChainStateChangedSource
    // and IDeviceAvailabilityObserver) — Effects.csproj stays Core-only, no DirectInput dependency,
    // no concrete-type naming. If _device does not implement the relevant interface (Phase-2
    // chain swap), both behave as silent no-ops rather than throwing — the right failure mode
    // for forward-compatibility.

    /// <summary>
    /// Re-exposes the underlying chain's <c>ChainStateChanged</c> event when the chain implements
    /// <see cref="Moza.ScLink.Core.Devices.IChainStateChangedSource"/>. Silent no-op otherwise.
    /// </summary>
    public event EventHandler<Moza.ScLink.Core.Devices.ChainStateChangedEventArgs>? ChainStateChanged
    {
        add { if (_device is Moza.ScLink.Core.Devices.IChainStateChangedSource s) s.ChainStateChanged += value; }
        remove { if (_device is Moza.ScLink.Core.Devices.IChainStateChangedSource s) s.ChainStateChanged -= value; }
    }

    /// <summary>
    /// Passthrough getter for the chain's
    /// <see cref="Moza.ScLink.Core.Devices.IDeviceAvailabilityObserver"/> when implemented; the
    /// WPF window's WM_DEVICECHANGE hook (B6) reaches this via
    /// <c>MainViewModel.DeviceAvailabilityObserver</c> (B5.4). Null when the chain does not
    /// implement the observer interface.
    /// </summary>
    public Moza.ScLink.Core.Devices.IDeviceAvailabilityObserver? DeviceAvailabilityObserver =>
        _device as Moza.ScLink.Core.Devices.IDeviceAvailabilityObserver;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _device.InitializeAsync(cancellationToken);
        await _device.PrepareAsync(GetStandardEffects(), cancellationToken);
    }

    public async Task<string> HandleAsync(ScGameEvent gameEvent, CancellationToken cancellationToken)
    {
        switch (gameEvent.Kind)
        {
            case ScEventKind.QuantumSpoolStarted:
                await _device.PlayAsync(new ForceEffect(
                    ForceEffectKind.PeriodicVibration,
                    "Quantum spool vibration",
                    gameEvent.Intensity <= 0 ? 0.42 : gameEvent.Intensity,
                    gameEvent.Duration == TimeSpan.Zero ? TimeSpan.FromSeconds(8) : gameEvent.Duration,
                    34,
                    "quantum-spool"), cancellationToken);
                return "Quantum spool vibration started.";

            case ScEventKind.QuantumSpoolEnded:
                await _device.StopAsync("quantum-spool", cancellationToken);
                return "Quantum spool vibration stopped.";

            case ScEventKind.LandingImpact:
                if (gameEvent.Timestamp - _lastLandingImpact < DuplicateImpactWindow)
                {
                    return "Landing/impact duplicate ignored.";
                }

                await _device.PlayAsync(new ForceEffect(
                    ForceEffectKind.Bump,
                    "Landing/impact bump",
                    gameEvent.Intensity <= 0 ? 0.7 : gameEvent.Intensity,
                    gameEvent.Duration == TimeSpan.Zero ? TimeSpan.FromMilliseconds(260) : gameEvent.Duration,
                    0,
                    null), cancellationToken);
                _lastLandingImpact = gameEvent.Timestamp;
                return "Landing/impact bump triggered.";

            case ScEventKind.AtmosphereEntered:
                await _device.PlayAsync(new ForceEffect(
                    ForceEffectKind.StateVibration,
                    "In-atmosphere vibration",
                    gameEvent.Intensity <= 0 ? 0.22 : gameEvent.Intensity,
                    TimeSpan.Zero,
                    18,
                    "atmosphere"), cancellationToken);
                return "In-atmosphere vibration started.";

            case ScEventKind.AtmosphereExited:
                await _device.StopAsync("atmosphere", cancellationToken);
                return "In-atmosphere vibration stopped.";

            default:
                return "Event ignored.";
        }
    }

    public Task StopAllAsync(CancellationToken cancellationToken) =>
        _device.StopAllAsync(cancellationToken);

    private static IReadOnlyList<ForceEffect> GetStandardEffects() =>
    [
        new ForceEffect(
            ForceEffectKind.PeriodicVibration,
            "Quantum spool vibration",
            0.42,
            TimeSpan.FromSeconds(8),
            34,
            "quantum-spool"),
        new ForceEffect(
            ForceEffectKind.Bump,
            "Landing/impact bump",
            0.75,
            TimeSpan.FromMilliseconds(260),
            0,
            null),
        new ForceEffect(
            ForceEffectKind.StateVibration,
            "In-atmosphere vibration",
            0.22,
            TimeSpan.Zero,
            18,
            "atmosphere")
    ];
}
