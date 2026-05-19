namespace Moza.ScLink.Core.Devices;

/// <summary>
/// Event arguments for the chain-composition layer's aggregate ChainStateChanged event (raised by
/// the FallbackForceFeedbackDevice in the DirectInput assembly). Carries the post-transition
/// device name and a coarse readiness flag.
/// </summary>
/// <remarks>
/// T-07 Issue #27 Pass-2 G3-Interpretation-B refinement: exposes the subscription point G3
/// specified ("for event subscription") at the chain layer rather than drilling the new
/// <see cref="IForceFeedbackDevice"/> through the chain to a property on
/// <c>ForceFeedbackController</c>. The chain raises this on every transition (init, fall-through,
/// hot-arrival, hot-removal) so subscribers do not have to re-subscribe on each chain re-selection.
/// </remarks>
public sealed class ChainStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Current device's display name (e.g. "Auto output (DirectInput: MOZA AB9 FFB Base)" or
    /// "Auto output (Preview output)").
    /// </summary>
    public required string OutputName { get; init; }

    /// <summary>Current device's human-readable status string.</summary>
    public required string OutputStatus { get; init; }

    /// <summary>
    /// True iff the active device is a real hardware output; false if it is the Null/Preview tier.
    /// </summary>
    public required bool IsReady { get; init; }
}
