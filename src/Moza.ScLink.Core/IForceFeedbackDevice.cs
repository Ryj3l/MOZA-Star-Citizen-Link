using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Core;

/// <summary>Legacy pre-migration force-feedback device interface. Superseded by <see cref="Moza.ScLink.Core.Devices.IForceFeedbackDevice"/> in T-06.</summary>
public interface IForceFeedbackDevice
{
    /// <summary>Display name of the device.</summary>
    public string Name { get; }

    /// <summary>Human-readable device status string.</summary>
    public string Status { get; }

    /// <summary>Initializes the device.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Pre-loads the specified effects onto the device.</summary>
    public Task PrepareAsync(IEnumerable<ForceEffect> effects, CancellationToken cancellationToken);

    /// <summary>Plays the specified effect.</summary>
    public Task PlayAsync(ForceEffect effect, CancellationToken cancellationToken);

    /// <summary>Stops the sustained effect identified by the given state key.</summary>
    public Task StopAsync(string stateKey, CancellationToken cancellationToken);

    /// <summary>Stops all active effects.</summary>
    public Task StopAllAsync(CancellationToken cancellationToken);
}
