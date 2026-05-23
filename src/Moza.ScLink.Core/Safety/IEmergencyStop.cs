namespace Moza.ScLink.Core.Safety;

/// <summary>
/// Authoritative emergency-stop state for force-feedback output (PRP §5.8).
/// Activation must result in all active effects halted within
/// <see cref="SafetyLimits.EmergencyStopMaxLatencyMs"/> (50 ms).
/// This is a cross-layer contract: the Effects-layer output worker
/// (<c>ForceCommandPipeline</c>) consumes it; the App-layer hotkey and UI (T-16 PR2) drive it.
/// </summary>
public interface IEmergencyStop
{
    /// <summary>True while emergency stop is engaged; force-feedback playback is refused until cleared.</summary>
    bool IsActive { get; }

    /// <summary>Raised on the transition from inactive to active.</summary>
    event EventHandler<EmergencyStopActivatedEventArgs>? Activated;

    /// <summary>Raised on the transition from active to inactive.</summary>
    event EventHandler? Cleared;

    /// <summary>Engages emergency stop. Idempotent: a call while already active is a no-op.</summary>
    /// <param name="reason">Human-readable activation source, e.g. "hotkey" or "ui".</param>
    /// <param name="ct">Cancellation token.</param>
    Task ActivateAsync(string reason, CancellationToken ct = default);

    /// <summary>Clears emergency stop, allowing normal force-feedback playback to resume.</summary>
    /// <param name="ct">Cancellation token.</param>
    Task ClearAsync(CancellationToken ct = default);
}
