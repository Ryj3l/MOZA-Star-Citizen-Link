using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using SharpGen.Runtime;
using Vortice.DirectInput;

namespace Moza.ScLink.DiAcquireProbe;

/// <summary>
/// Probe window: enumerates DirectInput game controllers, exclusive-acquires a selected device,
/// optionally sends <see cref="ForceFeedbackCommand.Reset"/>, and releases — logging every step
/// with millisecond timestamps and HRESULTs so the run correlates against the Moza.ScLink.App
/// diagnostic log (issue #83 persistence test; T-23 Section E2).
/// </summary>
/// <remarks>
/// API shapes (DInput.DirectInput8Create, GetDevices, SetDataFormat&lt;RawJoystickState&gt;,
/// SharpGenException.ResultCode.Code) mirror the production adapters in Moza.ScLink.DirectInput,
/// where they were empirically verified against the installed Vortice.DirectInput 3.6.2.
/// </remarks>
public sealed partial class MainWindow : Window, IDisposable
{
    // Mirrors the MOZA model patterns in src/device-allowlist.json (matchMode
    // containsAnyCaseInsensitive). Display-highlight only — the probe drives no force on any
    // device, so allowlist enforcement is not implicated; the highlight just makes the test
    // target unmistakable in the list.
    private static readonly string[] _targetPatterns =
        ["MOZA AB9", "AB9 FFB", "AB9", "MOZA AB6", "AB6 FFB", "AB6"];

    private readonly IDirectInput8 _di8;
    private IDirectInputDevice8? _device;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
        _di8 = DInput.DirectInput8Create();
        Loaded += (_, _) =>
        {
            LogLine("Probe started. Select the AB9 and press Acquire while a sustained effect plays.");
            RefreshDevices();
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseDevice(log: false);
        _di8.Dispose();
        _disposed = true;
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Dispose();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Focus transitions matter forensically in Foreground cooperative mode: DirectInput
    /// auto-unacquires a Foreground-mode device when its window deactivates, so these lines
    /// timestamp every instant the probe's hold could have lapsed or resumed.
    /// </remarks>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_device is not null)
        {
            LogLine("Window ACTIVATED (foreground-mode hold can resume on next Acquire).");
        }
    }

    /// <inheritdoc />
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_device is not null)
        {
            LogLine("Window DEACTIVATED (foreground-mode hold lapses here; background-mode hold persists).");
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshDevices();

    private void OnDeviceSelectionChanged(object sender, RoutedEventArgs e) => UpdateButtonStates();

    private void OnAcquireClick(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not DeviceItem item)
        {
            LogLine("No device selected.");
            return;
        }

        // Re-acquire on an existing session releases the prior device first so each attempt
        // starts from a clean state.
        ReleaseDevice(log: true);

        var level = ForegroundMode.IsChecked == true
            ? CooperativeLevel.Exclusive | CooperativeLevel.Foreground
            : CooperativeLevel.Exclusive | CooperativeLevel.Background;

        try
        {
            _device = _di8.CreateDevice(item.InstanceGuid);
            _device.SetDataFormat<RawJoystickState>();
            _device.SetCooperativeLevel(new WindowInteropHelper(this).Handle, level);

            // Foreground cooperative level only grants acquisition to the foreground window;
            // make sure that is us at the Acquire instant.
            Activate();
            _device.Acquire();
            LogLine($"ACQUIRED '{item.ProductName}' ({level}).");
        }
        catch (SharpGenException ex)
        {
            LogLine($"ACQUIRE FAILED for '{item.ProductName}' ({level}): HRESULT 0x{ex.ResultCode.Code:X8} — {ex.Message}");
            ReleaseDevice(log: false);
        }

        UpdateButtonStates();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            _device.SendForceFeedbackCommand(ForceFeedbackCommand.Reset);
            LogLine("FFB RESET sent (halt-type command; stops and removes all device effects).");
        }
        catch (SharpGenException ex)
        {
            LogLine($"FFB RESET FAILED: HRESULT 0x{ex.ResultCode.Code:X8} — {ex.Message}");
        }
    }

    private void OnReleaseClick(object sender, RoutedEventArgs e)
    {
        ReleaseDevice(log: true);
        UpdateButtonStates();
    }

    private void RefreshDevices()
    {
        try
        {
            var instances = _di8.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
            var items = new List<DeviceItem>(instances.Count);
            foreach (var instance in instances)
            {
                items.Add(new DeviceItem(instance.InstanceGuid, instance.ProductName, IsTargetDevice(instance.ProductName)));
            }

            DeviceList.ItemsSource = items;
            LogLine($"Enumerated {items.Count} attached game controller(s).");
        }
        catch (SharpGenException ex)
        {
            LogLine($"ENUMERATION FAILED: HRESULT 0x{ex.ResultCode.Code:X8} — {ex.Message}");
        }

        UpdateButtonStates();
    }

    private void ReleaseDevice(bool log)
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            _device.Unacquire();
            if (log)
            {
                LogLine("RELEASED (Unacquire + Dispose).");
            }
        }
        catch (SharpGenException ex)
        {
            if (log)
            {
                LogLine($"UNACQUIRE FAILED (disposing anyway): HRESULT 0x{ex.ResultCode.Code:X8} — {ex.Message}");
            }
        }

        _device.Dispose();
        _device = null;
    }

    private void UpdateButtonStates()
    {
        AcquireButton.IsEnabled = DeviceList.SelectedItem is not null;
        ResetButton.IsEnabled = _device is not null;
        ReleaseButton.IsEnabled = _device is not null;
    }

    private void LogLine(string message)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        StatusLog.AppendText($"{stamp}  {message}{Environment.NewLine}");
        StatusLog.ScrollToEnd();
    }

    private static bool IsTargetDevice(string productName)
    {
        foreach (var pattern in _targetPatterns)
        {
            if (productName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>List entry for an enumerated DirectInput device.</summary>
/// <param name="InstanceGuid">DirectInput instance GUID (the <c>CreateDevice</c> argument).</param>
/// <param name="ProductName">Device product name as reported by DirectInput.</param>
/// <param name="IsTarget">Matches a MOZA flight-base pattern from <c>src/device-allowlist.json</c>.</param>
public sealed record DeviceItem(Guid InstanceGuid, string ProductName, bool IsTarget)
{
    /// <summary>Display string for the device list.</summary>
    public string Display => $"{ProductName} — {InstanceGuid}";
}
