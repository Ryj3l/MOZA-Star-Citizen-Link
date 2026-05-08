namespace MozaStarCitizen.App.Models;

public sealed record ScGameEvent(
    ScEventKind Kind,
    string Name,
    double Intensity,
    TimeSpan Duration,
    string SourceLine,
    DateTimeOffset Timestamp);
