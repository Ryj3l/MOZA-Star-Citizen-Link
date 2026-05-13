namespace Moza.ScLink.Core.Diagnostics;

/// <summary>Abstraction over the system clock, enabling deterministic time in tests.</summary>
public interface IClock
{
    /// <summary>Returns the current UTC time.</summary>
    public DateTimeOffset UtcNow { get; }
}
