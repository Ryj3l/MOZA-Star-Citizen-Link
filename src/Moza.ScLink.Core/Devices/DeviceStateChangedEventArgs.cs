using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Core.Devices;

/// <summary>Event arguments carrying the previous and current device state on a lifecycle state transition.</summary>
public sealed class DeviceStateChangedEventArgs : EventArgs
{
    /// <summary>State before the transition.</summary>
    public required DeviceState Previous { get; init; }

    /// <summary>State after the transition.</summary>
    public required DeviceState Current { get; init; }
}
