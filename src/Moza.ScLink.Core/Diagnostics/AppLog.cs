using Serilog;

namespace Moza.ScLink.Core.Diagnostics;

/// <summary>Static application-wide logger shim. Replaced by Serilog after the generic host is built.</summary>
public static class AppLog
{
    // Pre-host no-op: replaced by Program.Main after IHostBuilder.Build()
    /// <summary>The active Serilog logger instance. Replaced by the fully-configured logger after host build.</summary>
    public static ILogger Logger { get; internal set; } = new LoggerConfiguration().CreateLogger();

    /// <summary>Returns the path to today's rolling log file under %LOCALAPPDATA%.</summary>
    public static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MozaStarCitizen", "logs",
            $"app-{DateTime.Now:yyyyMMdd}.log");

    /// <summary>Logs an informational message.</summary>
    public static void Write(string message) =>
        Logger.Information("{Message}", message);

    /// <summary>Logs an error with the given exception and context label.</summary>
    public static void Write(Exception exception, string context) =>
        Logger.Error(exception, "{Context}", context);
}
