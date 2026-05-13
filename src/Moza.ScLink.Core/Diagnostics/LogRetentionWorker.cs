namespace Moza.ScLink.Core.Diagnostics;

public sealed class LogRetentionWorker
{
    private readonly IClock _clock;
    private readonly IFileSystem _fs;
    private readonly int _retentionDays;

    public LogRetentionWorker(IClock clock, IFileSystem fs, int retentionDays)
    {
        _clock = clock;
        _fs = fs;
        _retentionDays = retentionDays;
    }

    public int EnforceRetention(DirectoryInfo logsDir) =>
        throw new NotImplementedException("Retention worker body lands in a later task.");
}
