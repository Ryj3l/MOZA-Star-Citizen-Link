namespace Moza.ScLink.Core.Diagnostics;

public interface IClock
{
    public DateTimeOffset UtcNow { get; }
}
