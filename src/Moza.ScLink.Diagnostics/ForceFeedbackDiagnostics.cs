using Moza.ScLink.Core;
using Moza.ScLink.Core.Models;
using Moza.ScLink.DirectInput;

namespace Moza.ScLink.Diagnostics;

public static class ForceFeedbackDiagnostics
{
    public static IReadOnlyList<string> GetLines(
        Moza.ScLink.Core.Devices.IForceFeedbackDevice selectedDevice,
        bool includeExtendedDiagnostics)
    {
        var lines = new List<string>
        {
            $"Output mode: {Environment.GetEnvironmentVariable("MOZA_SC_OUTPUT") ?? "Auto"}",
            $"Selected output: {selectedDevice.DisplayName}",
            $"Output status: {selectedDevice.State.ToUserFacingString()}"
        };

        if (!includeExtendedDiagnostics)
        {
            lines.Add("Press Refresh to probe DirectInput controllers.");
            return lines;
        }

        try
        {
            using var abstraction = new VorticeDirectInputAdapter();
            var controllers = abstraction.EnumerateAllGameControllers();
            var forceFeedbackDevices = abstraction.EnumerateForceFeedbackDevices();
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

            var allowlist = DeviceAllowlist.LoadDefault();
            lines.Add($"DirectInput force-feedback devices: {forceFeedbackDevices.Count}");
            foreach (var device in forceFeedbackDevices)
            {
                var model = allowlist.Classify(device.ProductName);
                var classification = model == DeviceModel.Unknown
                    ? "Unknown — not allowlisted, will not be driven"
                    : model.ToString();
                lines.Add($"  {DisplayName(device)} [{classification}]");
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
