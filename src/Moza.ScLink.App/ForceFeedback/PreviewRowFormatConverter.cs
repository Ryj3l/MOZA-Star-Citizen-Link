using System.Globalization;
using System.Windows.Data;
using Moza.ScLink.Core.Effects;

namespace Moza.ScLink.App.ForceFeedback;

/// <summary>
/// XAML row formatter for the Preview Output list (#76). Formats each <see cref="PreviewedCommand"/>
/// into the compact 5-field row (timestamp / CommandType / EffectId / intensity / active-count); when
/// the per-window <c>ShowPreviewEnvelopeDetail</c> toggle is on, appends an inline
/// <c>env A=…/H=…/D=…/R=… AL=… SL=…</c> clause sourced from <see cref="PreviewedCommand.Envelope"/>
/// (or <c>env=(none)</c> when the command carries no envelope). One-way binding only.
/// </summary>
/// <remarks>
/// MultiBinding inputs (arity 7, fixed order; tested via Convert directly):
/// <list type="number">
/// <item><description>0: <see cref="DateTime"/> WallClock.LocalDateTime</description></item>
/// <item><description>1: <see cref="string"/> CommandType</description></item>
/// <item><description>2: <see cref="string"/>? EffectId</description></item>
/// <item><description>3: <see cref="double"/>? FinalIntensity</description></item>
/// <item><description>4: <see cref="ForceEnvelope"/>? Envelope</description></item>
/// <item><description>5: <see cref="int"/> ActiveCount</description></item>
/// <item><description>6: <see cref="bool"/> ShowPreviewEnvelopeDetail (window DataContext via RelativeSource)</description></item>
/// </list>
/// All numeric and timestamp formatting uses <see cref="CultureInfo.InvariantCulture"/> so the rendered
/// row is deterministic in tests regardless of the developer's locale.
/// </remarks>
internal sealed class PreviewRowFormatConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        // XAML can call the converter with a shorter array during initialization — return empty
        // rather than throw so the binding system can settle without raising a binding error.
        if (values.Length != 7)
        {
            return string.Empty;
        }

        var inv = CultureInfo.InvariantCulture;
        var wallClock = values[0] is DateTime dt ? dt : default;
        var commandType = values[1] as string ?? string.Empty;
        var effectId = values[2] as string;
        var finalIntensity = values[3] as double?;
        var envelope = values[4] as ForceEnvelope;
        var activeCount = values[5] is int ac ? ac : 0;
        var showEnvelopeDetail = values[6] is bool show && show;

        var compact = string.Format(
            inv,
            "{0:HH:mm:ss.fff}  {1}  {2}  i={3}  active={4}",
            wallClock,
            commandType,
            effectId,
            finalIntensity?.ToString("0.##", inv) ?? string.Empty,
            activeCount);

        if (!showEnvelopeDetail)
        {
            return compact;
        }

        if (envelope is null)
        {
            return compact + "  env=(none)";
        }

        var envelopeClause = string.Format(
            inv,
            "  env A={0:0}ms/H={1:0}ms/D={2:0}ms/R={3:0}ms AL={4:0.##} SL={5:0.##}",
            envelope.Attack.TotalMilliseconds,
            envelope.Hold.TotalMilliseconds,
            envelope.Decay.TotalMilliseconds,
            envelope.Release.TotalMilliseconds,
            envelope.AttackLevel,
            envelope.SustainLevel);

        return compact + envelopeClause;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("PreviewRowFormatConverter is one-way only.");
}
