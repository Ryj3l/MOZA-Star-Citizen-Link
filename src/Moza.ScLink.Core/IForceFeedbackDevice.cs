using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Core;

public interface IForceFeedbackDevice
{
    public string Name { get; }

    public string Status { get; }

    public Task InitializeAsync(CancellationToken cancellationToken);

    public Task PrepareAsync(IEnumerable<ForceEffect> effects, CancellationToken cancellationToken);

    public Task PlayAsync(ForceEffect effect, CancellationToken cancellationToken);

    public Task StopAsync(string stateKey, CancellationToken cancellationToken);

    public Task StopAllAsync(CancellationToken cancellationToken);
}
