using System.Globalization;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moza.ScLink.App.Bus;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Diagnostics;
using Serilog;

namespace Moza.ScLink.App;

public static class Program
{
    private const string OutputTemplate =
        "{Timestamp:O} [pid:{ProcessId}] {Message:lj}{NewLine}{Exception}";

    [STAThread]
    public static int Main(string[] args)
    {
        var logPath = ComputeLogFilePath();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .WriteTo.File(logPath, outputTemplate: OutputTemplate,
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 52_428_800L,
                rollOnFileSizeLimit: true)
            .CreateBootstrapLogger();

        try
        {
            using var host = CreateHostBuilder(args).Build();
            AppLog.Logger = Log.Logger;

            var app = new App();
            return app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        var logPath = ComputeLogFilePath();
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.Sources.Clear();
                cfg.SetBasePath(AppContext.BaseDirectory);
                cfg.AddJsonFile("appsettings.json", optional: false);

                var profile = Environment.GetEnvironmentVariable("MOZA_SC_LOG_PROFILE")
                              ?? string.Empty;
                if (!string.IsNullOrEmpty(profile))
                {
                    cfg.AddJsonFile($"appsettings.{profile}.json", optional: true);
                }

                cfg.AddCommandLine(args);
            })
            .ConfigureServices((_, services) =>
            {
                // PRP §2.7 event pipeline + its drop-rate monitor. Registered here, but the generic host
                // is built-not-started (see Main: `using var host = ...Build()`, no StartAsync), so these
                // are DORMANT until host-start is wired — tracked in issue #43. Nothing consumes IEventBus
                // until T-11/T-12/T-16, so the inert registration is correct and forward-looking.
                services.AddSingleton<IEventBus, EventBus>();
                services.AddHostedService<DropRateMonitor>();
            })
            .UseSerilog((ctx, services, cfg) =>
            {
                cfg.ReadFrom.Configuration(ctx.Configuration)
                   .ReadFrom.Services(services)
                   .Enrich.WithProcessId()
                   .Enrich.WithThreadId()
                   .WriteTo.File(logPath, outputTemplate: OutputTemplate,
                       formatProvider: CultureInfo.InvariantCulture,
                       rollingInterval: RollingInterval.Day,
                       retainedFileCountLimit: 14,
                       fileSizeLimitBytes: 52_428_800L,
                       rollOnFileSizeLimit: true);
#if DEBUG
                cfg.WriteTo.Console(outputTemplate: OutputTemplate,
                    formatProvider: CultureInfo.InvariantCulture);
#endif
            });
    }

    private static string ComputeLogFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MozaStarCitizen", "logs", "app-.log");
}
