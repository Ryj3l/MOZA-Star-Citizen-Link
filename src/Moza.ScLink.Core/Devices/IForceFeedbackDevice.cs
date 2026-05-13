using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Core.Devices;

/// <summary>Contract for force-feedback output device implementations.</summary>
public interface IForceFeedbackDevice : IAsyncDisposable
{
    /// <summary>MOZA hardware model of this device.</summary>
    DeviceModel Model { get; }

    /// <summary>Human-readable display name, e.g. "MOZA AB6".</summary>
    string DisplayName { get; }

    /// <summary>Product name as reported by DirectInput.</summary>
    string ProductName { get; }

    /// <summary>DirectInput instance GUID for this device.</summary>
    Guid InstanceGuid { get; }

    /// <summary>Capabilities reported by the device at initialization.</summary>
    DeviceCapabilities Capabilities { get; }

    /// <summary>Current lifecycle state.</summary>
    DeviceState State { get; }

    /// <summary>Raised when the device's lifecycle state changes.</summary>
    event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    /// <summary>Initializes the device and acquires the DirectInput cooperative level.</summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Executes a <see cref="ForceCommand"/> on the device.</summary>
    Task ExecuteAsync(ForceCommand command, CancellationToken cancellationToken);

    /// <summary>Stops all active effects immediately (emergency stop path).</summary>
    Task StopAllAsync(CancellationToken cancellationToken);
}
