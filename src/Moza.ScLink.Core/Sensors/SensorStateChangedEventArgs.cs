using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Core.Sensors;

/// <summary>Event arguments carrying the previous and current state when a sensor's lifecycle state changes.</summary>
public sealed class SensorStateChangedEventArgs : EventArgs
{
    /// <summary>State before the transition.</summary>
    public required SensorState Previous { get; init; }

    /// <summary>State after the transition.</summary>
    public required SensorState Current { get; init; }
}
