using MozaStarCitizen.App.Models;

namespace MozaStarCitizen.App.ForceFeedback;

public interface IForceFeedbackDevice
{
    string Name { get; }

    string Status { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task PrepareAsync(IEnumerable<ForceEffect> effects, CancellationToken cancellationToken);

    Task PlayAsync(ForceEffect effect, CancellationToken cancellationToken);

    Task StopAsync(string stateKey, CancellationToken cancellationToken);

    Task StopAllAsync(CancellationToken cancellationToken);
}
