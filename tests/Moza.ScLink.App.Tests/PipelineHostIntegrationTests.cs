using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moza.ScLink.App.GameLog;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Safety;
using Moza.ScLink.Core.Sensors;
using Xunit;

namespace Moza.ScLink.App.Tests;

/// <summary>
/// T-27 E3 end-to-end integration tests. These build the REAL service graph via the internal
/// <see cref="Moza.ScLink.App.Program.ConfigureServices"/> (InternalsVisibleTo App.Tests) and start the
/// generic host — covering T-27's central risk, the DI registration + host-start lifecycle that a
/// hand-composed pipeline test would leave untested. The canonical device registration is overridden with
/// a recording double and the Game.log path with a test provider; everything else (FusionEngine,
/// EffectResolver, SafetyLimiter, ForceCommandPipeline, LogSensor, EmergencyStop, the default
/// pattern/rule/effect libraries) is the production registration.
/// <para>
/// Host build = minimal (T-27 E3 blessed): <see cref="Host.CreateDefaultBuilder(string[])"/> provides
/// ILogger + appsettings; we deliberately do NOT drive Program.CreateHostBuilder's Serilog-to-disk path —
/// the registration graph is the risk under test, not the logging config. Output is controlled purely by
/// the device override (no MOZA_SC_OUTPUT env var, which would be process-global and racy under xUnit).
/// Assertions use xUnit Assert to match the App.Tests convention (the other test projects use FluentAssertions).
/// </para>
/// </summary>
public sealed class PipelineHostIntegrationTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// Builds the real host from <see cref="Moza.ScLink.App.Program.ConfigureServices"/>, then replaces the
    /// canonical device and the Game.log path provider with the supplied test doubles (post-hoc
    /// <c>RemoveAll</c>/<c>AddSingleton</c> — Program is untouched).
    /// </summary>
    private static IHost BuildHost(IForceFeedbackDevice device, IGameLogPathProvider pathProvider) =>
        Host.CreateDefaultBuilder(Array.Empty<string>())
            .ConfigureServices((_, services) =>
            {
                Moza.ScLink.App.Program.ConfigureServices(services);

                services.RemoveAll<IForceFeedbackDevice>();
                services.AddSingleton(device);

                services.RemoveAll<IGameLogPathProvider>();
                services.AddSingleton(pathProvider);
            })
            .Build();

    /// <summary>Clean-machine path provider: resolves to no path, so the LogSensor idles (no Game.log).</summary>
    private static StubGameLogPathProvider CleanMachine() =>
        new() { StartupResolution = new GameLogPathResolution(null, GameLogPathOrigin.None) };

    private static StopEffectCommand Stop(string stateKey) =>
        new(stateKey) { CommandId = Guid.NewGuid().ToString(), IssuedAt = T0 };

    private static SensorEvent LogSensorEvent(string eventType) =>
        new()
        {
            EventId = Guid.NewGuid().ToString(),
            SensorId = "test.injection",
            SensorKind = SensorKind.Log,
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow,
            Intensity = 1.0,
            // No Confidence: the single-sensor log rules derive confidence from rule weights, so this
            // synthetic event behaves exactly like a real LogSensor.ToSensorEvent (which also omits it).
        };

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
    public async Task HostStartsAndStopsCleanlyOnCleanMachine()
    {
        // Acceptance: app starts on a clean Windows 11 machine without Star Citizen installed (no Game.log)
        // and without MOZA hardware (the recording double stands in for the canonical device). The full
        // hosted-service graph must start and stop without throwing — a throw fails the test.
        var device = new RecordingCanonicalDevice();
        using var host = BuildHost(device, CleanMachine());

        await host.StartAsync();
        await host.StopAsync();

        // DeviceInitializer (hosted, before the pipeline) initialized the device to Ready at host-start.
        Assert.Equal(1, device.InitializeCount);
        Assert.Equal(DeviceState.Ready, device.State);
    }

    [Fact]
    public async Task InjectedSensorEventReachesDeviceThroughAllFiveStages()
    {
        // Acceptance: the Channels bus runs end-to-end. A synthetic SensorEvent written to the bus must
        // traverse fusion -> resolver -> safety -> output worker -> device. quantum-spool-start has an
        // evidenceWindowMs:0 rule (no suppression) resolving to the sustained quantum-spool-v1 effect.
        var device = new RecordingCanonicalDevice();
        using var host = BuildHost(device, CleanMachine());
        await host.StartAsync();

        var bus = host.Services.GetRequiredService<IEventBus>();
        Assert.True(bus.SensorEvents.TryWrite(LogSensorEvent("log.quantum_spool_start")));

        await WaitUntilAsync(() => device.ExecutedSnapshot.Count >= 1, TimeSpan.FromSeconds(10));
        await host.StopAsync();

        // Exactly one path drives the device — one input must yield exactly one command (0 = pipeline broken,
        // 2 = a duplicate/second path).
        var command = Assert.Single(device.ExecutedSnapshot);
        var play = Assert.IsType<PlayEffectCommand>(command);
        Assert.Equal("quantum-spool-v1", play.Effect.EffectId);
    }

    [Fact]
    public async Task EmergencyStopP95Under50msThroughLiveHost()
    {
        // Acceptance: emergency stop halts all effects within 50 ms through the LIVE pipeline (the T-16 PR1
        // latency property holds end-to-end, not only in isolation). Same measurement shape as
        // ForceCommandPipelineTests.MeasuresLatencyUnder50msWithMockDevice, but exercising the REAL host
        // registration: resolve IEmergencyStop + IEventBus + the device from the started host.
        const int warmup = 10;
        const int measured = 100;

        var device = new RecordingCanonicalDevice();
        using var host = BuildHost(device, CleanMachine());
        await host.StartAsync();

        var bus = host.Services.GetRequiredService<IEventBus>();
        var estop = host.Services.GetRequiredService<IEmergencyStop>();

        var samples = new List<double>(measured);
        for (var i = 0; i < warmup + measured; i++)
        {
            var beforeStops = device.StopAllCount;
            var start = Stopwatch.GetTimestamp();
            await estop.ActivateAsync("latency");
            await WaitUntilAsync(() => device.StopAllCount > beforeStops, TimeSpan.FromSeconds(5));
            var elapsed = Stopwatch.GetElapsedTime(start, device.LastStopAllTimestamp);

            if (i >= warmup)
            {
                samples.Add(elapsed.TotalMilliseconds);
            }

            // Re-arm: clear, then round-trip a sentinel Stop so the pipeline's consume loop has provably
            // returned to its blocked read before the next idle activation (matches the unit latency test).
            await estop.ClearAsync();
            var beforeExec = device.ExecutedSnapshot.Count;
            Assert.True(bus.ForceCommands.TryWrite(Stop("__latency_sentinel__")));
            await WaitUntilAsync(() => device.ExecutedSnapshot.Count > beforeExec, TimeSpan.FromSeconds(5));
        }

        await host.StopAsync();

        // Every activation produced exactly one bypass StopAll — no missed wakes, no double-fires.
        Assert.Equal(warmup + measured, device.StopAllCount);
        Assert.Equal(measured, samples.Count);

        samples.Sort();
        var p95 = samples[(int)Math.Ceiling(0.95 * samples.Count) - 1];
        Assert.True(
            p95 < SafetyLimits.EmergencyStopMaxLatencyMs,
            $"PRP §5.8 budgets emergency stop at {SafetyLimits.EmergencyStopMaxLatencyMs} ms (95th percentile) " +
            $"end-to-end; measured p95 was {p95:F2} ms");
    }

    [Fact]
    public async Task TempGameLogDrivenThroughLogSensorReachesDevice()
    {
        // Acceptance: the bus runs end-to-end driven by the REAL LogSensor (not synthetic injection), and
        // §14.2-#2 seek-to-end holds live. The file is seeded (pre-start) with a LandingImpact line that the
        // sensor must skip; an AtmosphereEntered line appended after start must reach the device. Both lines
        // match v0.json's real regexes (LandingImpact requires the literal <FatalCollision> token).
        var tempPath = Path.Combine(Path.GetTempPath(), $"moza-e2e-gamelog-{Guid.NewGuid():N}.log");
        try
        {
            // Seeded BEFORE start: if seek-to-end fails, this would resolve to landing-contact-v1 — a
            // distinguishable command that the assertions below would catch.
            WriteShared(tempPath, "<FatalCollision> seed hull impact (pre-start, must be skipped)\n");

            var device = new RecordingCanonicalDevice();
            var pathProvider = new StubGameLogPathProvider
            {
                StartupResolution = new GameLogPathResolution(tempPath, GameLogPathOrigin.Saved),
            };
            using var host = BuildHost(device, pathProvider);
            await host.StartAsync();

            // Let the tailer's start position settle at the seed EOF before appending (avoids the race where
            // an append captured into the start position would itself be skipped). Mirrors GameLogTailerTests.
            await Task.Delay(500);
            AppendShared(tempPath, "atmosphere entered: aerodynamic buffet begins\n");

            await WaitUntilAsync(() => device.ExecutedSnapshot.Count >= 1, TimeSpan.FromSeconds(10));
            // Brief settle so a late-read seed line (the seek-to-end failure case) would surface as a 2nd command.
            await Task.Delay(500);
            await host.StopAsync();

            var commands = device.ExecutedSnapshot;
            // seek-to-end skips the seeded line and exactly one path drives the device.
            var command = Assert.Single(commands);
            var play = Assert.IsType<PlayEffectCommand>(command);
            Assert.Equal("atmosphere-entry-v1", play.Effect.EffectId);
            // The pre-start seed line must be skipped (§14.2-#2 seek-to-end): no landing effect ever appears.
            Assert.DoesNotContain(
                commands,
                c => c is PlayEffectCommand p && p.Effect.EffectId == "landing-contact-v1");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

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
}
