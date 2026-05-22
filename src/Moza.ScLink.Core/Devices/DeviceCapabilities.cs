using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Core.Devices;

/// <summary>Immutable capabilities reported by a connected force-feedback device at initialization.</summary>
/// <param name="Model">The classified MOZA hardware model; the gain stack keys the per-device multiplier on this.</param>
/// <param name="AxisCount">Number of force-feedback axes the device exposes.</param>
/// <param name="SimultaneousEffectCount">Maximum number of effects the device can play simultaneously.</param>
/// <param name="SupportsConstantForce">Whether the device supports constant-force DirectInput effects.</param>
/// <param name="SupportsPeriodic">Whether the device supports periodic DirectInput effects.</param>
/// <param name="SupportsEnvelope">Whether the device supports ADSR envelope parameters.</param>
/// <param name="MaxGain">Maximum gain value reported by DirectInput (typically 10000).</param>
/// <param name="MaxIntensityRecommended">Device-specific safety ceiling, e.g. 0.85 for AB6. Used as the final clamp in the gain stack.</param>
public sealed record DeviceCapabilities(
    DeviceModel Model,
    int AxisCount,
    int SimultaneousEffectCount,
    bool SupportsConstantForce,
    bool SupportsPeriodic,
    bool SupportsEnvelope,
    int MaxGain,
    double MaxIntensityRecommended);
