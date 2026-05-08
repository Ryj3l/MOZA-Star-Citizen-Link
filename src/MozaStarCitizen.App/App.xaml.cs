using System.Windows;
using System.Windows.Threading;
using MozaStarCitizen.App.Diagnostics;

namespace MozaStarCitizen.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
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

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write(e.Exception, "Unhandled dispatcher exception");
        e.Handled = true;
    }
}
