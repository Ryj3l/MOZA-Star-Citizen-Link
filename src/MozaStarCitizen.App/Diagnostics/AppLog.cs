using System.IO;

namespace MozaStarCitizen.App.Diagnostics;

public static class AppLog
{
    private static readonly object Sync = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MozaStarCitizen",
        "app.log");

    public static void Write(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (Sync)
            {
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    public static void Write(Exception exception, string context)
    {
        Write($"{context}: {exception}");
    }
}
