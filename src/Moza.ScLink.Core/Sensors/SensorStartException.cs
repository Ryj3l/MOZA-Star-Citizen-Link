namespace Moza.ScLink.Core.Sensors;

/// <summary>Thrown by <see cref="ISensor.StartAsync"/> when a sensor fails to start terminally.</summary>
public sealed class SensorStartException : Exception
{
    /// <summary>Initializes a new <see cref="SensorStartException"/> with the sensor ID and a message.</summary>
    public SensorStartException(string sensorId, string message)
        : base(message)
    {
        SensorId = sensorId;
    }

    /// <summary>Initializes a new <see cref="SensorStartException"/> with the sensor ID, message, and inner exception.</summary>
    public SensorStartException(string sensorId, string message, Exception innerException)
        : base(message, innerException)
    {
        SensorId = sensorId;
    }

    /// <summary>The ID of the sensor that failed to start.</summary>
    public string SensorId { get; }
}
