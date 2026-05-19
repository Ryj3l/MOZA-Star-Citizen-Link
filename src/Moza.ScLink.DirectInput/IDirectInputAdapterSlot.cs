namespace Moza.ScLink.DirectInput;

/// <summary>
/// Marks force-feedback adapters that host a DirectInput device whose lifetime the chain
/// composition (<see cref="FallbackForceFeedbackDevice"/>) owns. Pass-2 hot-plug re-selection
/// identifies the chain's DI slot through this interface rather than naming the obsolete
/// concrete adapter type, and disposes the underlying device via <see cref="DisposeWrappedAsync"/>.
/// </summary>
/// <remarks>
/// Lifetime is bound to the transitional adapter's: this interface deletes alongside
/// <see cref="LegacyForceFeedbackDeviceAdapter"/> at T-10/T-14 per issue #15. The interface
/// exists as a Pass-2 chain decoupling — it lets the chain depend on the disposal capability
/// rather than the obsolete concrete type, with no codebase-first consumer-side
/// CS0618 suppression precedent. T-07 Issue #27 Pass-2 E-B3 disposition.
/// </remarks>
public interface IDirectInputAdapterSlot
{
    /// <summary>
    /// Disposes the underlying DirectInput device. The chain calls this on
    /// DBT_DEVICEREMOVECOMPLETE (hot-removal) and on double-arrival teardown-then-replace.
    /// Adapter owns the mechanics of reaching its wrapped field; chain owns the timing of the call.
    /// </summary>
    ValueTask DisposeWrappedAsync();
}
