namespace Moza.ScLink.Core.Effects;

/// <summary>Base record for all force-feedback commands. Carries a correlation ID for diagnostics and deduplication.</summary>
public abstract record ForceCommand
{
    /// <summary>Unique identifier for this command instance.</summary>
    public required string CommandId { get; init; }

    /// <summary>Time at which the command was issued.</summary>
    public required DateTimeOffset IssuedAt { get; init; }
}
