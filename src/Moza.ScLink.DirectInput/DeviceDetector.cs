using Moza.ScLink.Core.Models;

namespace Moza.ScLink.DirectInput;

/// <summary>
/// Enumerates DirectInput force-feedback devices and classifies each against a
/// <see cref="DeviceAllowlist"/>. Returns every enumerated device — including
/// <see cref="DeviceModel.Unknown"/> ones — so diagnostics can list them. Refusing to *drive* Unknown
/// devices is the caller's job; the <see cref="VorticeDirectInputDevice"/> constructor also refuses
/// Unknown as a backstop.
/// </summary>
public sealed class DeviceDetector
{
    private readonly IDirectInputAbstraction _abstraction;
    private readonly DeviceAllowlist _allowlist;

    public DeviceDetector(IDirectInputAbstraction abstraction, DeviceAllowlist allowlist)
    {
        ArgumentNullException.ThrowIfNull(abstraction);
        ArgumentNullException.ThrowIfNull(allowlist);
        _abstraction = abstraction;
        _allowlist = allowlist;
    }

    /// <summary>
    /// Enumerates attached force-feedback devices and pairs each with its classified
    /// <see cref="DeviceModel"/>. Order follows DirectInput enumeration order.
    /// </summary>
    public IReadOnlyList<DetectedDevice> DetectForceFeedbackDevices()
    {
        var infos = _abstraction.EnumerateForceFeedbackDevices();
        var detected = new List<DetectedDevice>(infos.Count);
        foreach (var info in infos)
        {
            detected.Add(new DetectedDevice(info, _allowlist.Classify(info.ProductName)));
        }

        return detected;
    }
}

/// <summary>A DirectInput force-feedback device paired with its allowlist classification.</summary>
public readonly record struct DetectedDevice(DirectInputDeviceInfo Info, DeviceModel Model);
