namespace Moza.ScLink.Core.Devices;

/// <summary>
/// Marks chain-composition layers that aggregate per-tier transitions into a single
/// <see cref="ChainStateChangedEventArgs"/> stream. T-07 Issue #27 Pass-2 G3-Interpretation-B
/// decoupling: lets <c>ForceFeedbackController</c> in the Effects assembly re-expose chain
/// transitions without naming the concrete chain implementation (currently
/// <c>FallbackForceFeedbackDevice</c> in the DirectInput assembly; Phase-2 channels-pipeline
/// will introduce a successor).
/// </summary>
/// <remarks>
/// Mirrors the B1 <see cref="IDeviceAvailabilityObserver"/> decoupling pattern: cross-layer
/// reach goes through a Core interface, not a downstream concrete type. Effects.csproj
/// references only Core; this interface lets it stay that way while still re-exposing chain
/// transitions to consumers. Subscribers receive <see cref="ChainStateChangedEventArgs"/> for
/// every selection-change transition (init success, fall-through, hot-arrival, hot-removal).
/// </remarks>
public interface IChainStateChangedSource
{
    /// <summary>
    /// Raised on every transition of the chain's currently-selected tier.
    /// </summary>
    event EventHandler<ChainStateChangedEventArgs>? ChainStateChanged;
}
