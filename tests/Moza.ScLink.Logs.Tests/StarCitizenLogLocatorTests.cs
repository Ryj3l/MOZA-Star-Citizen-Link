using System.IO;
using FluentAssertions;
using Moza.ScLink.Logs;

namespace Moza.ScLink.Logs.Tests;

public sealed class StarCitizenLogLocatorTests : IDisposable
{
    private const string EnvVar = "STAR_CITIZEN_GAME_LOG";
    private readonly string? _originalEnv = Environment.GetEnvironmentVariable(EnvVar);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"moza-sclog-{Guid.NewGuid():N}.log");

    [Fact]
    public void FindGameLogHonorsStarCitizenGameLogEnvironmentOverride()
    {
        File.WriteAllText(_path, "a log line\n");
        Environment.SetEnvironmentVariable(EnvVar, _path);

        var found = StarCitizenLogLocator.FindGameLog();

        // The explicit override returns the readable file's full path before any drive scan.
        found.Should().Be(Path.GetFullPath(_path));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, _originalEnv);   // restore process-global state
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
        }
    }
}
