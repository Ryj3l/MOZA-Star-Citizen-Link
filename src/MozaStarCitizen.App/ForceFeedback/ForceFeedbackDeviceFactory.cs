namespace MozaStarCitizen.App.ForceFeedback;

public static class ForceFeedbackDeviceFactory
{
    public static IForceFeedbackDevice Create()
    {
        var outputMode = ParseOutputMode(Environment.GetEnvironmentVariable("MOZA_SC_OUTPUT"));
        var devices = new List<IForceFeedbackDevice>();

        var directInput = DirectInputForceFeedbackDevice.CreateIfAvailable();
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
