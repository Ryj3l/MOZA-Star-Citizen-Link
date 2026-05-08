using System.IO;

namespace MozaStarCitizen.App.Log;

public static class StarCitizenLogLocator
{
    private static readonly string[] Channels = ["LIVE", "PTU", "EPTU", "TECH-PREVIEW"];

    public static string? FindGameLog()
    {
        var explicitPath = Environment.GetEnvironmentVariable("STAR_CITIZEN_GAME_LOG");
        if (IsReadableLog(explicitPath))
        {
            return explicitPath;
        }

        foreach (var candidate in BuildCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsReadableLog(candidate))
            {
                return candidate;
            }
        }

        return null;
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

    private static bool IsReadableLog(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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
}
