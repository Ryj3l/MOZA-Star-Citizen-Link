using System.Windows;
using System.Windows.Threading;
using MozaStarCitizen.App.Diagnostics;

namespace MozaStarCitizen.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
        {
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
