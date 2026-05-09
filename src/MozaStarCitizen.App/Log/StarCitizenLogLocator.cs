using System.IO;
using MozaStarCitizen.App.Diagnostics;

namespace MozaStarCitizen.App.Log;

public static class StarCitizenLogLocator
{
    private static readonly string[] Channels = ["LIVE", "PTU", "EPTU", "TECH-PREVIEW"];

    public static string? FindGameLog()
    {
        var explicitPath = Environment.GetEnvironmentVariable("STAR_CITIZEN_GAME_LOG");
        if (TryGetReadableLog(explicitPath, out var explicitLog))
        {
            AppLog.Write($"Using STAR_CITIZEN_GAME_LOG: {Describe(explicitLog)}");
            return explicitLog.Path;
        }

        var candidates = new List<CandidateGameLog>();
        foreach (var candidate in BuildCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryGetReadableLog(candidate, out var log))
            {
                candidates.Add(log);
            }
        }

        var latest = candidates
            .OrderByDescending(candidate => candidate.LastWriteTimeUtc)
            .FirstOrDefault();

        if (latest is null)
        {
            return null;
        }

        AppLog.Write($"Auto-detected latest readable Game.log: {Describe(latest)}");
        return latest.Path;
    }

    private static IEnumerable<string> BuildCandidates()
    {
        var roots = new List<string>();
        AddIfPresent(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddIfPresent(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            AddIfPresent(roots, drive.RootDirectory.FullName);
            AddIfPresent(roots, Path.Combine(drive.RootDirectory.FullName, "Program Files"));
            AddIfPresent(roots, Path.Combine(drive.RootDirectory.FullName, "Games"));
            AddIfPresent(roots, Path.Combine(drive.RootDirectory.FullName, "Roberts Space Industries"));
        }

        foreach (var root in roots)
        {
            foreach (var channel in Channels)
            {
                yield return Path.Combine(root, "Roberts Space Industries", "StarCitizen", channel, "Game.log");
                yield return Path.Combine(root, "StarCitizen", channel, "Game.log");
            }
        }
    }

    private static void AddIfPresent(List<string> roots, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            roots.Add(path);
        }
    }

    private static bool TryGetReadableLog(string? path, out CandidateGameLog log)
    {
        log = CandidateGameLog.Empty;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var fileInfo = new FileInfo(fullPath);
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            log = new CandidateGameLog(fullPath, fileInfo.LastWriteTimeUtc, fileInfo.Length);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Describe(CandidateGameLog log) =>
        $"{log.Path}. Length: {log.Length} bytes. Last write UTC: {log.LastWriteTimeUtc:O}";

    private sealed record CandidateGameLog(string Path, DateTime LastWriteTimeUtc, long Length)
    {
        public static CandidateGameLog Empty { get; } = new(string.Empty, DateTime.MinValue, 0);
    }
}
