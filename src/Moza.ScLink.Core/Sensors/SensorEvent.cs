using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Core.Sensors;

/// <summary>Immutable evidence event produced by a sensor and consumed by the fusion engine.</summary>
public sealed record SensorEvent
{
    /// <summary>Unique identifier for this event instance (GUID).</summary>
    public required string EventId { get; init; }

    /// <summary>Identifier of the sensor that produced this event, e.g. "audio.endpoint-loopback".</summary>
    public required string SensorId { get; init; }

    /// <summary>The kind of sensor that produced this event.</summary>
    public required SensorKind SensorKind { get; init; }

    /// <summary>Sensor-local event type string, e.g. "audio.weapon_fire_ballistic".</summary>
    public required string EventType { get; init; }

    /// <summary>Time at which the sensor observed this event.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Sensor's confidence in its classification, in [0.0, 1.0].</summary>
    public double Confidence { get; init; }

    /// <summary>Relative intensity of the observed signal, in [0.0, 1.0].</summary>
    public double Intensity { get; init; }

    /// <summary>Duration of the observation; <see langword="null"/> for transient events.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Named numeric features explaining the classification decision. Reference equality (ImmutableDictionary).</summary>
    public ImmutableDictionary<string, double> Features { get; init; }
        = ImmutableDictionary<string, double>.Empty;

    /// <summary>Arbitrary string metadata for diagnostics and extensibility. Reference equality (ImmutableDictionary).</summary>
    public ImmutableDictionary<string, string> Metadata { get; init; }
        = ImmutableDictionary<string, string>.Empty;
}
