using System.Runtime.InteropServices;

namespace Moza.ScLink.DirectInput.Native;

[ComImport]
[Guid("BF798031-483A-4DA2-AA99-5D64ED369700")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirectInput8W
{
    [PreserveSig]
    public int CreateDevice(ref Guid rguid, out IDirectInputDevice8W directInputDevice, IntPtr outer);

    [PreserveSig]
    public int EnumDevices(
        int deviceType,
        IntPtr callback,
        IntPtr context,
        int flags);

    [PreserveSig]
    public int GetDeviceStatus(ref Guid instanceGuid);

    [PreserveSig]
    public int RunControlPanel(IntPtr ownerWindow, int flags);

    [PreserveSig]
    public int Initialize(IntPtr instanceHandle, int version);
}

[ComImport]
[Guid("54D41081-DC15-4833-A41B-748F73A38179")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirectInputDevice8W
{
    [PreserveSig]
    public int GetCapabilities(IntPtr capabilities);

    [PreserveSig]
    public int EnumObjects(IntPtr callback, IntPtr context, int flags);

    [PreserveSig]
    public int GetProperty(ref Guid propertyGuid, IntPtr property);

    [PreserveSig]
    public int SetProperty(ref Guid propertyGuid, IntPtr property);

    [PreserveSig]
    public int Acquire();

    [PreserveSig]
    public int Unacquire();

    [PreserveSig]
    public int GetDeviceState(int dataSize, IntPtr data);

    [PreserveSig]
    public int GetDeviceData(int objectDataSize, IntPtr objectData, ref int inOut, int flags);

    [PreserveSig]
    public int SetDataFormat(IntPtr dataFormat);

    [PreserveSig]
    public int SetEventNotification(IntPtr eventHandle);

    [PreserveSig]
    public int SetCooperativeLevel(IntPtr windowHandle, int flags);

    [PreserveSig]
    public int GetObjectInfo(IntPtr objectInstance, int objectId, int flags);

    [PreserveSig]
    public int GetDeviceInfo(IntPtr deviceInstance);

    [PreserveSig]
    public int RunControlPanel(IntPtr ownerWindow, int flags);

    [PreserveSig]
    public int Initialize(IntPtr instanceHandle, int version, ref Guid instanceGuid);

    [PreserveSig]
    public int CreateEffect(ref Guid effectGuid, ref DirectInputEffect effect, out IDirectInputEffect directInputEffect, IntPtr outer);

    [PreserveSig]
    public int EnumEffects(IntPtr callback, IntPtr context, int effectType);

    [PreserveSig]
    public int GetEffectInfo(IntPtr effectInfo, ref Guid effectGuid);

    [PreserveSig]
    public int GetForceFeedbackState(out int state);

    [PreserveSig]
    public int SendForceFeedbackCommand(int flags);
}

[ComImport]
[Guid("E7E1F7C0-88D2-11D0-9AD0-00A0C9A06E35")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirectInputEffect
{
    [PreserveSig]
    public int Initialize(IntPtr instanceHandle, int version, ref Guid effectGuid);

    [PreserveSig]
    public int GetEffectGuid(out Guid effectGuid);

    [PreserveSig]
    public int GetParameters(IntPtr effect, int flags);

    [PreserveSig]
    public int SetParameters(ref DirectInputEffect effect, int flags);

    [PreserveSig]
    public int Start(int iterations, int flags);

    [PreserveSig]
    public int Stop();

    [PreserveSig]
    public int GetEffectStatus(out int flags);

    [PreserveSig]
    public int Download();

    [PreserveSig]
    public int Unload();

    [PreserveSig]
    public int Escape(IntPtr escape);
}

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
[return: MarshalAs(UnmanagedType.Bool)]
internal delegate bool DirectInputEnumDevicesCallback(IntPtr instance, IntPtr context);
