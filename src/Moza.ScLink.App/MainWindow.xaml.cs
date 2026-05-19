using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Interop;
using Moza.ScLink.App.ViewModels;
using Moza.ScLink.Core.Diagnostics;

namespace Moza.ScLink.App;

[SuppressMessage(
    "Microsoft.Design",
    "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
    Justification = "WPF Window instances are not disposed by callers; the view model is disposed in OnClosed per WPF lifecycle.")]
public partial class MainWindow : Window
{
    // T-07 Issue #27 Pass-2 WM_DEVICECHANGE constants. Pass-2 stays permissive — handles ALL
    // DBT_DEVICEARRIVAL/REMOVECOMPLETE messages for this window (not just HID-class).
    // Post-probe filtering (e.g. DBT_DEVTYP) is a follow-up refinement based on F2a observed payload data.
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

    private readonly MainViewModel _viewModel = new();
    private HwndSource? _hwndSource;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        // T-07 Issue #27 Pass-2 hook installation. Ordering constraints:
        //   (a) HWND is valid at this point — WPF's Loaded event fires after window creation.
        //   (b) Hook installation precedes AutoStartAsync so any hot-plug event during the async
        //       startup window is captured (alternative — install AFTER AutoStartAsync — would
        //       lose events during the ~hundreds-of-ms init window).
        var helper = new WindowInteropHelper(this);
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(OnWindowMessage);

        await _viewModel.AutoStartAsync();
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_DEVICECHANGE) return IntPtr.Zero;
        var code = wParam.ToInt32();

        // S1-refinement: DeviceChangeProbe.LogMsg fires BEFORE the observer-null guard so the
        // probe captures every observed WM_DEVICECHANGE even if the observer chain is
        // misconfigured. The probe is gated inside DeviceChangeProbe (Enabled = env-var read at
        // type init); no-op when MOZA_SC_DEVICECHANGE_PROBE is unset.
        DeviceChangeProbe.LogMsg(code);

        var observer = _viewModel.DeviceAvailabilityObserver;
        if (observer is null) return IntPtr.Zero;

        if (code == DBT_DEVICEARRIVAL)             observer.OnDeviceArrived();
        else if (code == DBT_DEVICEREMOVECOMPLETE) observer.OnDeviceRemoved();

        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        // Hook teardown MUST precede _viewModel.Dispose(): the viewmodel's Dispose unsubscribes
        // ChainStateChanged and fires StopAsync(); leaving the hook live after the VM is gone
        // would race observer.OnDeviceArrived/Removed calls against a disposed VM.
        _hwndSource?.RemoveHook(OnWindowMessage);
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
