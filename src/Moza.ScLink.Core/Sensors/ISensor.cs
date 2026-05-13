using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Core.Sensors;

/// <summary>Contract for all sensor implementations in the fusion pipeline.</summary>
public interface ISensor : IAsyncDisposable
{
    /// <summary>Unique identifier for this sensor instance, e.g. "audio.endpoint-loopback".</summary>
    string SensorId { get; }

    /// <summary>The kind of signal this sensor observes.</summary>
    SensorKind Kind { get; }

    /// <summary>Current health snapshot.</summary>
    SensorHealth Health { get; }

    /// <summary>Current lifecycle state.</summary>
    SensorState State { get; }

    /// <summary>Raised when the sensor's health changes.</summary>
    event EventHandler<SensorHealthChangedEventArgs>? HealthChanged;

    /// <summary>Raised when the sensor's lifecycle state changes.</summary>
    event EventHandler<SensorStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Starts the sensor. Idempotent: calling Start on an already-started sensor is a no-op.
    /// Throws <see cref="SensorStartException"/> if start fails terminally.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the sensor. Idempotent. Must complete within 5 seconds or throw <see cref="TimeoutException"/>.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Yields <see cref="SensorEvent"/>s as they are produced. Multiple calls produce independent enumerations.
    /// Events are dropped (counted in <see cref="SensorHealth.DroppedEvents"/>) if a consumer falls behind by more than 100 events.
    /// </summary>
    IAsyncEnumerable<SensorEvent> ReadEventsAsync(CancellationToken cancellationToken);
}
