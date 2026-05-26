using System.Globalization;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moza.ScLink.App.Bus;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Diagnostics;
using Moza.ScLink.Core.Resolver;
using Moza.ScLink.Core.Safety;
using Moza.ScLink.Effects;
using Moza.ScLink.Effects.Catalogs;
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
    // E1 (T-27): pure extraction, registrations unchanged. The canonical device, ForceCommandPipeline,
    // LogSensor, FusionEngine, and IGameLogPathProvider registrations land in E2's atomic flip.
    internal static void ConfigureServices(IServiceCollection services)
    {
        // PRP §2.7 event pipeline + its drop-rate monitor. Registered here, but the generic host
        // is built-not-started (see Main: `using var host = ...Build()`, no StartAsync), so these
        // are DORMANT until host-start is wired — tracked in issue #43. Nothing consumes IEventBus
        // until T-11/T-12/T-16, so the inert registration is correct and forward-looking.
        services.AddSingleton<IEventBus, EventBus>();
        services.AddHostedService<DropRateMonitor>();

        // T-14 effect-resolution stage (PRP §5.7/§5.9). The EffectCatalog singleton is the
        // holding-location for the hot-reloading IDisposable loader (T-13 findings #1/#2/#4: the
        // container owns its FileSystemWatcher lifetime; a single registration avoids two-instance
        // duplication). Also DORMANT pending #43 — hosted services only instantiate on host-start,
        // which never happens here, so EffectCatalog.LoadDefault() does not run and T-13 #2/#3
        // (catalog-load-on-start + "Effects loaded: N") remain deferred to the convergence. The
        // live ResolverContext source (device caps, settings gains) also arrives with #43/T-16.
        services.AddSingleton(_ => EffectCatalog.LoadDefault());
        services.AddSingleton<IResolverContextProvider, DefaultResolverContextProvider>();
        services.AddSingleton<IEffectResolver, EffectResolver>();

        // T-15 safety limiter (PRP §5.8): the pure policy and its state-owning stage, which
        // EffectResolverService runs between the resolver and the ForceCommands channel. Also DORMANT
        // pending #43 — the stage holds no state until the hosted service drains the bus at host-start.
        services.AddSingleton<ISafetyLimiter, SafetyLimiter>();
        services.AddSingleton<SafetyLimiterStage>();

        services.AddHostedService<EffectResolverService>();

        // T-16 PR1 emergency-stop state authority (PRP §5.8). Shared singleton, consumed by:
        //   - ForceCommandPipeline, the output worker (deferred — see below);
        //   - PR2's global hotkey + WPF button/banner + telemetry (T-16 PR2 milestone).
        // The pipeline's AddHostedService registration is deferred until the canonical
        // Moza.ScLink.Core.Devices.IForceFeedbackDevice is DI-registered: today the App's
        // ForceFeedbackDeviceFactory.Create() returns the legacy Core.IForceFeedbackDevice via
        // LegacyForceFeedbackDeviceAdapter wrapping a VorticeDirectInputDevice, so no canonical-interface
        // registration exists to satisfy the pipeline's constructor. Registering it now would be the
        // first dormant service with an unsatisfiable dependency (host-start would throw). Wiring lands
        // with #43 (host-start) + #15 (transitional T-07 shim removal, incl. the legacy adapter that
        // currently down-converts the canonical device) — a convergence step no current task owns.
        services.AddSingleton<IEmergencyStop, EmergencyStop>();
    }

    private static string ComputeLogFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MozaStarCitizen", "logs", "app-.log");
}
