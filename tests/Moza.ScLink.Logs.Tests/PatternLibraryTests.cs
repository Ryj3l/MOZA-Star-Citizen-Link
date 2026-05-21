using System.IO;
using FluentAssertions;
using Moza.ScLink.Logs.Parsing;

namespace Moza.ScLink.Logs.Tests;

public sealed class PatternLibraryTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"moza-patternlib-test-{Guid.NewGuid():N}.json");

    private static readonly TimeSpan FastDebounce = TimeSpan.FromMilliseconds(100);

    private static string ValidFile(string kind, string pattern) =>
        $$"""
        { "schemaVersion": 1, "patterns": [ { "kind": "{{kind}}", "name": "test", "pattern": "{{pattern}}", "intensity": 0.3, "durationMs": 0, "unsupported": false } ] }
        """;

    private static void Write(string path, string content)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(fs);
        writer.Write(content);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }
    }

    [Fact]
    public void HappyPathLoadsAndParses()
    {
        Write(_path, ValidFile("AtmosphereEntered", "alpha-token"));
        using var library = new PatternLibrary(_path, debounceWindow: FastDebounce);

        library.Current.Parse("alpha-token here").Should().NotBeNull();
    }

    [Fact]
    public void SchemaVersionMismatchOnInitialLoadYieldsEmptySet()
    {
        Write(_path, """{ "schemaVersion": 2, "patterns": [ { "kind": "AtmosphereEntered", "name": "x", "pattern": "alpha-token", "intensity": 0.3, "durationMs": 0 } ] }""");
        using var library = new PatternLibrary(_path, debounceWindow: FastDebounce);

        library.Current.PatternCount.Should().Be(0);                 // "load defaults" posture
        library.Current.Parse("alpha-token here").Should().BeNull();
    }

    [Fact]
    public async Task MalformedReloadRetainsPriorAndDoesNotSignalChange()
    {
        Write(_path, ValidFile("AtmosphereEntered", "alpha-token"));
        using var library = new PatternLibrary(_path, debounceWindow: FastDebounce);
        var changedCount = 0;
        library.Changed += (_, _) => Interlocked.Increment(ref changedCount);

        library.Current.Parse("alpha-token here").Should().NotBeNull();  // original works

        Write(_path, "{ this is not valid json");
        await Task.Delay(400);  // debounce window + reload attempt

        library.Current.Parse("alpha-token here").Should().NotBeNull();  // (a) prior retained, not degraded
        changedCount.Should().Be(0);                                     // (b) no misleading change signal
    }

    [Fact]
    public async Task HotReloadPicksUpChangesWithinOneSecond()
    {
        Write(_path, ValidFile("AtmosphereEntered", "alpha-token"));
        using var library = new PatternLibrary(_path, debounceWindow: FastDebounce);
        library.Current.Parse("beta-token here").Should().BeNull();  // beta is not a pattern yet

        Write(_path, ValidFile("AtmosphereExited", "beta-token"));
        await WaitUntilAsync(() => library.Current.Parse("beta-token here") is not null, TimeSpan.FromSeconds(1));

        library.Current.Parse("beta-token here").Should().NotBeNull();  // new pattern is live
    }

    [Fact]
    public async Task RapidWritesDebounceToASingleReload()
    {
        Write(_path, ValidFile("AtmosphereEntered", "alpha-token"));
        using var library = new PatternLibrary(_path, debounceWindow: FastDebounce);
        var reloadCount = 0;
        library.Changed += (_, _) => Interlocked.Increment(ref reloadCount);

        // Five rapid rewrites within the debounce window must coalesce into exactly one reload.
        for (var i = 0; i < 5; i++)
        {
            Write(_path, ValidFile("AtmosphereExited", "beta-token"));
        }

        await WaitUntilAsync(() => reloadCount >= 1, TimeSpan.FromSeconds(1));
        await Task.Delay(300);  // let any trailing (coalesced-away) reload settle

        reloadCount.Should().Be(1);  // coalesced, not 5
    }

    public void Dispose()
    {
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
