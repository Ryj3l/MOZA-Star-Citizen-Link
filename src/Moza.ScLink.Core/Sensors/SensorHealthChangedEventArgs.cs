namespace Moza.ScLink.Core.Sensors;

/// <summary>Event arguments carrying the previous and current health snapshot when a sensor's health changes.</summary>
public sealed class SensorHealthChangedEventArgs : EventArgs
{
    /// <summary>Health snapshot before the change.</summary>
    public required SensorHealth Previous { get; init; }

    /// <summary>Health snapshot after the change.</summary>
    public required SensorHealth Current { get; init; }
}
