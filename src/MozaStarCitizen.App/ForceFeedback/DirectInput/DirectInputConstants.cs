namespace MozaStarCitizen.App.ForceFeedback.DirectInput;

internal static class DirectInputConstants
{
    public const int DirectInputVersion = 0x0800;

    public const int Di8DevClassGameControl = 4;
    public const int DiEnumAttachedOnly = 0x00000001;
    public const int DiEnumForceFeedback = 0x00000100;

    public const int DiExclusive = 0x00000001;
    public const int DiBackground = 0x00000008;

    public const int DidfAbsoluteAxis = 0x00000001;
    public const int DidftAbsoluteAxis = 0x00000002;

    public const int DisffcReset = 0x00000001;
    public const int DisffcStopAll = 0x00000002;
    public const int DisffcSetActuatorsOn = 0x00000010;

    public const int DieffCartesian = 0x00000010;
    public const int DieffObjectOffsets = 0x00000002;

    public const int DiDegrees = 100;
    public const int DiFfNominalMax = 10000;
    public const int Infinite = unchecked((int)0xffffffff);

    public const int DijoFsX = 0;
    public const int DijoFsY = 4;

    public const int DiepDuration = 0x00000001;
    public const int DiepGain = 0x00000004;
    public const int DiepAxes = 0x00000020;
    public const int DiepDirection = 0x00000040;
    public const int DiepTypespecificparams = 0x00000080;
    public const int DiepStart = 0x20000000;
    public const int DiepNoDownload = unchecked((int)0x80000000);

    public const int EffectParameterFlags =
        DiepDuration |
        DiepGain |
        DiepAxes |
        DiepDirection |
        DiepTypespecificparams;

    public static readonly Guid IidDirectInput8W = new("BF798031-483A-4DA2-AA99-5D64ED369700");

    public static readonly Guid GuidXAxis = new("A36D02E0-C9F3-11CF-BFC7-444553540000");
    public static readonly Guid GuidYAxis = new("A36D02E1-C9F3-11CF-BFC7-444553540000");

    public static readonly Guid GuidConstantForce = new("13541C20-8E33-11D0-9AD0-00A0C9A06E35");
    public static readonly Guid GuidSine = new("13541C23-8E33-11D0-9AD0-00A0C9A06E35");

    public static int DidftMakeInstance(int instance) =>
        (instance & 0xffff) << 8;
}
