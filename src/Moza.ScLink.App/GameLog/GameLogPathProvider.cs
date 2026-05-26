using System.IO;
using Moza.ScLink.Logs;
using Moza.ScLink.Profiles.Settings;

namespace Moza.ScLink.App.GameLog;

/// <summary>
/// Default <see cref="IGameLogPathProvider"/>. Composes <see cref="StarCitizenLogLocator"/> (detection)
/// with <see cref="AppSettingsStore"/> (saved-path persistence + §14.2-#4 corrupted-file recovery).
/// Lives in the App composition layer because it bridges two sibling projects (Logs + Profiles) that do
/// not — and should not — reference each other. The orchestration is synchronous: detection, file
/// checks, and settings load/save have no async work (the former <c>MainViewModel</c> handlers were
/// async only to tail-call the UI's auto-start).
/// </summary>
public sealed class GameLogPathProvider : IGameLogPathProvider
{
    private const int NewerLogThresholdSeconds = 5;

    private readonly AppSettingsStore _settingsStore;
    private readonly Func<string?> _detectGameLog;

    /// <summary>Production constructor: detects via <see cref="StarCitizenLogLocator.FindGameLog"/>.</summary>
    public GameLogPathProvider(AppSettingsStore settingsStore)
        : this(settingsStore, StarCitizenLogLocator.FindGameLog)
    {
    }

    // Test seam (internal + InternalsVisibleTo): injects the detection function so the resolution
    // policy — including the "nothing detected" (None) branch — is exercised hermetically, without
    // depending on whether Star Citizen is installed on the test machine (FindGameLog scans the real
    // filesystem). A delegate, not an interface abstraction — symmetric with the AppSettingsStore
    // test-path ctor.
    internal GameLogPathProvider(AppSettingsStore settingsStore, Func<string?> detectGameLog)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(detectGameLog);
        _settingsStore = settingsStore;
        _detectGameLog = detectGameLog;
    }

    /// <inheritdoc />
    public GameLogPathResolution ResolveAtStartup()
    {
        var settings = _settingsStore.Load();   // §14.2-#4 corrupted-file recovery lives in Load()
        var detected = _detectGameLog();
        var saved = settings.GameLogPath;

        if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
        {
            if (!string.IsNullOrWhiteSpace(detected) &&
                !PathsEqual(saved, detected) &&
                IsNewerLog(detected, saved))
            {
                _settingsStore.Save(new AppSettings { GameLogPath = detected });
                return new GameLogPathResolution(detected, GameLogPathOrigin.ReplacedWithNewer);
            }

            return new GameLogPathResolution(saved, GameLogPathOrigin.Saved);
        }

        if (!string.IsNullOrWhiteSpace(detected))
        {
            _settingsStore.Save(new AppSettings { GameLogPath = detected });
            return new GameLogPathResolution(detected, GameLogPathOrigin.Detected);
        }

        return new GameLogPathResolution(null, GameLogPathOrigin.None);
    }

    /// <inheritdoc />
    public GameLogPathResolution AutoDetect()
    {
        var detected = _detectGameLog();
        if (string.IsNullOrWhiteSpace(detected))
        {
            return new GameLogPathResolution(null, GameLogPathOrigin.None);
        }

        _settingsStore.Save(new AppSettings { GameLogPath = detected });
        return new GameLogPathResolution(detected, GameLogPathOrigin.Detected);
    }

    // Extracted verbatim from MainViewModel.PathsEqual (T-27 §14.2-#3 relocation).
    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Extracted verbatim from MainViewModel.IsNewerLog (T-27 §14.2-#3 relocation): a candidate is
    // "newer" only if it is more than 5 s ahead of the current log, to avoid churn on near-simultaneous writes.
    private static bool IsNewerLog(string candidatePath, string currentPath)
    {
        try
        {
            var candidate = new FileInfo(candidatePath);
            var current = new FileInfo(currentPath);
            return candidate.LastWriteTimeUtc > current.LastWriteTimeUtc.AddSeconds(NewerLogThresholdSeconds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
