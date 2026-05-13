namespace Moza.ScLink.Core.Models;

/// <summary>Lifecycle state of an <see cref="Moza.ScLink.Core.Devices.IForceFeedbackDevice"/> implementation.</summary>
public enum DeviceState
{
    /// <summary>Device is not connected.</summary>
    Disconnected,
    /// <summary>Device is being detected via DirectInput enumeration.</summary>
    Detecting,
    /// <summary>Device is being initialized and cooperative level is being acquired.</summary>
    Initializing,
    /// <summary>Device is initialized and ready to accept commands.</summary>
    Ready,
    /// <summary>Device has encountered a fault requiring re-initialization.</summary>
    Faulted,
}
