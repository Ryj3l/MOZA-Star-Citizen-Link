namespace Moza.ScLink.Core.Diagnostics;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
