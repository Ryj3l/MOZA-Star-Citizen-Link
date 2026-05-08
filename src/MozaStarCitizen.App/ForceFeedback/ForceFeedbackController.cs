using MozaStarCitizen.App.Models;

namespace MozaStarCitizen.App.ForceFeedback;

public sealed class ForceFeedbackController
{
    private readonly IForceFeedbackDevice _device;

    public ForceFeedbackController(IForceFeedbackDevice device)
    {
        _device = device;
    }

    public string OutputName => _device.Name;

    public string OutputStatus => _device.Status;

    public IForceFeedbackDevice Device => _device;

    public Task InitializeAsync(CancellationToken cancellationToken) =>
        _device.InitializeAsync(cancellationToken);

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
                await _device.PlayAsync(new ForceEffect(
                    ForceEffectKind.Bump,
                    "Landing/impact bump",
                    gameEvent.Intensity <= 0 ? 0.7 : gameEvent.Intensity,
                    gameEvent.Duration == TimeSpan.Zero ? TimeSpan.FromMilliseconds(260) : gameEvent.Duration,
                    0,
                    null), cancellationToken);
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
}
