using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Safety;

namespace Moza.ScLink.Effects.Tests;

public sealed class ForceCommandPipelineTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task DiscardsPlayCommandsWhileActive()
    {
        var bus = new EventBus();
        var device = new RecordingForceFeedbackDevice();
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        var pipeline = new ForceCommandPipeline(bus, device, estop, NullLogger<ForceCommandPipeline>.Instance);

        await pipeline.StartAsync(CancellationToken.None);
        await estop.ActivateAsync("test");
        bus.ForceCommands.TryWrite(Play("alpha")).Should().BeTrue();
        bus.ForceCommands.TryWrite(Stop("alpha")).Should().BeTrue();
        // FIFO single-reader: once the trailing stop is executed, the leading play has already been processed.
        await WaitUntilAsync(() => device.ExecutedCommands.Any(c => c is StopEffectCommand), TimeSpan.FromSeconds(2));
        await pipeline.StopAsync(CancellationToken.None);

        device.ExecutedCommands.Should().NotContain(c => c is PlayEffectCommand);
        device.ExecutedCommands.Should().ContainSingle(c => c is StopEffectCommand);
    }

    [Fact]
    public async Task PassesStopCommandsThroughWhileActive()
    {
        var bus = new EventBus();
        var device = new RecordingForceFeedbackDevice();
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        var pipeline = new ForceCommandPipeline(bus, device, estop, NullLogger<ForceCommandPipeline>.Instance);

        await pipeline.StartAsync(CancellationToken.None);
        await estop.ActivateAsync("test");
        bus.ForceCommands.TryWrite(Stop("alpha")).Should().BeTrue();
        bus.ForceCommands.TryWrite(new StopAllCommand { CommandId = "all", IssuedAt = T0 }).Should().BeTrue();
        await WaitUntilAsync(() => device.ExecutedCommands.Count >= 2, TimeSpan.FromSeconds(2));
        await pipeline.StopAsync(CancellationToken.None);

        device.ExecutedCommands.Should().HaveCount(2);
        device.ExecutedCommands.Should().Contain(c => c is StopEffectCommand);
        device.ExecutedCommands.Should().Contain(c => c is StopAllCommand);
    }

    [Fact]
    public async Task ResumesNormalProcessingAfterClear()
    {
        var bus = new EventBus();
        var device = new RecordingForceFeedbackDevice();
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        var pipeline = new ForceCommandPipeline(bus, device, estop, NullLogger<ForceCommandPipeline>.Instance);

        await pipeline.StartAsync(CancellationToken.None);
        await estop.ActivateAsync("test");
        await estop.ClearAsync();
        bus.ForceCommands.TryWrite(Play("beta")).Should().BeTrue();
        await WaitUntilAsync(() => device.ExecutedCommands.Any(c => c is PlayEffectCommand), TimeSpan.FromSeconds(2));
        await pipeline.StopAsync(CancellationToken.None);

        device.ExecutedCommands.Should().ContainSingle()
            .Which.Should().BeOfType<PlayEffectCommand>()
            .Which.Effect.EffectId.Should().Be("beta");
    }

    [Fact]
    public async Task MeasuresLatencyUnder50msWithMockDevice()
    {
        // Fork 1 verification: the pipeline is IDLE (blocked on an empty channel) at every activation — the
        // state a per-command IsActive check could never service, and the reason the wake is CTS-driven.
        // Measures wall-clock from ActivateAsync to the device's StopAllAsync stamp over 100 iterations
        // (after warm-up) and asserts the 95th percentile is within the PRP §5.8 budget.
        const int warmup = 10;
        const int measured = 100;

        var bus = new EventBus();
        var device = new RecordingForceFeedbackDevice();
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        var pipeline = new ForceCommandPipeline(bus, device, estop, NullLogger<ForceCommandPipeline>.Instance);

        await pipeline.StartAsync(CancellationToken.None);

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

            // Reset for the next idle activation: clear, then round-trip a sentinel so the consume loop has
            // provably re-armed its wake trigger and returned to the blocked read before the next activation.
            // (Polling granularity does not affect the measurement — latency is the gap between two Stopwatch
            // stamps taken on the activation path, independent of when the test thread observes completion.)
            await estop.ClearAsync();
            var beforeExec = device.ExecutedCommands.Count;
            bus.ForceCommands.TryWrite(Stop("__latency_sentinel__")).Should().BeTrue();
            await WaitUntilAsync(() => device.ExecutedCommands.Count > beforeExec, TimeSpan.FromSeconds(5));
        }

        await pipeline.StopAsync(CancellationToken.None);

        // Every activation produced exactly one bypass StopAll — no missed wakes, no double-fires (Fork 1).
        device.StopAllCount.Should().Be(warmup + measured);

        samples.Should().HaveCount(measured);
        samples.Sort();
        var p95 = samples[(int)Math.Ceiling(0.95 * samples.Count) - 1];
        p95.Should().BeLessThan(
            SafetyLimits.EmergencyStopMaxLatencyMs,
            "PRP §5.8 budgets emergency stop at {0} ms (95th percentile); measured p95 was {1:F2} ms",
            SafetyLimits.EmergencyStopMaxLatencyMs,
            p95);
    }

    [Fact]
    public void DisposeReleasesResourcesAndIsIdempotent()
    {
        var bus = new EventBus();
        var device = new RecordingForceFeedbackDevice();
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        var pipeline = new ForceCommandPipeline(bus, device, estop, NullLogger<ForceCommandPipeline>.Instance);

        // Disposing without ever starting the host must release the wake-trigger CTS cleanly, and a second
        // Dispose must be a safe no-op (CancellationTokenSource.Dispose is idempotent).
        var dispose = () =>
        {
            pipeline.Dispose();
            pipeline.Dispose();
        };

        dispose.Should().NotThrow();
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

    private static PlayEffectCommand Play(string effectId) =>
        new(Effect(effectId), 0.5) { CommandId = Guid.NewGuid().ToString(), IssuedAt = T0 };

    private static StopEffectCommand Stop(string stateKey) =>
        new(stateKey) { CommandId = Guid.NewGuid().ToString(), IssuedAt = T0 };

    private static ForceEffect Effect(string id) =>
        new()
        {
            EffectId = id,
            EffectType = ForceEffectType.Periodic,
            Category = EffectCategory.Combat,
            BaseIntensity = 0.5,
            Duration = TimeSpan.FromSeconds(1),
        };
}
