using Serilog;

namespace Moza.ScLink.Core.Diagnostics;

public static class AppLog
{
    // Pre-host no-op: replaced by Program.Main after IHostBuilder.Build()
    public static ILogger Logger { get; internal set; } = new LoggerConfiguration().CreateLogger();

    public static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MozaStarCitizen", "logs",
            $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void Write(string message) =>
        Logger.Information("{Message}", message);

    public static void Write(Exception exception, string context) =>
        Logger.Error(exception, "{Context}", context);
}
