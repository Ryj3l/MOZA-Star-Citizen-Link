using System.Text.Json.Serialization;

namespace MozaStarCitizen.App.Parsing;

public sealed class EventPattern
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;

    [JsonPropertyName("intensity")]
    public double Intensity { get; set; }

    [JsonPropertyName("durationMs")]
    public int DurationMs { get; set; }
}
