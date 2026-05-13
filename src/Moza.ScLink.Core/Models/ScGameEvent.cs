namespace Moza.ScLink.Core.Models;

/// <summary>Legacy pre-migration game event record. Superseded by <see cref="Moza.ScLink.Core.Events.GameEvent"/> in T-06.</summary>
public sealed record ScGameEvent(
    ScEventKind Kind,
    string Name,
    double Intensity,
    TimeSpan Duration,
    string SourceLine,
    DateTimeOffset Timestamp);
