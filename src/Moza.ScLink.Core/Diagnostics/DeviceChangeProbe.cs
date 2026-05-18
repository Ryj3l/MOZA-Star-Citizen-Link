namespace Moza.ScLink.Core.Diagnostics;

/// <summary>
/// T-07 Issue #27 Pass-2 F2a diagnostic harness for the WM_DEVICECHANGE →
/// IDeviceAvailabilityObserver pipeline. Gates two log markers on the
/// <c>MOZA_SC_DEVICECHANGE_PROBE</c> env var, read ONCE at type init (= single source of truth;
/// two-sources-of-truth structurally impossible because the read happens in a single
/// <c>static readonly</c> initializer).
/// </summary>
/// <remarks>
/// DELETE this file when F2a is confirmed on AB9 hardware and the probe is no longer needed.
/// The probe has zero production object-graph tendrils: it is consumed via two unconditional
/// static calls (one in MainWindow's WM_DEVICECHANGE hook, two in the Fallback chain's observer
/// methods), each of which short-circuits internally when <see cref="Enabled"/> is false.
/// </remarks>
public static class DeviceChangeProbe
{
    /// <summary>True iff <c>MOZA_SC_DEVICECHANGE_PROBE=1</c> at process start.</summary>
    public static readonly bool Enabled =
        string.Equals(
            Environment.GetEnvironmentVariable("MOZA_SC_DEVICECHANGE_PROBE"),
            "1",
            StringComparison.Ordinal);

    /// <summary>
    /// Logged at <c>MainWindow.OnWindowMessage</c> for each observed WM_DEVICECHANGE, BEFORE the
    /// arrival/removal classification. Proves the OS notification reached the window AND the hook
    /// is installed; absence indicates F2a hardware failure OR a missing hook.
    /// </summary>
    public static void LogMsg(int eventCode)
    {
        if (!Enabled) return;
        AppLog.Write($"[device-change-probe-msg] eventCode=0x{eventCode:X8}");
    }

    /// <summary>
    /// Logged at <c>FallbackForceFeedbackDevice</c>'s <c>OnDeviceArrived</c> / <c>OnDeviceRemoved</c>
    /// for each observer-method entry. Proves the observer-call wiring (MainWindow → controller
    /// passthrough → Fallback) is intact; absence with present <c>LogMsg</c> markers indicates a
    /// classification or passthrough bug.
    /// </summary>
    public static void LogObserver(string entry)
    {
        if (!Enabled) return;
        AppLog.Write($"[device-change-probe-observer] {entry}");
    }
}
