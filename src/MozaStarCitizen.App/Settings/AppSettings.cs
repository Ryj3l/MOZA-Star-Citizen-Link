using System.Text.Json.Serialization;

namespace MozaStarCitizen.App.Settings;

public sealed class AppSettings
{
    [JsonPropertyName("gameLogPath")]
    public string? GameLogPath { get; set; }
}
