using FluentAssertions;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Sensors;

namespace Moza.ScLink.Core.Tests.Sensors;

public sealed class SensorSupportingTypesTests
{
    // ── SensorHealth ────────────────────────────────────────────────────────────

    [Fact]
    public void SensorHealthPositionalConstructionPopulatesAllProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var health = new SensorHealth(
            IsHealthy: true,
            LastFault: null,
            EventsEmitted: 42,
            DroppedEvents: 1,
            LastEventAt: now);

        health.IsHealthy.Should().BeTrue();
        health.LastFault.Should().BeNull();
        health.EventsEmitted.Should().Be(42);
        health.DroppedEvents.Should().Be(1);
        health.LastEventAt.Should().Be(now);
    }

    [Fact]
    public void SensorHealthEqualityTwoInstancesWithSameValuesAreEqual()
    {
        var now = new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);
        var h1 = new SensorHealth(true, null, 10, 0, now);
        var h2 = new SensorHealth(true, null, 10, 0, now);

        h1.Should().Be(h2);
    }

    [Fact]
    public void SensorHealthEqualityTwoInstancesWithDifferentValuesAreNotEqual()
    {
        var now = DateTimeOffset.UtcNow;
        var h1 = new SensorHealth(true, null, 10, 0, now);
        var h2 = new SensorHealth(false, "io error", 10, 3, now);

        h1.Should().NotBe(h2);
    }

    [Fact]
    public void SensorHealthFaultedInstanceHasLastFault()
    {
        var health = new SensorHealth(false, "Device lost", 5, 2, null);

        health.IsHealthy.Should().BeFalse();
        health.LastFault.Should().Be("Device lost");
        health.LastEventAt.Should().BeNull();
    }

    // ── SensorStartException ────────────────────────────────────────────────────

    [Fact]
    public void SensorStartExceptionTwoArgConstructorStoresSensorIdAndMessage()
    {
        var ex = new SensorStartException("audio.endpoint-loopback", "device not found");

        ex.SensorId.Should().Be("audio.endpoint-loopback");
        ex.Message.Should().Be("device not found");
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void SensorStartExceptionThreeArgConstructorStoresSensorIdMessageAndInnerException()
    {
        var inner = new InvalidOperationException("com error");
        var ex = new SensorStartException("screen.roi", "capture init failed", inner);

        ex.SensorId.Should().Be("screen.roi");
        ex.Message.Should().Be("capture init failed");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void SensorStartExceptionIsException()
    {
        var ex = new SensorStartException("s", "m");
        ex.Should().BeAssignableTo<Exception>();
    }

    // ── SensorHealthChangedEventArgs ────────────────────────────────────────────

    [Fact]
    public void SensorHealthChangedEventArgsStoresPreviousAndCurrent()
    {
        var now = DateTimeOffset.UtcNow;
        var prev = new SensorHealth(true, null, 100, 0, now);
        var curr = new SensorHealth(false, "io error", 100, 1, now);

        var args = new SensorHealthChangedEventArgs { Previous = prev, Current = curr };

        args.Previous.Should().Be(prev);
        args.Current.Should().Be(curr);
    }

    // ── SensorStateChangedEventArgs ─────────────────────────────────────────────

    [Fact]
    public void SensorStateChangedEventArgsStoresPreviousAndCurrent()
    {
        var args = new SensorStateChangedEventArgs
        {
            Previous = SensorState.Starting,
            Current = SensorState.Running,
        };

        args.Previous.Should().Be(SensorState.Starting);
        args.Current.Should().Be(SensorState.Running);
    }
}
