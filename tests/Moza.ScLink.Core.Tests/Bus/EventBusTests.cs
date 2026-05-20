using FluentAssertions;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Events;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Sensors;

namespace Moza.ScLink.Core.Tests.Bus;

public sealed class EventBusTests
{
    private static readonly DateTimeOffset FixedTs = new(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);

    private static SensorEvent ASensorEvent() => new()
    {
        EventId = Guid.NewGuid().ToString(),
        SensorId = "audio.endpoint-loopback",
        SensorKind = SensorKind.Audio,
        EventType = "audio.weapon_fire_ballistic",
        Timestamp = FixedTs,
    };

    private static GameEvent AGameEvent() => new()
    {
        EventId = Guid.NewGuid().ToString(),
        EventType = GameEventType.WeaponFireBallistic,
        Timestamp = FixedTs,
    };

    private static StopAllCommand AForceCommand() =>
        new() { CommandId = Guid.NewGuid().ToString(), IssuedAt = FixedTs };

    [Fact]
    public void SensorEventPublishConsumeRoundTrips()
    {
        var bus = new EventBus();
        var evt = ASensorEvent();

        bus.SensorEvents.TryWrite(evt).Should().BeTrue();
        bus.SensorEventReader.TryRead(out var read).Should().BeTrue();

        read.Should().Be(evt);
        bus.Metrics.SensorEvents.Published.Should().Be(1);
    }

    [Fact]
    public void GameEventPublishConsumeRoundTrips()
    {
        var bus = new EventBus();
        var evt = AGameEvent();

        bus.GameEvents.TryWrite(evt).Should().BeTrue();
        bus.GameEventReader.TryRead(out var read).Should().BeTrue();

        read.Should().Be(evt);
        bus.Metrics.GameEvents.Published.Should().Be(1);
    }

    [Fact]
    public async Task ForceCommandPublishConsumeRoundTripsViaWriteAsync()
    {
        var bus = new EventBus();
        var cmd = AForceCommand();

        await bus.ForceCommands.WriteAsync(cmd);
        var read = await bus.ForceCommandReader.ReadAsync();

        read.Should().Be(cmd);
        bus.Metrics.ForceCommands.Published.Should().Be(1);
    }

    [Fact]
    public void SensorEventDropOldestCountsEvictions()
    {
        var bus = new EventBus();

        for (var i = 0; i < 1024 + 5; i++)
        {
            bus.SensorEvents.TryWrite(ASensorEvent()).Should().BeTrue();
        }

        bus.Metrics.SensorEvents.Published.Should().Be(1029);
        bus.Metrics.SensorEvents.Dropped.Should().Be(5);
        bus.Metrics.SensorEvents.Depth.Should().Be(1024);
    }

    [Fact]
    public void GameEventDropOldestCountsEvictions()
    {
        var bus = new EventBus();

        for (var i = 0; i < 256 + 3; i++)
        {
            bus.GameEvents.TryWrite(AGameEvent()).Should().BeTrue();
        }

        bus.Metrics.GameEvents.Dropped.Should().Be(3);
        bus.Metrics.GameEvents.Depth.Should().Be(256);
    }

    [Fact]
    public void ForceCommandWaitModeRejectsWritesWhenFullWithoutDropping()
    {
        var bus = new EventBus();

        for (var i = 0; i < 64; i++)
        {
            bus.ForceCommands.TryWrite(AForceCommand()).Should().BeTrue();
        }

        bus.ForceCommands.TryWrite(AForceCommand()).Should().BeFalse();
        bus.Metrics.ForceCommands.Published.Should().Be(64);
        bus.Metrics.ForceCommands.Dropped.Should().Be(0);
    }

    [Fact]
    public void DepthReflectsBufferedCount()
    {
        var bus = new EventBus();

        bus.SensorEvents.TryWrite(ASensorEvent());
        bus.SensorEvents.TryWrite(ASensorEvent());
        bus.SensorEvents.TryWrite(ASensorEvent());
        bus.Metrics.SensorEvents.Depth.Should().Be(3);

        bus.SensorEventReader.TryRead(out _).Should().BeTrue();
        bus.Metrics.SensorEvents.Depth.Should().Be(2);
    }

    [Fact]
    public async Task WaitToWriteAsyncReturnsTrueWhenCapacityAvailable()
    {
        var bus = new EventBus();

        (await bus.SensorEvents.WaitToWriteAsync()).Should().BeTrue();
    }

    [Fact]
    public void TryCompleteCompletesTheChannelAndRejectsFurtherWrites()
    {
        var bus = new EventBus();

        bus.ForceCommands.TryComplete().Should().BeTrue();
        bus.ForceCommands.TryWrite(AForceCommand()).Should().BeFalse();
    }
}
