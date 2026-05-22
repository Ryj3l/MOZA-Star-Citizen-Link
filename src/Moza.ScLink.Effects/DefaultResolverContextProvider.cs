using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Resolver;
using Moza.ScLink.Core.Safety;

namespace Moza.ScLink.Effects;

/// <summary>
/// Phase-1 default <see cref="IResolverContextProvider"/>: an empty (all-ones) ship profile, default
/// <see cref="UserGains"/>, and a placeholder <see cref="DeviceCapabilities"/> whose
/// <see cref="DeviceModel.Unknown"/> model makes it identifiable as a placeholder in any output until the
/// #43/T-16 convergence supplies the live device. The placeholder ceiling is
/// <see cref="SafetyLimits.MaxIntensity"/> — refusing to drive an unknown device is the allowlist's job
/// (T-08) at the output worker, not this ceiling, so the gain-stack math stays meaningful here.
/// </summary>
public sealed class DefaultResolverContextProvider : IResolverContextProvider
{
    private static readonly ShipProfile DefaultShipProfile =
        new() { ShipId = "default", DisplayName = "Default" };

    private static readonly DeviceCapabilities PlaceholderDevice =
        new(DeviceModel.Unknown, AxisCount: 0, SimultaneousEffectCount: 0,
            SupportsConstantForce: false, SupportsPeriodic: false, SupportsEnvelope: false,
            MaxGain: 0, MaxIntensityRecommended: SafetyLimits.MaxIntensity);

    /// <inheritdoc />
    public ResolverContext GetContext() =>
        new(DefaultShipProfile, new UserGains(), PlaceholderDevice, DateTimeOffset.UtcNow);
}
