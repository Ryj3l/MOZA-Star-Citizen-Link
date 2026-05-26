using Microsoft.Extensions.Logging;
using Moza.ScLink.Core;
using Moza.ScLink.Core.Diagnostics;
using Moza.ScLink.Core.Models;
using Moza.ScLink.DirectInput;
using Serilog;
using Serilog.Extensions.Logging;

namespace Moza.ScLink.App.ForceFeedback;

public static class ForceFeedbackDeviceFactory
{
    // R5: bridges Serilog to Microsoft.Extensions.Logging so the canonical VorticeDirectInputDevice and
    // the LoggingNullForceFeedbackDevice (both ILogger<T>-constructed) receive a real logger. Created
    // once, reused across calls, never disposed — its lifetime matches the app lifetime; Serilog
    // tolerates this (Program.Main owns Log.CloseAndFlush).
    private static readonly ILoggerFactory _loggerFactory =
        LoggerFactory.Create(b => b.AddSerilog(Log.Logger));

    // Production IDirectInputAbstraction. Lazily constructed inside TryEnumerateAllowlistedVorticeDevice's
    // try/catch (DInput.DirectInput8Create can throw) and cached for the app lifetime —
    // VorticeDirectInputDevice keeps a reference to it and uses it during its deferred InitializeAsync,
    // so it must outlive the factory call.
    private static VorticeDirectInputAdapter? _directInputAbstraction;

    // Loaded once from device-allowlist.json (beside the executable) and reused for the app lifetime —
    // read-only config, no per-call reload needed. Mirrors the _directInputAbstraction caching above.
    private static DeviceAllowlist? _allowlist;

    /// <summary>
    /// T-27 canonical device factory. Decides ONCE at startup: the unwrapped canonical
    /// <see cref="VorticeDirectInputDevice"/> if an allowlisted DirectInput device enumerates, otherwise
    /// the <see cref="LoggingNullForceFeedbackDevice"/> no-hardware fallback. <c>MOZA_SC_OUTPUT=Preview</c>
    /// forces the fallback without enumerating (dev affordance on a machine that has hardware). Canonical
    /// hot-plug + an SDK-bridge tier on this interface are tracked as a follow-up (PRP §10.1, line 1011).
    /// </summary>
    public static Moza.ScLink.Core.Devices.IForceFeedbackDevice CreateCanonical()
    {
        if (ParseOutputMode(Environment.GetEnvironmentVariable("MOZA_SC_OUTPUT")) != ForceFeedbackOutputMode.Preview)
        {
            var device = TryEnumerateAllowlistedVorticeDevice();
            if (device is not null)
            {
                return device;
            }
        }

        return new LoggingNullForceFeedbackDevice(
            _loggerFactory.CreateLogger<LoggingNullForceFeedbackDevice>());
    }

    // Enumerates allowlisted DirectInput force-feedback devices and returns the first as an unwrapped
    // canonical VorticeDirectInputDevice. Returns null when nothing is allowlisted or enumeration fails —
    // CreateCanonical then falls back to the logging-null device.
    private static VorticeDirectInputDevice? TryEnumerateAllowlistedVorticeDevice()
    {
        try
        {
            _directInputAbstraction ??= new VorticeDirectInputAdapter();
            _allowlist ??= DeviceAllowlist.LoadDefault();

            var detector = new DeviceDetector(_directInputAbstraction, _allowlist);
            foreach (var detected in detector.DetectForceFeedbackDevices())
            {
                if (detected.Model == DeviceModel.Unknown)
                {
                    AppLog.Write($"Skipping unrecognized force-feedback device: {detected.Info.ProductName}");
                    continue;
                }

                var identity = new DirectInputDeviceIdentity(
                    detected.Info.InstanceGuid,
                    detected.Info.ProductName,
                    detected.Info.ProductName,   // DisplayName == ProductName until T-14 profile names land
                    detected.Model);

                // First allowlisted device wins — matches the legacy IsPreferredDevice priority.
                return new VorticeDirectInputDevice(
                    _directInputAbstraction,
                    identity,
                    _loggerFactory.CreateLogger<VorticeDirectInputDevice>());
            }

            return null;
        }
        catch (Exception ex)
        {
            // A COM/enumeration failure degrades to the logging-null device rather than crashing app
            // startup. CA1031 is .editorconfig 'suggestion' severity — this comment is the justification.
            AppLog.Write(ex, "DirectInput canonical force-feedback enumeration failed");
            return null;
        }
    }

    private static ForceFeedbackOutputMode ParseOutputMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ForceFeedbackOutputMode.Auto;
        }

        return Enum.TryParse<ForceFeedbackOutputMode>(value, ignoreCase: true, out var mode)
            ? mode
            : ForceFeedbackOutputMode.Auto;
    }
}
