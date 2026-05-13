namespace Moza.ScLink.Core.Sensors;

/// <summary>Snapshot of a sensor's operational health at a point in time.</summary>
/// <param name="IsHealthy">Whether the sensor is currently healthy.</param>
/// <param name="LastFault">Description of the most recent fault, or <see langword="null"/> if none.</param>
/// <param name="EventsEmitted">Total number of events emitted since the sensor started.</param>
/// <param name="DroppedEvents">Total number of events dropped due to slow consumers.</param>
/// <param name="LastEventAt">Time of the most recently emitted event, or <see langword="null"/> if none yet.</param>
public sealed record SensorHealth(
    bool IsHealthy,
    string? LastFault,
    long EventsEmitted,
    long DroppedEvents,
    DateTimeOffset? LastEventAt);
