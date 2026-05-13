using System.Text.Json.Serialization;

namespace Moza.ScLink.Profiles.Settings;

public sealed class AppSettings
{
    [JsonPropertyName("gameLogPath")]
    public string? GameLogPath { get; set; }
}
