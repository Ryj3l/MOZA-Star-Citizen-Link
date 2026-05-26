using System.Globalization;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moza.ScLink.App.Bus;
using Moza.ScLink.App.ForceFeedback;
using Moza.ScLink.App.GameLog;
using Moza.ScLink.App.Hosting;
using Moza.ScLink.App.ViewModels;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Diagnostics;
using Moza.ScLink.Core.Resolver;
using Moza.ScLink.Core.Safety;
using Moza.ScLink.Effects;
using Moza.ScLink.Effects.Catalogs;
using Moza.ScLink.Fusion;
using Moza.ScLink.Fusion.Rules;
using Moza.ScLink.Logs;
using Moza.ScLink.Logs.Parsing;
using Moza.ScLink.Profiles.Settings;
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

            // T-27: the host is now started by App.OnStartup (after the main window is shown) and stopped
            // by App.OnExit; this `using` owns its disposal once Run() returns.
            var app = new App(host);
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
            .ConfigureServices((_, services) => ConfigureServices(services))
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

    // Extracted from the ConfigureServices lambda so the real service graph is composable by the
    // integration tests (App's AssemblyInfo grants InternalsVisibleTo to Moza.ScLink.App.Tests).
    // T-27 E2 (the atomic flip): the generic host is now STARTED by App.OnStartup, so the hosted
    // services below run end-to-end (sensor → fusion → resolver → safety → output worker → device).
    internal static void ConfigureServices(IServiceCollection services)
    {
        // PRP §2.7 event pipeline + its drop-rate monitor. The host is started at App.OnStartup, so the
        // bus carries live evidence from the LogSensor (and the synthetic Test-button injections).
        services.AddSingleton<IEventBus, EventBus>();
        services.AddHostedService<DropRateMonitor>();

        // T-14 effect-resolution stage (PRP §5.7/§5.9). The EffectCatalog singleton is the
        // holding-location for the hot-reloading IDisposable loader (T-13 findings #1/#2/#4: the
        // container owns its FileSystemWatcher lifetime; a single registration avoids two-instance
        // duplication). EffectCatalog.LoadDefault() runs at host-start now.
        services.AddSingleton(_ => EffectCatalog.LoadDefault());
        services.AddSingleton<IResolverContextProvider, DefaultResolverContextProvider>();
        services.AddSingleton<IEffectResolver, EffectResolver>();

        // T-15 safety limiter (PRP §5.8): the pure policy and its state-owning stage, which
        // EffectResolverService runs between the resolver and the ForceCommands channel.
        services.AddSingleton<ISafetyLimiter, SafetyLimiter>();
        services.AddSingleton<SafetyLimiterStage>();

        services.AddHostedService<EffectResolverService>();

        // T-16 PR1 emergency-stop state authority (PRP §5.8). Shared singleton, consumed by the
        // ForceCommandPipeline output worker (registered below) and, later, PR2's hotkey + UI.
        services.AddSingleton<IEmergencyStop, EmergencyStop>();

        // ── T-27 convergence: canonical device, log sensor, fusion, and the output worker ──────────
        // Hot-reloading IDisposable libraries (container-owned lifetime, like EffectCatalog).
        services.AddSingleton(_ => PatternLibrary.LoadDefault());
        services.AddSingleton(_ => RuleLibrary.LoadDefault());

        // Game.log path policy (§14.2-#3/#4 orchestration) — composes Logs + Profiles in the App layer.
        services.AddSingleton<AppSettingsStore>();
        services.AddSingleton<IGameLogPathProvider, GameLogPathProvider>();

        // Canonical force-feedback device: unwrapped VorticeDirectInputDevice if an allowlisted device
        // enumerates, else the LoggingNullForceFeedbackDevice (no-hardware preview). DeviceInitializer
        // calls InitializeAsync at host-start (after the main window is shown — see App.OnStartup).
        services.AddSingleton<IForceFeedbackDevice>(_ => ForceFeedbackDeviceFactory.CreateCanonical());

        // The Game.log sensor (T-11). Its path is resolved once at startup via the provider; an empty
        // path (clean machine) is tolerated — the underlying tailer idles until the file appears.
        services.AddSingleton(sp => new LogSensor(
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<PatternLibrary>(),
            sp.GetRequiredService<IGameLogPathProvider>().ResolveAtStartup().Path ?? string.Empty));

        // Hosted services. DeviceInitializer is a plain IHostedService registered BEFORE the pipeline:
        // its awaited StartAsync leaves the device Ready before the pipeline BackgroundService's
        // ExecuteAsync loop runs (which would otherwise throw on an uninitialized device).
        services.AddHostedService<DeviceInitializer>();
        services.AddHostedService<FusionEngine>();
        services.AddHostedService<ForceCommandPipeline>();
        services.AddHostedService<LogSensorHostService>();

        // WPF composition: the host owns MainWindow + its view model so App.OnStartup resolves them.
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private static string ComputeLogFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MozaStarCitizen", "logs", "app-.log");
}
