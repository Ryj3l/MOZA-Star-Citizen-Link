namespace Moza.ScLink.App.GameLog;

/// <summary>
/// Resolves the Star Citizen Game.log path, owning the PRP §14.2-#3 orchestration
/// (load saved → auto-detect → saved-vs-detected-newer → persist) previously embedded in
/// <c>MainViewModel</c>. Delegates persistence to <c>AppSettingsStore</c> so the §14.2-#4
/// corrupted-settings recovery is preserved. Consumed at host startup (LogSensor registration) and
/// by the path UI (T-16 PR2).
/// </summary>
public interface IGameLogPathProvider
{
    /// <summary>
    /// Startup resolution: returns the saved path, or a newer auto-detected log (persisted), or a
    /// freshly detected log (persisted), or none. Tolerates a clean machine (returns
    /// <see cref="GameLogPathOrigin.None"/> with a null path) so host start never crashes.
    /// </summary>
    GameLogPathResolution ResolveAtStartup();

    /// <summary>
    /// Interactive auto-detect (the UI "Auto-detect" affordance): re-detects the latest readable
    /// Game.log, persists it when found, and returns it; returns <see cref="GameLogPathOrigin.None"/>
    /// when nothing is detected.
    /// </summary>
    GameLogPathResolution AutoDetect();
}

/// <summary>Origin of a resolved Game.log path, so the UI can render status without re-running policy.</summary>
public enum GameLogPathOrigin
{
    /// <summary>No saved path and nothing auto-detected.</summary>
    None,

    /// <summary>The previously saved path was used unchanged.</summary>
    Saved,

    /// <summary>A newer auto-detected log replaced the saved path (and was persisted).</summary>
    ReplacedWithNewer,

    /// <summary>No usable saved path; a freshly auto-detected log was used (and persisted).</summary>
    Detected,
}

/// <summary>Outcome of a Game.log path resolution.</summary>
public sealed record GameLogPathResolution(string? Path, GameLogPathOrigin Origin);
