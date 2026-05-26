using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moza.ScLink.Core.Diagnostics;

namespace Moza.ScLink.App;

[SuppressMessage(
    "Microsoft.Design",
    "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
    Justification = "WPF Application instances are not disposed by callers; mutex is released and disposed in OnExit per WPF lifecycle. The host is owned (disposed) by Program.Main's using statement.")]
public partial class App : System.Windows.Application
{
    private readonly IHost _host;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    public App(IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // §14.2-#6 single-instance mutex — checked BEFORE the host starts, so a second instance exits
        // without spinning up the pipeline or contending for the device.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Global\\MozaStarCitizenLink", out var ownsMutex);
        _ownsSingleInstanceMutex = ownsMutex;
        if (!ownsMutex)
        {
            AppLog.Write("Another MozaStarCitizen instance is already running. Exiting this instance.");
            System.Windows.MessageBox.Show(
                "MOZA Star Citizen Link is already running. Close the existing window before starting another copy.",
                "MOZA Star Citizen Link",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppLog.Write("Application starting.");
        AppLog.Write($"Executable base directory: {AppContext.BaseDirectory}");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                AppLog.Write(ex, "Unhandled AppDomain exception");
            }
            else
            {
                AppLog.Write($"Unhandled AppDomain exception: {args.ExceptionObject}");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Write(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        base.OnStartup(e);

        // T-27 Fork-1 ordering: resolve + Show the main window from the host's service provider BEFORE
        // host.Start(). The window's HWND must be valid when DeviceInitializer (a hosted service) calls
        // VorticeDirectInputDevice.InitializeAsync, which passes Application.Current.MainWindow's handle
        // to SetCooperativeLevel(Exclusive|Background). The service provider is available post-Build, so
        // resolving before Start is safe; all of MainWindow's dependencies are host-state-free singletons.
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        _host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Only the instance that owns the mutex started the host. Stop it (drains the hosted pipeline and
        // disposes the canonical device) before WPF tears down; Program.Main's `using` disposes the host.
        if (_ownsSingleInstanceMutex)
        {
            try
            {
                _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "Host stop on exit failed");
            }

            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        _ownsSingleInstanceMutex = false;
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write(e.Exception, "Unhandled dispatcher exception");
        e.Handled = true;
    }
}
