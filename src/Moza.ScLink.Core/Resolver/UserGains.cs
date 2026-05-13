using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Safety;

namespace Moza.ScLink.Core.Resolver;

/// <summary>User-configured gain settings applied across the gain stack per PRP §5.9.</summary>
public sealed record UserGains
{
    /// <summary>Master output gain applied to all effects. Defaults to <see cref="SafetyLimits.DefaultMasterGain"/> (0.6).</summary>
    public double MasterGain { get; init; } = SafetyLimits.DefaultMasterGain;

    /// <summary>
    /// Per-category gain multipliers keyed by <see cref="EffectCategory"/>.
    /// The resolver uses <c>GetValueOrDefault(category, 1.0)</c> — missing keys default to 1.0.
    /// </summary>
    public ImmutableDictionary<EffectCategory, double> CategoryGains { get; init; }
        = ImmutableDictionary<EffectCategory, double>.Empty;

    /// <summary>
    /// Per-device calibration multipliers keyed by <see cref="DeviceModel"/>.
    /// Allows independent calibration of AB6 and AB9 in a multi-device setup.
    /// The resolver uses <c>GetValueOrDefault(deviceModel, 1.0)</c> — missing keys default to 1.0.
    /// </summary>
    public ImmutableDictionary<DeviceModel, double> DeviceGainMultipliers { get; init; }
        = ImmutableDictionary<DeviceModel, double>.Empty;
}
