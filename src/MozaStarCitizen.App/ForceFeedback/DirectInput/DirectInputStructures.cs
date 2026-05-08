using System.Runtime.InteropServices;

namespace MozaStarCitizen.App.ForceFeedback.DirectInput;

internal sealed record DirectInputDeviceInfo(Guid InstanceGuid, string ProductName, string InstanceName);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DirectInputDeviceInstance
{
    public int Size;
    public Guid GuidInstance;
    public Guid GuidProduct;
    public int DeviceType;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string InstanceName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string ProductName;

    public Guid GuidForceFeedbackDriver;
    public ushort UsagePage;
    public ushort Usage;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DirectInputEffect
{
    public int Size;
    public int Flags;
    public int Duration;
    public int SamplePeriod;
    public int Gain;
    public int TriggerButton;
    public int TriggerRepeatInterval;
    public int AxisCount;
    public IntPtr Axes;
    public IntPtr Direction;
    public IntPtr Envelope;
    public int TypeSpecificParameterSize;
    public IntPtr TypeSpecificParameters;
    public int StartDelay;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DirectInputPeriodic
{
    public int Magnitude;
    public int Offset;
    public int Phase;
    public int Period;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DirectInputConstantForce
{
    public int Magnitude;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DirectInputObjectDataFormat
{
    public IntPtr ObjectGuid;
    public int Offset;
    public int Type;
    public int Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DirectInputDataFormat
{
    public int Size;
    public int ObjectSize;
    public int Flags;
    public int DataSize;
    public int ObjectCount;
    public IntPtr Objects;
}
