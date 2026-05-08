using System.Runtime.InteropServices;

namespace MozaStarCitizen.App.ForceFeedback.DirectInput;

internal static class DirectInputNative
{
    public static IDirectInput8W CreateDirectInput()
    {
        var iid = DirectInputConstants.IidDirectInput8W;
        var hResult = DirectInput8Create(
            GetModuleHandle(null),
            DirectInputConstants.DirectInputVersion,
            ref iid,
            out var directInput,
            IntPtr.Zero);

        ThrowIfFailed(hResult, "DirectInput8Create failed");
        return directInput;
    }

    public static IReadOnlyList<DirectInputDeviceInfo> EnumerateForceFeedbackDevices()
    {
        return EnumerateDevices(DirectInputConstants.DiEnumAttachedOnly | DirectInputConstants.DiEnumForceFeedback);
    }

    public static IReadOnlyList<DirectInputDeviceInfo> EnumerateGameControllers()
    {
        return EnumerateDevices(DirectInputConstants.DiEnumAttachedOnly);
    }

    public static void SetTwoAxisJoystickDataFormat(IDirectInputDevice8W device, string deviceName)
    {
        var xAxisGuid = IntPtr.Zero;
        var yAxisGuid = IntPtr.Zero;
        var objects = IntPtr.Zero;
        var dataFormat = IntPtr.Zero;

        try
        {
            xAxisGuid = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
            yAxisGuid = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
            objects = Marshal.AllocHGlobal(Marshal.SizeOf<DirectInputObjectDataFormat>() * 2);
            dataFormat = Marshal.AllocHGlobal(Marshal.SizeOf<DirectInputDataFormat>());

            Marshal.StructureToPtr(DirectInputConstants.GuidXAxis, xAxisGuid, false);
            Marshal.StructureToPtr(DirectInputConstants.GuidYAxis, yAxisGuid, false);

            var xAxis = new DirectInputObjectDataFormat
            {
                ObjectGuid = xAxisGuid,
                Offset = DirectInputConstants.DijoFsX,
                Type = DirectInputConstants.DidftAbsoluteAxis | DirectInputConstants.DidftMakeInstance(0),
                Flags = 0
            };

            var yAxis = new DirectInputObjectDataFormat
            {
                ObjectGuid = yAxisGuid,
                Offset = DirectInputConstants.DijoFsY,
                Type = DirectInputConstants.DidftAbsoluteAxis | DirectInputConstants.DidftMakeInstance(1),
                Flags = 0
            };

            Marshal.StructureToPtr(xAxis, objects, false);
            Marshal.StructureToPtr(
                yAxis,
                objects + Marshal.SizeOf<DirectInputObjectDataFormat>(),
                false);

            var format = new DirectInputDataFormat
            {
                Size = Marshal.SizeOf<DirectInputDataFormat>(),
                ObjectSize = Marshal.SizeOf<DirectInputObjectDataFormat>(),
                Flags = DirectInputConstants.DidfAbsoluteAxis,
                DataSize = sizeof(int) * 2,
                ObjectCount = 2,
                Objects = objects
            };

            Marshal.StructureToPtr(format, dataFormat, false);
            ThrowIfFailed(device.SetDataFormat(dataFormat), $"DirectInput could not set a two-axis data format for '{deviceName}'");
        }
        finally
        {
            FreeIfAllocated(dataFormat);
            FreeIfAllocated(objects);
            FreeIfAllocated(yAxisGuid);
            FreeIfAllocated(xAxisGuid);
        }
    }

    private static IReadOnlyList<DirectInputDeviceInfo> EnumerateDevices(int flags)
    {
        var directInput = CreateDirectInput();
        var devices = new List<DirectInputDeviceInfo>();

        DirectInputEnumDevicesCallback callback = (instancePointer, _) =>
        {
            var instance = Marshal.PtrToStructure<DirectInputDeviceInstance>(instancePointer);
            devices.Add(new DirectInputDeviceInfo(
                instance.GuidInstance,
                instance.ProductName,
                instance.InstanceName));
            return true;
        };

        try
        {
            var callbackPointer = Marshal.GetFunctionPointerForDelegate(callback);
            ThrowIfFailed(
                directInput.EnumDevices(DirectInputConstants.Di8DevClassGameControl, callbackPointer, IntPtr.Zero, flags),
                "DirectInput device enumeration failed");
            return devices;
        }
        finally
        {
            GC.KeepAlive(callback);
            _ = Marshal.FinalReleaseComObject(directInput);
        }
    }

    private static void FreeIfAllocated(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    public static bool Succeeded(int hResult) => hResult >= 0;

    public static void ThrowIfFailed(int hResult, string message)
    {
        if (!Succeeded(hResult))
        {
            throw new InvalidOperationException($"{message}. HRESULT: 0x{hResult:X8}.");
        }
    }

    [DllImport("dinput8.dll", EntryPoint = "DirectInput8Create", ExactSpelling = true)]
    private static extern int DirectInput8Create(
        IntPtr hinst,
        int dwVersion,
        ref Guid riidltf,
        out IDirectInput8W ppvOut,
        IntPtr punkOuter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
