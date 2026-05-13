using Moza.ScLink.Core.Devices;

namespace Moza.ScLink.Core.Resolver;

/// <summary>Immutable snapshot of all context the effect resolver needs to apply the gain stack per PRP §5.9.</summary>
/// <param name="ActiveShipProfile">The active ship profile providing per-effect multipliers.</param>
/// <param name="UserGains">The user's master, category, and per-device gain settings.</param>
/// <param name="DeviceCapabilities">Capabilities of the target output device, including the safety clamp.</param>
/// <param name="Now">The current time, used for rate-of-change and sustain-cap enforcement.</param>
public sealed record ResolverContext(
    ShipProfile ActiveShipProfile,
    UserGains UserGains,
    DeviceCapabilities DeviceCapabilities,
    DateTimeOffset Now);
