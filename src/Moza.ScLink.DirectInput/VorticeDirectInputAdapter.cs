using Vortice.DirectInput;

namespace Moza.ScLink.DirectInput;

/// <summary>
/// Production implementation of <see cref="IDirectInputAbstraction"/> backed by Vortice.DirectInput 3.6.2.
/// Holds the singleton <see cref="IDirectInput8"/> for the app lifetime; owns the device-enumeration entry points
/// for both force-feedback-only enumeration (used by <see cref="VorticeDirectInputDevice"/>) and full game-controller
/// enumeration (used by <c>Moza.ScLink.Diagnostics.ForceFeedbackDiagnostics</c> through the public abstraction surface).
/// </summary>
/// <remarks>
/// Exceptions thrown by Vortice (typically <see cref="SharpGen.Runtime.SharpGenException"/>) bubble out unmodified.
/// Callers route them through <see cref="DirectInputErrorClassifier"/>. Adapters do not editorialize on exceptions —
/// rewrapping would defeat the classifier's Tier-1 SharpGenException short-circuit.
/// </remarks>
public sealed class VorticeDirectInputAdapter : IDirectInputAbstraction
{
    private readonly IDirectInput8 _di8;
    private bool _disposed;

    /// <summary>Creates a new adapter and the underlying <see cref="IDirectInput8"/> COM object.</summary>
    public VorticeDirectInputAdapter()
    {
        _di8 = DInput.DirectInput8Create();
    }

    /// <inheritdoc />
    public IReadOnlyList<DirectInputDeviceInfo> EnumerateForceFeedbackDevices()
    {
        ThrowIfDisposed();
        var instances = _di8.GetDevices(
            DeviceClass.GameControl,
            DeviceEnumerationFlags.AttachedOnly | DeviceEnumerationFlags.ForceFeedback);

        return MapInstances(instances);
    }

    /// <inheritdoc />
    public IReadOnlyList<DirectInputDeviceInfo> EnumerateAllGameControllers()
    {
        ThrowIfDisposed();
        var instances = _di8.GetDevices(
            DeviceClass.GameControl,
            DeviceEnumerationFlags.AttachedOnly);

        return MapInstances(instances);
    }

    /// <inheritdoc />
    public IDirectInputDeviceAbstraction CreateDevice(Guid instanceGuid)
    {
        ThrowIfDisposed();
        var device = _di8.CreateDevice(instanceGuid);
        return new VorticeDirectInputDeviceAdapter(device, instanceGuid);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _di8.Dispose();
        _disposed = true;
    }

    private static List<DirectInputDeviceInfo> MapInstances(IList<DeviceInstance> instances)
    {
        var mapped = new List<DirectInputDeviceInfo>(instances.Count);
        foreach (var instance in instances)
        {
            mapped.Add(new DirectInputDeviceInfo(
                instance.InstanceGuid,
                instance.ProductName,
                instance.InstanceName));
        }

        return mapped;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
