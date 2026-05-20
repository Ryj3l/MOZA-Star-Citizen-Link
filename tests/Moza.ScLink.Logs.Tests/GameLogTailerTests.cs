using System.Collections.Concurrent;
using System.IO;
using System.Text;
using FluentAssertions;

namespace Moza.ScLink.Logs.Tests;

/// <summary>
/// Characterization tests pinning GameLogTailer's PRP §14.2 preserve-behaviors against the CURRENT
/// implementation, before the T-11 ISensor refactor. Temp-file integration tests via the public API.
/// </summary>
public sealed class GameLogTailerTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"moza-tailer-test-{Guid.NewGuid():N}.log");

    private static void WriteShared(string path, string content)
    {
        using var fs = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(fs, Encoding.UTF8);
        writer.Write(content);
    }

    private static void AppendShared(string path, string content)
    {
        using var fs = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(fs, Encoding.UTF8);
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

            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task DetectsTruncationAndRestartsFromBeginning()
    {
        WriteShared(_path, "line-A\nline-B\nline-C\n");
        var lines = new ConcurrentQueue<string>();
        using var tailer = new GameLogTailer(_path);
        tailer.LineRead += (_, line) => lines.Enqueue(line);

        // startAtEnd:false reads from byte 0 — pick up the seeded lines, advancing the read position.
        tailer.Start(startAtEnd: false);
        await WaitUntilAsync(() => lines.Contains("line-C"), TimeSpan.FromSeconds(5));

        // Replace with strictly shorter content: new length < current read position triggers the
        // truncation branch (position > length -> reset to 0 -> re-read from the beginning).
        WriteShared(_path, "after-truncation\n");
        await WaitUntilAsync(() => lines.Contains("after-truncation"), TimeSpan.FromSeconds(5));

        await tailer.StopAsync();

        lines.Should().Contain("line-C");
        lines.Should().Contain("after-truncation");
    }

    [Fact]
    public async Task ResumesFromLastPositionWhenFileGrows()
    {
        WriteShared(_path, "line-1\nline-2\n");
        var lines = new ConcurrentQueue<string>();
        using var tailer = new GameLogTailer(_path);
        tailer.LineRead += (_, line) => lines.Enqueue(line);

        tailer.Start(startAtEnd: false);
        await WaitUntilAsync(() => lines.Contains("line-2"), TimeSpan.FromSeconds(5));

        // Append (grow): file length now exceeds the read position. The tailer seeks to the prior
        // position and reads only the appended line — it does NOT reset to 0 or re-read line-1/line-2.
        AppendShared(_path, "line-3\n");
        await WaitUntilAsync(() => lines.Contains("line-3"), TimeSpan.FromSeconds(5));

        await tailer.StopAsync();

        lines.Count(l => l == "line-1").Should().Be(1);  // read once, not re-read on resume
        lines.Count(l => l == "line-3").Should().Be(1);
    }

    [Fact]
    public async Task ToleratesFileDeletionAndRecoversOnRotation()
    {
        WriteShared(_path, "before-rotation\n");
        var lines = new ConcurrentQueue<string>();
        using var tailer = new GameLogTailer(_path);
        tailer.LineRead += (_, line) => lines.Enqueue(line);

        tailer.Start(startAtEnd: false);
        await WaitUntilAsync(() => lines.Contains("before-rotation"), TimeSpan.FromSeconds(5));

        // Delete out from under the running tailer (Star Citizen rotates logs by delete+create).
        // The tailer must not hold a lock that blocks deletion.
        var deleteThrew = false;
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            deleteThrew = true;
        }

        await Task.Delay(400);  // let a poll observe the missing file (not-found -> wait branch)

        // Recreate a fresh, strictly-shorter file (rotation). Recovery routes through the truncation
        // reset (new length < pre-delete position), so the tailer re-reads from the start.
        WriteShared(_path, "rotated\n");
        await WaitUntilAsync(() => lines.Contains("rotated"), TimeSpan.FromSeconds(5));

        await tailer.StopAsync();

        deleteThrew.Should().BeFalse();      // tailer did not lock the file against deletion
        lines.Should().Contain("rotated");   // recovered and resumed reading after rotation
    }

    [Fact]
    public async Task SeekToEndOnStartupSkipsExistingContent()
    {
        WriteShared(_path, "seed-1\nseed-2\n");
        var lines = new ConcurrentQueue<string>();
        using var tailer = new GameLogTailer(_path);
        tailer.LineRead += (_, line) => lines.Enqueue(line);

        // startAtEnd:true captures position at the current EOF (top of ReadLoopAsync), so seed is skipped.
        tailer.Start(startAtEnd: true);
        await Task.Delay(500);  // let the start position settle at seed EOF BEFORE appending (avoids the
                                // race where an append captured into the start position would be skipped)

        AppendShared(_path, "appended-after-start\n");
        await WaitUntilAsync(() => lines.Contains("appended-after-start"), TimeSpan.FromSeconds(5));

        await tailer.StopAsync();

        lines.Should().Contain("appended-after-start");  // post-start content is read
        lines.Should().NotContain("seed-1");             // pre-existing content seek-skipped
        lines.Should().NotContain("seed-2");
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
