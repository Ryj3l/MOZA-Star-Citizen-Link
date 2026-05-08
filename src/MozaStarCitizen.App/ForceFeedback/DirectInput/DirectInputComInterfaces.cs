using System.Runtime.InteropServices;

namespace MozaStarCitizen.App.ForceFeedback.DirectInput;

[ComImport]
[Guid("BF798031-483A-4DA2-AA99-5D64ED369700")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirectInput8W
{
    [PreserveSig]
    int CreateDevice(ref Guid rguid, out IDirectInputDevice8W directInputDevice, IntPtr outer);

    [PreserveSig]
    int EnumDevices(
        int deviceType,
        IntPtr callback,
        IntPtr context,
        int flags);

    [PreserveSig]
    int GetDeviceStatus(ref Guid instanceGuid);

    [PreserveSig]
    int RunControlPanel(IntPtr ownerWindow, int flags);

    [PreserveSig]
    int Initialize(IntPtr instanceHandle, int version);
}

[ComImport]
[Guid("54D41081-DC15-4833-A41B-748F73A38179")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirectInputDevice8W
{
    [PreserveSig]
    int GetCapabilities(IntPtr capabilities);

    [PreserveSig]
    int EnumObjects(IntPtr callback, IntPtr context, int flags);

    [PreserveSig]
    int GetProperty(ref Guid propertyGuid, IntPtr property);

    [PreserveSig]
    int SetProperty(ref Guid propertyGuid, IntPtr property);

    [PreserveSig]
    int Acquire();

    [PreserveSig]
    int Unacquire();

    [PreserveSig]
    int GetDeviceState(int dataSize, IntPtr data);

    [PreserveSig]
    int GetDeviceData(int objectDataSize, IntPtr objectData, ref int inOut, int flags);

    [PreserveSig]
    int SetDataFormat(IntPtr dataFormat);

    [PreserveSig]
    int SetEventNotification(IntPtr eventHandle);

    [PreserveSig]
    int SetCooperativeLevel(IntPtr windowHandle, int flags);

    [PreserveSig]
    int GetObjectInfo(IntPtr objectInstance, int objectId, int flags);

    [PreserveSig]
    int GetDeviceInfo(IntPtr deviceInstance);

    [PreserveSig]
    int RunControlPanel(IntPtr ownerWindow, int flags);

    [PreserveSig]
    int Initialize(IntPtr instanceHandle, int version, ref Guid instanceGuid);

    [PreserveSig]
    int CreateEffect(ref Guid effectGuid, ref DirectInputEffect effect, out IDirectInputEffect directInputEffect, IntPtr outer);

    [PreserveSig]
    int EnumEffects(IntPtr callback, IntPtr context, int effectType);

    [PreserveSig]
    int GetEffectInfo(IntPtr effectInfo, ref Guid effectGuid);

    [PreserveSig]
    int GetForceFeedbackState(out int state);

    [PreserveSig]
    int SendForceFeedbackCommand(int flags);
}

[ComImport]
[Guid("E7E1F7C0-88D2-11D0-9AD0-00A0C9A06E35")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirectInputEffect
{
    [PreserveSig]
    int Initialize(IntPtr instanceHandle, int version, ref Guid effectGuid);

    [PreserveSig]
    int GetEffectGuid(out Guid effectGuid);

    [PreserveSig]
    int GetParameters(IntPtr effect, int flags);

    [PreserveSig]
    int SetParameters(ref DirectInputEffect effect, int flags);

    [PreserveSig]
    int Start(int iterations, int flags);

    [PreserveSig]
    int Stop();

    [PreserveSig]
    int GetEffectStatus(out int flags);

    [PreserveSig]
    int Download();

    [PreserveSig]
    int Unload();

    [PreserveSig]
    int Escape(IntPtr escape);
}

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
[return: MarshalAs(UnmanagedType.Bool)]
internal delegate bool DirectInputEnumDevicesCallback(IntPtr instance, IntPtr context);
