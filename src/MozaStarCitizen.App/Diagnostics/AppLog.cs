using System.IO;

namespace MozaStarCitizen.App.Diagnostics;

public static class AppLog
{
    private static readonly object Sync = new();
    private static readonly int ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;

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
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} [pid:{ProcessId}] {message}{Environment.NewLine}");
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
