using Vortice.DirectInput;

namespace Moza.ScLink.DirectInput;

/// <summary>
/// Production implementation of <see cref="IDirectInputDeviceAbstraction"/> wrapping a single
/// <see cref="IDirectInputDevice8"/>. Constructed by <see cref="VorticeDirectInputAdapter.CreateDevice"/>;
/// owned by the caller (<see cref="VorticeDirectInputDevice"/>) for the device's lifetime.
/// </summary>
/// <remarks>
/// Method-level implementation notes:
/// <list type="bullet">
///   <item><see cref="SetJoystickDataFormat"/> routes through Vortice's typed-generic <c>SetDataFormat&lt;RawJoystickState&gt;()</c>.
///         Vortice 3.6.2 does not expose a <c>DataFormat.Joystick</c> static; the typed-generic API selects the canonical layout.</item>
///   <item><see cref="SetGain"/> routes through <c>device.Properties.ForceFeedbackGain</c> (on <see cref="Vortice.DirectInput.ObjectProperties"/>);
///         <see cref="IDirectInputDevice8"/> has no top-level <c>SetGain</c> method.</item>
///   <item><see cref="CreateEffect"/> wraps the produced <see cref="IDirectInputEffect"/> in <see cref="VorticeDirectInputEffectAdapter"/>.</item>
///   <item>Vortice exceptions (<see cref="SharpGen.Runtime.SharpGenException"/>) bubble out unmodified for <see cref="DirectInputErrorClassifier"/> to classify.</item>
/// </list>
/// </remarks>
internal sealed class VorticeDirectInputDeviceAdapter : IDirectInputDeviceAbstraction
{
    private readonly IDirectInputDevice8 _device;
    private bool _disposed;

    public VorticeDirectInputDeviceAdapter(IDirectInputDevice8 device, Guid instanceGuid)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        InstanceGuid = instanceGuid;
    }

    /// <inheritdoc />
    public string ProductName => _device.DeviceInfo.ProductName;

    /// <inheritdoc />
    public Guid InstanceGuid { get; }

    /// <inheritdoc />
    public void SetCooperativeLevel(IntPtr hwnd, CooperativeLevel level)
    {
        ThrowIfDisposed();
        _device.SetCooperativeLevel(hwnd, level);
    }

    /// <inheritdoc />
    public void SetJoystickDataFormat()
    {
        ThrowIfDisposed();
        _device.SetDataFormat<RawJoystickState>();
    }

    /// <inheritdoc />
    public void Acquire()
    {
        ThrowIfDisposed();
        _device.Acquire();
    }

    /// <inheritdoc />
    public void Unacquire()
    {
        ThrowIfDisposed();
        _device.Unacquire();
    }

    /// <inheritdoc />
    public IDirectInputEffectAbstraction CreateEffect(Guid effectGuid, EffectParameters parameters)
    {
        ThrowIfDisposed();
        var effect = _device.CreateEffect(effectGuid, parameters);
        return new VorticeDirectInputEffectAdapter(effect);
    }

    /// <inheritdoc />
    public void SetGain(int gain)
    {
        ThrowIfDisposed();
        _device.Properties.ForceFeedbackGain = gain;
    }

    /// <inheritdoc />
    public void SendForceFeedbackCommand(ForceFeedbackCommand command)
    {
        ThrowIfDisposed();
        _device.SendForceFeedbackCommand(command);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _device.Unacquire();
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            // Already unacquired or device gone — disposal proceeds regardless.
        }

        _device.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
