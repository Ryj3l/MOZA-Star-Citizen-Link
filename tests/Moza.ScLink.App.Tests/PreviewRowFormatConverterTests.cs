using System.Globalization;
using Moza.ScLink.App.ForceFeedback;
using Moza.ScLink.Core.Effects;

namespace Moza.ScLink.App.Tests;

/// <summary>
/// Covers <see cref="PreviewRowFormatConverter"/> (#76) row formatting. Each case calls Convert
/// directly with a 7-input value array (the same order the XAML MultiBinding supplies) and
/// asserts the exact rendered string. Layer 2 of Fork 4 — the converter is the only place
/// in the App layer where envelope detail becomes a user-visible string.
/// </summary>
public sealed class PreviewRowFormatConverterTests
{
    private static readonly DateTime SampleWallClock = new(2026, 5, 30, 14, 35, 12, 250, DateTimeKind.Local);

    private static object?[] Values(
        DateTime wallClock,
        string commandType,
        string? effectId,
        double? finalIntensity,
        ForceEnvelope? envelope,
        int activeCount,
        bool showEnvelopeDetail)
        => new object?[] { wallClock, commandType, effectId, finalIntensity, envelope, activeCount, showEnvelopeDetail };

    private static string Render(object?[] values)
    {
        var converter = new PreviewRowFormatConverter();
        return (string)converter.Convert(values, typeof(string), parameter: null, CultureInfo.InvariantCulture)!;
    }

    [Fact]
    public void ToggleOffNullEnvelopeRendersCompactFiveFieldRow()
    {
        var rendered = Render(Values(SampleWallClock, "PlayEffectCommand", "quantum-spool-v1",
            finalIntensity: 0.42, envelope: null, activeCount: 1, showEnvelopeDetail: false));

        Assert.Equal(
            "14:35:12.250  PlayEffectCommand  quantum-spool-v1  i=0.42  active=1",
            rendered);
    }

    [Fact]
    public void ToggleOffWithEnvelopeStillRendersOnlyCompactRow()
    {
        var envelope = new ForceEnvelope(
            TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(80),
            AttackLevel: 0.6, SustainLevel: 0.3);

        var rendered = Render(Values(SampleWallClock, "PlayEffectCommand", "atmo-entry-v1",
            finalIntensity: 0.22, envelope: envelope, activeCount: 1, showEnvelopeDetail: false));

        // Toggle gates rendering, not envelope presence: same row whether envelope is set or null.
        Assert.Equal(
            "14:35:12.250  PlayEffectCommand  atmo-entry-v1  i=0.22  active=1",
            rendered);
    }

    [Fact]
    public void ToggleOnWithEnvelopeAppendsEnvelopeClause()
    {
        var envelope = new ForceEnvelope(
            TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(80),
            AttackLevel: 0.6, SustainLevel: 0.3);

        var rendered = Render(Values(SampleWallClock, "PlayEffectCommand", "atmo-entry-v1",
            finalIntensity: 0.22, envelope: envelope, activeCount: 1, showEnvelopeDetail: true));

        Assert.Equal(
            "14:35:12.250  PlayEffectCommand  atmo-entry-v1  i=0.22  active=1" +
            "  env A=20ms/H=10ms/D=40ms/R=80ms AL=0.6 SL=0.3",
            rendered);
    }

    [Fact]
    public void ToggleOnNullEnvelopeAppendsNoneSentinel()
    {
        // Stop / StopAll / StopAllAsync rows surface as env=(none) when the detail toggle is on.
        var rendered = Render(Values(SampleWallClock, "StopAllCommand", effectId: null,
            finalIntensity: null, envelope: null, activeCount: 0, showEnvelopeDetail: true));

        Assert.Equal(
            "14:35:12.250  StopAllCommand    i=  active=0  env=(none)",
            rendered);
    }

    [Fact]
    public void ToggleOnBoundaryEnvelopeRendersZerosCleanly()
    {
        // Atmosphere-entered case from T-28 uses AttackLevel=0.5, SustainLevel=1.0 (inverted shape).
        // Bumped here to the boundary (AttackLevel=0, Release=TimeSpan.Zero) to validate the
        // numeric formatting doesn't leak culture-specific or extraneous decimals.
        var envelope = new ForceEnvelope(
            TimeSpan.FromMilliseconds(15), TimeSpan.Zero, TimeSpan.FromMilliseconds(30), TimeSpan.Zero,
            AttackLevel: 0, SustainLevel: 1.0);

        var rendered = Render(Values(SampleWallClock, "PlayEffectCommand", "atmo-entry-v1",
            finalIntensity: 0.22, envelope: envelope, activeCount: 1, showEnvelopeDetail: true));

        Assert.Equal(
            "14:35:12.250  PlayEffectCommand  atmo-entry-v1  i=0.22  active=1" +
            "  env A=15ms/H=0ms/D=30ms/R=0ms AL=0 SL=1",
            rendered);
    }

    [Fact]
    public void ConvertWithWrongArityReturnsEmptyString()
    {
        // XAML can briefly invoke the converter with a shorter array during binding initialization;
        // the converter must yield empty rather than throw so WPF doesn't raise a binding error.
        var converter = new PreviewRowFormatConverter();
        var result = converter.Convert(values: new object?[] { SampleWallClock, "x" },
            typeof(string), parameter: null, CultureInfo.InvariantCulture);

        Assert.Equal(string.Empty, result);
    }
}
