using Microsoft.Extensions.Logging;
using Moza.ScLink.Core;
using Moza.ScLink.Core.Diagnostics;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Diagnostics.MozaSdkProbe;
using Moza.ScLink.DirectInput;
using Serilog;
using Serilog.Extensions.Logging;

namespace Moza.ScLink.App.ForceFeedback;

public static class ForceFeedbackDeviceFactory
{
    // R5: bridges Serilog to Microsoft.Extensions.Logging so the new VorticeDirectInputDevice and
    // LegacyForceFeedbackDeviceAdapter (both ILogger<T>-constructed) receive a real logger. Created
    // once, reused across calls, never disposed — its lifetime matches the app lifetime; Serilog
    // tolerates this (Program.Main owns Log.CloseAndFlush).
    private static readonly ILoggerFactory _loggerFactory =
        LoggerFactory.Create(b => b.AddSerilog(Log.Logger));

    // Production IDirectInputAbstraction. Lazily constructed inside CreateDirectInputDevice's
    // try/catch (DInput.DirectInput8Create can throw) and cached for the app lifetime —
    // VorticeDirectInputDevice keeps a reference to it and uses it during its deferred
    // InitializeAsync, so it must outlive Create().
    private static VorticeDirectInputAdapter? _directInputAbstraction;

    public static IForceFeedbackDevice Create()
    {
        var outputMode = ParseOutputMode(Environment.GetEnvironmentVariable("MOZA_SC_OUTPUT"));
        var devices = new List<IForceFeedbackDevice>();

        var directInput = CreateDirectInputDevice();
        var managedSdk = MozaSdkManagedForceFeedbackDevice.CreateIfAvailable();
        var bridge = MozaSdkForceFeedbackDevice.CreateIfAvailable();

        switch (outputMode)
        {
            case ForceFeedbackOutputMode.DirectInput:
                AddIfPresent(devices, directInput);
                break;
            case ForceFeedbackOutputMode.MozaSdk:
                AddIfPresent(devices, managedSdk);
                break;
            case ForceFeedbackOutputMode.NativeBridge:
                AddIfPresent(devices, bridge);
                break;
            case ForceFeedbackOutputMode.Preview:
                break;
            default:
                AddIfPresent(devices, directInput);
                AddIfPresent(devices, bridge);
                AddIfPresent(devices, managedSdk);
                break;
        }

        devices.Add(new NullForceFeedbackDevice($"Output mode '{outputMode}' had no working hardware output. Effects are logged for parser validation."));

        return new FallbackForceFeedbackDevice(devices);
    }

    // Replaces the legacy DirectInputForceFeedbackDevice.CreateIfAvailable(). Enumerates DirectInput
    // force-feedback devices through the Vortice abstraction, classifies each by product name, and
    // wraps the first allowlisted device as LegacyForceFeedbackDeviceAdapter(new VorticeDirectInputDevice(...)).
    // Returns null when nothing is allowlisted or enumeration fails — the caller falls through to the
    // Null device, exactly as the legacy CreateIfAvailable() did.
    //
    // CS0618: DeviceClassifier and LegacyForceFeedbackDeviceAdapter are [Obsolete] transitional types;
    // the factory is their intended consumer for the T-07/T-08 migration window (both deleted with their
    // successors per issue #15). CA1859: suppressed rather than fixed by narrowing the return type,
    // because LegacyForceFeedbackDeviceAdapter? as the return type would propagate the obsolete type
    // into Create()'s var inference and expand the obsolete-warning surface beyond this method.
    // IForceFeedbackDevice? is also the factory's domain contract — Create() composes
    // IForceFeedbackDevice — making containment the cleaner architectural fit.
#pragma warning disable CS0618, CA1859
    private static IForceFeedbackDevice? CreateDirectInputDevice()
    {
        try
        {
            _directInputAbstraction ??= new VorticeDirectInputAdapter();

            foreach (var info in _directInputAbstraction.EnumerateForceFeedbackDevices())
            {
                var model = DeviceClassifier.ClassifyByProductName(info.ProductName);
                if (model == DeviceModel.Unknown)
                {
                    AppLog.Write($"Skipping unrecognized force-feedback device: {info.ProductName}");
                    continue;
                }

                var identity = new DirectInputDeviceIdentity(
                    info.InstanceGuid,
                    info.ProductName,
                    info.ProductName,   // DisplayName == ProductName at the T-07 layer; T-08's allowlist JSON may differentiate
                    model);

                var device = new VorticeDirectInputDevice(
                    _directInputAbstraction,
                    identity,
                    _loggerFactory.CreateLogger<VorticeDirectInputDevice>());

                // First allowlisted device wins — matches the legacy IsPreferredDevice priority.
                return new LegacyForceFeedbackDeviceAdapter(
                    device,
                    _loggerFactory.CreateLogger<LegacyForceFeedbackDeviceAdapter>());
            }

            return null;
        }
        catch (Exception ex)
        {
            // Preserves the legacy CreateIfAvailable() resilience: a COM/enumeration failure degrades
            // to the Null device rather than crashing app startup (T-07.md non-goal: "No behavioral
            // changes"). CA1031 is .editorconfig 'suggestion' severity — this comment is the justification.
            AppLog.Write(ex, "DirectInput force-feedback enumeration failed");
            return null;
        }
    }
#pragma warning restore CS0618, CA1859

    private static void AddIfPresent(List<IForceFeedbackDevice> devices, IForceFeedbackDevice? device)
    {
        if (device is not null)
        {
            devices.Add(device);
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
