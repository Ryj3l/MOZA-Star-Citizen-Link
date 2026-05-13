namespace Moza.ScLink.Core.Diagnostics;

/// <summary>Deletes log files older than the configured retention window.</summary>
public sealed class LogRetentionWorker
{
    private readonly IClock _clock;
    private readonly IFileSystem _fs;
    private readonly int _retentionDays;

    /// <summary>Initializes a new <see cref="LogRetentionWorker"/> with the specified dependencies.</summary>
    public LogRetentionWorker(IClock clock, IFileSystem fs, int retentionDays)
    {
        _clock = clock;
        _fs = fs;
        _retentionDays = retentionDays;
    }

    /// <summary>Deletes log files in <paramref name="logsDir"/> older than the retention window. Returns the count deleted.</summary>
    public int EnforceRetention(DirectoryInfo logsDir) =>
        throw new NotImplementedException("Retention worker body lands in a later task.");
}
