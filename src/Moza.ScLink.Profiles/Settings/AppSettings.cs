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

    /// <summary>
    /// The global hotkey that activates emergency stop, in human-readable form (default
    /// <c>"Ctrl+Alt+F12"</c>): modifiers (Ctrl/Control, Alt, Shift, Win/Windows) and exactly one key
    /// (F1–F24, a letter, or a digit) joined by <c>+</c>. Parsed by <c>HotkeyCombination.TryParse</c>
    /// (T-16 PR2); a missing, blank, or unparseable value falls back to the default. Read once at
    /// startup and registered once — no in-app rebind UI (hand-edit settings.json to change).
    /// Persisted across launches; participates in the merge-safe <c>AppSettingsStore.Update</c> path.
    /// </summary>
    [JsonPropertyName("emergencyStopHotkey")]
    public string EmergencyStopHotkey { get; set; } = "Ctrl+Alt+F12";
}
