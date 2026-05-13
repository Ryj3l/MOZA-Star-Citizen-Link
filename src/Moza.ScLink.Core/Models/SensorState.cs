namespace Moza.ScLink.Core.Models;

/// <summary>Lifecycle state of an <see cref="Moza.ScLink.Core.Sensors.ISensor"/> implementation.</summary>
public enum SensorState
{
    /// <summary>Sensor is not running.</summary>
    Stopped,
    /// <summary>Sensor is in the process of starting.</summary>
    Starting,
    /// <summary>Sensor is running and emitting events.</summary>
    Running,
    /// <summary>Sensor has encountered a terminal fault.</summary>
    Faulted,
    /// <summary>Sensor is in the process of stopping.</summary>
    Stopping,
}
