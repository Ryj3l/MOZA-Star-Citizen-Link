using System.Text.Json.Serialization;

namespace Moza.ScLink.Profiles.Settings;

public sealed class AppSettings
{
    [JsonPropertyName("gameLogPath")]
    public string? GameLogPath { get; set; }

    /// <summary>
    /// When <c>true</c>, the app forces preview mode at startup even if an allowlisted MOZA base is
    /// present — no force commands reach hardware; the <c>PreviewForceFeedbackDevice</c> logs and
    /// streams what would have been sent (T-17). Composes with the <c>MOZA_SC_OUTPUT=Preview</c> dev
    /// env var: either one forces preview. User-facing affordance, persisted across launches; applied
    /// once at startup (no live device hot-swap — see T-27 / issue #27).
    /// </summary>
    [JsonPropertyName("forcePreviewMode")]
    public bool ForcePreviewMode { get; set; }
}
