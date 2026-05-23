namespace Moza.ScLink.Core.Safety;

/// <summary>Event arguments carrying the reason for and time of an emergency-stop activation.</summary>
public sealed class EmergencyStopActivatedEventArgs : EventArgs
{
    /// <summary>Human-readable activation source, e.g. "hotkey" or "ui".</summary>
    public required string Reason { get; init; }

    /// <summary>Wall-clock time at which emergency stop was activated.</summary>
    public required DateTimeOffset ActivatedAt { get; init; }
}
