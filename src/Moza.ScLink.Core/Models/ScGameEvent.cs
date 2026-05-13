namespace Moza.ScLink.Core.Models;

public sealed record ScGameEvent(
    ScEventKind Kind,
    string Name,
    double Intensity,
    TimeSpan Duration,
    string SourceLine,
    DateTimeOffset Timestamp);
