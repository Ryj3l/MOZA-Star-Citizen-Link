using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Core.Events;

/// <summary>Canonical game event emitted by the fusion engine after sensor evidence is aggregated and deduplicated.</summary>
public sealed record GameEvent
{
    /// <summary>Unique identifier for this event instance (GUID).</summary>
    public required string EventId { get; init; }

    /// <summary>Canonical event type.</summary>
    public required GameEventType EventType { get; init; }

    /// <summary>Time at which the fusion engine emitted this event.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Fusion confidence in [0.0, 1.0].</summary>
    public double Confidence { get; init; }

    /// <summary>Relative intensity of the event in [0.0, 1.0].</summary>
    public double Intensity { get; init; }

    /// <summary>Duration of the event; <see langword="null"/> for instantaneous events.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Sensor IDs that contributed evidence to this event. Structural equality via ImmutableArray.</summary>
    public ImmutableArray<string> Sources { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Human-readable codes explaining the fusion decision. Structural equality via ImmutableArray.</summary>
    public ImmutableArray<string> ReasonCodes { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Numeric evidence features keyed by feature name. Reference equality (ImmutableDictionary).</summary>
    public ImmutableDictionary<string, double> Evidence { get; init; }
        = ImmutableDictionary<string, double>.Empty;

    /// <summary>Arbitrary string metadata for diagnostics and extensibility. Reference equality (ImmutableDictionary).</summary>
    public ImmutableDictionary<string, string> Metadata { get; init; }
        = ImmutableDictionary<string, string>.Empty;
}
