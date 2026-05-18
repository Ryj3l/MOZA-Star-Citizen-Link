using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Models;
using Moza.ScLink.DirectInput;
using NSubstitute;
using LegacyDevice = Moza.ScLink.Core.IForceFeedbackDevice;
using NewDevice = Moza.ScLink.Core.Devices.IForceFeedbackDevice;

// Composes LegacyForceFeedbackDeviceAdapter (Obsolete) instances; suppressed file-wide rather than
// peppered at every construction site.
#pragma warning disable CS0618

namespace Moza.ScLink.Effects.Tests;

/// <summary>
/// Pins the T-07 Issue #27 Pass-2 G3-Interpretation-B parallel event surface on
/// <see cref="ForceFeedbackController"/>: <c>ChainStateChanged</c> add/remove must delegate to the
/// underlying <see cref="FallbackForceFeedbackDevice"/> when present, and must be a silent no-op
/// (subscribers never throw) when the controller wraps a non-Fallback device. The silent-no-op
/// shape is the right failure mode for the Phase-2 channels-pipeline future where the chain may
/// be swapped at the factory level.
/// </summary>
public sealed class ForceFeedbackControllerChainStateChangedReExposureTests
{
    private static NewDevice ASubstituteNewDevice(string productName = "Test FFB Base")
    {
        var device = Substitute.For<NewDevice>();
        device.ProductName.Returns(productName);
        device.DisplayName.Returns(productName);
        device.InstanceGuid.Returns(Guid.NewGuid());
        device.Model.Returns(DeviceModel.MozaAb9);
        return device;
    }

    [Fact]
    public async Task ChainStateChangedAddRemoveDelegatesToFallbackDevice()
    {
        // Construct a real Fallback wrapping a real DI adapter + Null tier, wire it through the
        // controller, then drive a hot-loss via the OBSERVER PASSTHROUGH on the controller (proving
        // that BOTH the event re-exposure AND the DeviceAvailabilityObserver passthrough work
        // end-to-end). Subscribed handler must fire.
        var underlying = ASubstituteNewDevice("MOZA AB9 FFB Base");
        var diAdapter = new LegacyForceFeedbackDeviceAdapter(
            underlying,
            NullLogger<LegacyForceFeedbackDeviceAdapter>.Instance);
        var nullTier = new NullForceFeedbackDevice("test-null");
        var fallback = new FallbackForceFeedbackDevice(new LegacyDevice[] { diAdapter, nullTier });
        await fallback.InitializeAsync(CancellationToken.None);

        var controller = new ForceFeedbackController(fallback);
        var observer = controller.DeviceAvailabilityObserver;
        observer.Should().NotBeNull("the controller must expose the observer when wrapping a Fallback");

        var tcs = new TaskCompletionSource<ChainStateChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, ChainStateChangedEventArgs e) => tcs.TrySetResult(e);
        controller.ChainStateChanged += Handler;

        try
        {
            observer!.OnDeviceRemoved();
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
            completed.Should().Be(tcs.Task, "controller.ChainStateChanged must propagate the underlying Fallback event");

            var args = await tcs.Task;
            args.IsReady.Should().BeFalse();
            args.OutputName.Should().Contain("Preview");
        }
        finally
        {
            controller.ChainStateChanged -= Handler;
        }
    }

    [Fact]
    public void ChainStateChangedIsSilentNoOpWhenDeviceIsNotFallbackForceFeedbackDevice()
    {
        // G3-Interpretation-B documented failure mode for Phase-2 channels-pipeline future-proofing:
        // if the chain is ever swapped out at the factory level, subscribers must observe a silent
        // no-op rather than a crash. Both add and remove are wrapped in this contract.
        var bare = Substitute.For<LegacyDevice>();
        bare.Name.Returns("not-a-fallback");
        bare.Status.Returns("not-a-fallback-status");

        var controller = new ForceFeedbackController(bare);
        controller.DeviceAvailabilityObserver.Should().BeNull("the passthrough returns null when not a Fallback");

        Action subscribe = () => controller.ChainStateChanged += (_, _) => { };
        Action unsubscribe = () => controller.ChainStateChanged -= (_, _) => { };

        subscribe.Should().NotThrow();
        unsubscribe.Should().NotThrow();
    }
}
