using System.IO;
using FluentAssertions;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Sensors;
using Moza.ScLink.Logs.Parsing;

namespace Moza.ScLink.Logs.Tests;

public sealed class LogSensorTests : IDisposable
{
    private static readonly DateTimeOffset FixedTs = new(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly List<PatternLibrary> _libraries = [];
    private readonly List<string> _tempPaths = [];

    private static ScGameEvent ALandingImpact(string sourceLine) =>
        new(ScEventKind.LandingImpact, "Landing", 0.5, TimeSpan.FromMilliseconds(260), sourceLine, FixedTs);

    private static SensorEvent ASensorEvent() => new()
    {
        EventId = Guid.NewGuid().ToString(),
        SensorId = LogSensor.LogSensorId,
        SensorKind = SensorKind.Log,
        EventType = "log.test",
        Timestamp = FixedTs,
    };

    private string NewTempPath(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"moza-logsensor-{Guid.NewGuid():N}.{extension}");
        _tempPaths.Add(path);
        return path;
    }

    private static void WriteShared(string path, string content)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(fs);
        writer.Write(content);
    }

    private static void AppendShared(string path, string content)
    {
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(fs);
        writer.Write(content);
    }

    private PatternLibrary EmptyLibrary()
    {
        var library = new PatternLibrary(NewTempPath("json"));  // nonexistent file -> empty parser
        _libraries.Add(library);
        return library;
    }

    private PatternLibrary LibraryMatching(string kind, string pattern)
    {
        var path = NewTempPath("json");
        WriteShared(path, $$"""{ "schemaVersion": 1, "patterns": [ { "kind": "{{kind}}", "name": "x", "pattern": "{{pattern}}", "intensity": 0.3, "durationMs": 0, "unsupported": false } ] }""");
        var library = new PatternLibrary(path);
        _libraries.Add(library);
        return library;
    }

    [Fact]
    public void EmitWritesToBusAndCountsEmitted()
    {
        var bus = new EventBus();
        var sensor = new LogSensor(bus, EmptyLibrary(), "nonexistent.log");
        var evt = ASensorEvent();

        sensor.Emit(evt);

        bus.SensorEventReader.TryRead(out var read).Should().BeTrue();
        read.Should().Be(evt);
        sensor.Health.EventsEmitted.Should().Be(1);
    }

    [Theory]
    [InlineData(ScEventKind.QuantumSpoolStarted, "log.quantum_spool_start")]
    [InlineData(ScEventKind.QuantumSpoolEnded, "log.quantum_spool_end")]
    [InlineData(ScEventKind.LandingImpact, "log.landing_impact_candidate")]
    [InlineData(ScEventKind.AtmosphereEntered, "log.atmosphere_entered")]
    [InlineData(ScEventKind.AtmosphereExited, "log.atmosphere_exited")]
    public void ToSensorEventMapsEventType(ScEventKind kind, string expected)
    {
        var gameEvent = new ScGameEvent(kind, "x", 0.4, TimeSpan.Zero, "line", FixedTs);

        LogSensor.ToSensorEvent(gameEvent).EventType.Should().Be(expected);
    }

    [Fact]
    public void ToSensorEventPopulatesRelativeVelocityMagnitudeForLandingImpact()
    {
        // (3,4,12) -> 3D norm 13; asserts the exact value to distinguish "populated" from "wrong value".
        var sensorEvent = LogSensor.ToSensorEvent(ALandingImpact("FatalCollision Relative Vel: x:3, y:4, z:12"));

        sensorEvent.Features.Should().ContainKey("relativeVelocityMagnitude");
        sensorEvent.Features["relativeVelocityMagnitude"].Should().BeApproximately(13.0, 1e-9);
    }

    [Fact]
    public async Task LifecycleTransitionsAndIsIdempotent()
    {
        var bus = new EventBus();
        var sensor = new LogSensor(bus, EmptyLibrary(), "nonexistent.log");

        await sensor.StartAsync(CancellationToken.None);
        sensor.State.Should().Be(SensorState.Running);
        await sensor.StartAsync(CancellationToken.None);   // idempotent
        sensor.State.Should().Be(SensorState.Running);

        await sensor.StopAsync(CancellationToken.None);
        sensor.State.Should().Be(SensorState.Stopped);
        await sensor.StopAsync(CancellationToken.None);     // idempotent
        sensor.State.Should().Be(SensorState.Stopped);

        await sensor.DisposeAsync();
    }

    [Fact]
    public async Task FileIntegrationPublishesParsedEventToBus()
    {
        var logPath = NewTempPath("log");
        WriteShared(logPath, string.Empty);
        var bus = new EventBus();
        var sensor = new LogSensor(bus, LibraryMatching("AtmosphereEntered", "atmosphere entered"), logPath);

        await sensor.StartAsync(CancellationToken.None);
        await Task.Delay(300);  // let the start position settle before appending (matches GameLogTailer tests)
        AppendShared(logPath, "atmosphere entered now\n");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && bus.Metrics.SensorEvents.Published == 0)
        {
            await Task.Delay(25);
        }

        await sensor.StopAsync(CancellationToken.None);

        bus.SensorEventReader.TryRead(out var read).Should().BeTrue();
        read!.EventType.Should().Be("log.atmosphere_entered");
    }

    [Fact]
    public async Task MulticastEnumerationsAreIndependent()
    {
        var bus = new EventBus();
        await using var sensor = new LogSensor(bus, EmptyLibrary(), "nonexistent.log");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var a = new List<SensorEvent>();
        var b = new List<SensorEvent>();
        var taskA = Task.Run(async () =>
        {
            await foreach (var e in sensor.ReadEventsAsync(cts.Token))
            {
                a.Add(e);
                if (a.Count == 3) { break; }
            }
        });
        var taskB = Task.Run(async () =>
        {
            await foreach (var e in sensor.ReadEventsAsync(cts.Token))
            {
                b.Add(e);
                if (b.Count == 3) { break; }
            }
        });
        await Task.Delay(150);  // let both subscribers register before the first Emit

        for (var i = 0; i < 3; i++)
        {
            sensor.Emit(ASensorEvent());
        }

        await Task.WhenAll(taskA, taskB).WaitAsync(TimeSpan.FromSeconds(5));

        a.Should().HaveCount(3);   // each enumeration receives EVERY event (multicast, not distribute-to-one)
        b.Should().HaveCount(3);
    }

    [Fact]
    public async Task SlowConsumerEventsAreDroppedAfterHundredBehind()
    {
        var bus = new EventBus();
        await using var sensor = new LogSensor(bus, EmptyLibrary(), "nonexistent.log");

        var received = 0;
        var consume = Task.Run(async () =>
        {
            await foreach (var _ in sensor.ReadEventsAsync(CancellationToken.None))
            {
                if (received == 0) { await Task.Delay(500); }  // pause so the buffer overflows behind us
                received++;
            }
        });
        await Task.Delay(150);  // subscriber registered

        for (var i = 0; i < 150; i++)
        {
            sensor.Emit(ASensorEvent());
        }

        await sensor.DisposeAsync();  // completes the subscriber channel so the drain terminates
        await consume.WaitAsync(TimeSpan.FromSeconds(5));

        received.Should().BeLessThan(150);  // drops occurred (cap 100 + DropOldest) — observed via data loss
    }

    [Fact]
    public async Task DropsIncrementHealthDroppedEvents()
    {
        var bus = new EventBus();
        await using var sensor = new LogSensor(bus, EmptyLibrary(), "nonexistent.log");
        using var cts = new CancellationTokenSource();

        var enumerator = sensor.ReadEventsAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        var pending = enumerator.MoveNextAsync();  // triggers registration; then we never drain
        await Task.Delay(150);

        for (var i = 0; i < 150; i++)
        {
            sensor.Emit(ASensorEvent());
        }

        await Task.Delay(100);  // let drops settle

        sensor.Health.DroppedEvents.Should().BeGreaterThan(0);  // the counter reflects the drops

        cts.Cancel();
        try
        {
            await pending;
        }
        catch (OperationCanceledException)
        {
        }

        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task StopAsyncCompletesPromptlyForRunningTailer()
    {
        // #73 baseline: the tailer is actively iterating (open + read + 250ms sleep) over a real file.
        // Asserts StopAsync drains within a tight budget — production >5s was environmental
        // (AV scan / slow disk / file lock), the code-mechanics path is clean.
        var logPath = NewTempPath("log");
        WriteShared(logPath, "seed-1\nseed-2\n");
        var sensor = new LogSensor(new EventBus(), EmptyLibrary(), logPath);

        await sensor.StartAsync(CancellationToken.None);
        await Task.Delay(600);  // straddle ~2 iterations

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await sensor.StopAsync(CancellationToken.None);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(500, "stop must complete within the host's 5s budget with headroom");
        sensor.State.Should().Be(SensorState.Stopped);

        await sensor.DisposeAsync();
    }

    [Fact]
    public async Task StopAsyncCompletesPromptlyForIdleTailer()
    {
        // #73 baseline: the tailer is sitting in the file-not-exists 1000ms wait branch.
        // Cancellation must observe within the wait, not require its natural completion.
        var sensor = new LogSensor(new EventBus(), EmptyLibrary(), NewTempPath("log"));  // never created
        await sensor.StartAsync(CancellationToken.None);
        await Task.Delay(200);  // partway through the 1000ms wait

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await sensor.StopAsync(CancellationToken.None);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(500);
        sensor.State.Should().Be(SensorState.Stopped);

        await sensor.DisposeAsync();
    }

    [Fact]
    public async Task StopAsyncDoesNotThrowWhenCallerBudgetExceeded()
    {
        // #73 contract: ISensor.StopAsync MUST NOT throw to signal shutdown timeout. A pre-cancelled
        // CT simulates the slow-drain path (caller's deadline elapsed before drain) without needing
        // an environmental repro (AV scan / slow disk). The tailer's worker continues to drain in
        // the background; LogSensor soft-logs and transitions to Stopped without throwing.
        var logPath = NewTempPath("log");
        WriteShared(logPath, "seed\n");
        var sensor = new LogSensor(new EventBus(), EmptyLibrary(), logPath);

        await sensor.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        using var preCancelled = new CancellationTokenSource();
        await preCancelled.CancelAsync();

        var stop = async () => await sensor.StopAsync(preCancelled.Token);

        await stop.Should().NotThrowAsync("the contract is best-effort drain — host owns the outer deadline");
        sensor.State.Should().Be(SensorState.Stopped);

        await sensor.DisposeAsync();
    }

    public void Dispose()
    {
        foreach (var library in _libraries)
        {
            library.Dispose();
        }

        foreach (var path in _tempPaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
