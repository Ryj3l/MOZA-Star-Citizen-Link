using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.Core.Safety;

namespace Moza.ScLink.Effects.Tests;

// Spec deliverable 7 names these with underscores (ActivateAsync_Sets_IsActive, …); repo convention is
// PascalCase (see SafetyLimiterStageTests). Mapped 1:1 per the PR-body name table.
public sealed class EmergencyStopTests
{
    [Fact]
    public async Task ActivateAsyncSetsIsActive()
    {
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        estop.IsActive.Should().BeFalse();

        await estop.ActivateAsync("hotkey");

        estop.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ActivateAsyncRaisesActivatedEvent()
    {
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        EmergencyStopActivatedEventArgs? captured = null;
        estop.Activated += (_, e) => captured = e;

        await estop.ActivateAsync("ui");

        captured.Should().NotBeNull();
        captured!.Reason.Should().Be("ui");
    }

    [Fact]
    public async Task ClearAsyncResetsState()
    {
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        var clearedRaised = false;
        estop.Cleared += (_, _) => clearedRaised = true;
        await estop.ActivateAsync("hotkey");

        await estop.ClearAsync();

        estop.IsActive.Should().BeFalse();
        clearedRaised.Should().BeTrue();
    }

    [Fact]
    public async Task MultipleActivateCallsAreIdempotent()
    {
        var estop = new EmergencyStop(NullLogger<EmergencyStop>.Instance);
        var activationCount = 0;
        estop.Activated += (_, _) => activationCount++;

        await estop.ActivateAsync("hotkey");
        await estop.ActivateAsync("ui");
        await estop.ActivateAsync("hotkey");

        activationCount.Should().Be(1);
        estop.IsActive.Should().BeTrue();
    }
}
