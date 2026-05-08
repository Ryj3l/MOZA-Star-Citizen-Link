using System.IO;
using MozaStarCitizen.App.ForceFeedback.DirectInput;

namespace MozaStarCitizen.App.ForceFeedback;

public static class ForceFeedbackDiagnostics
{
    public static IReadOnlyList<string> GetLines(
        IForceFeedbackDevice selectedDevice,
        bool includeExtendedDiagnostics)
    {
        var lines = new List<string>
        {
            $"Output mode: {Environment.GetEnvironmentVariable("MOZA_SC_OUTPUT") ?? "Auto"}",
            $"Selected output: {selectedDevice.Name}",
            $"Output status: {selectedDevice.Status}"
        };

        var bridgePath = Path.Combine(AppContext.BaseDirectory, "drivers", "MozaForceBridge.dll");
        var managedSdkPath = Path.Combine(AppContext.BaseDirectory, "drivers", "moza-sdk", "x64", "MOZA_API_CSharp.dll");
        lines.Add(File.Exists(managedSdkPath)
            ? $"MOZA C# SDK: found at {managedSdkPath}"
            : $"MOZA C# SDK: not found at {managedSdkPath}");
        lines.Add("MOZA SDK product query: disabled because this racing SDK can terminate the process when probing unsupported devices.");
        lines.Add(File.Exists(bridgePath)
            ? $"MOZA SDK bridge: found at {bridgePath}"
            : $"MOZA SDK bridge: not found at {bridgePath}");

        if (!includeExtendedDiagnostics)
        {
            lines.Add("Press Refresh to probe MOZA SDK devices and DirectInput controllers.");
            return lines;
        }

        try
        {
            var controllers = DirectInputNative.EnumerateGameControllers();
            var forceFeedbackDevices = DirectInputNative.EnumerateForceFeedbackDevices();
            var forceFeedbackIds = forceFeedbackDevices
                .Select(d => d.InstanceGuid)
                .ToHashSet();

            lines.Add($"DirectInput game controllers: {controllers.Count}");
            if (controllers.Count == 0)
            {
                lines.Add("  No attached DirectInput game controllers were reported by Windows.");
            }

            foreach (var controller in controllers)
            {
                var supportsForceFeedback = forceFeedbackIds.Contains(controller.InstanceGuid);
                lines.Add($"  {(supportsForceFeedback ? "[FFB]" : "[no FFB]")} {DisplayName(controller)}");
            }

            lines.Add($"DirectInput force-feedback devices: {forceFeedbackDevices.Count}");
            foreach (var device in forceFeedbackDevices)
            {
                lines.Add($"  {DisplayName(device)}");
            }
        }
        catch (Exception ex)
        {
            lines.Add($"DirectInput diagnostics failed: {ex.Message}");
        }

        return lines;
    }

    private static string DisplayName(DirectInputDeviceInfo deviceInfo)
    {
        if (!string.IsNullOrWhiteSpace(deviceInfo.ProductName))
        {
            return deviceInfo.ProductName;
        }

        return string.IsNullOrWhiteSpace(deviceInfo.InstanceName)
            ? deviceInfo.InstanceGuid.ToString()
            : deviceInfo.InstanceName;
    }
}
