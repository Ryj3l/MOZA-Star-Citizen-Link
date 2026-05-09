using MozaStarCitizen.App.Models;

namespace MozaStarCitizen.App.ForceFeedback;

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
