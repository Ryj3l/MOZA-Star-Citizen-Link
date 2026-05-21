using System.Text.Json.Serialization;

namespace Moza.ScLink.Logs.Parsing;

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

    /// <summary>When true, the pattern is loaded and compiled (regex validity) but never emits events —
    /// marks placeholder/unvalidated patterns honestly (issue #32). The no-emit behavior is enforced by
    /// T-11's PatternLibrary; this field only carries the flag.</summary>
    [JsonPropertyName("unsupported")]
    public bool Unsupported { get; set; }

    /// <summary>Advisory minimum Star Citizen build version (logged, not enforced in Phase 1).</summary>
    [JsonPropertyName("minStarCitizenBuildVersion")]
    public string? MinStarCitizenBuildVersion { get; set; }

    /// <summary>Advisory maximum Star Citizen build version (logged, not enforced in Phase 1).</summary>
    [JsonPropertyName("maxStarCitizenBuildVersion")]
    public string? MaxStarCitizenBuildVersion { get; set; }
}
