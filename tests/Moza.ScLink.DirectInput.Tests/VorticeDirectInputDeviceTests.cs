using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.Core.Models;
using NSubstitute;

namespace Moza.ScLink.DirectInput.Tests;

/// <summary>
/// Unit tests for <see cref="VorticeDirectInputDevice"/>. Covers the M5 constructor guards (plan §H tests
/// 8/9/10 plus three null-argument guards) and the M6 <see cref="VorticeDirectInputDevice.ScaleDirection"/>
/// helper (plan §H tests 11–16).
/// </summary>
/// <remarks>
/// Behavioral coverage of <c>InitializeAsync</c>, <c>ExecuteAsync</c>, the dual-dictionary effect cache, and
/// the re-acquire / re-download retry loop lands in M7–M9. Each milestone adds its surface to this file with
/// its own per-milestone hand-review pause.
/// </remarks>
public sealed class VorticeDirectInputDeviceTests
{
    private static DirectInputDeviceIdentity AnAb6Identity() => new(
        Guid.NewGuid(), "MOZA AB6 Base", "MOZA AB6 Base", DeviceModel.MozaAb6);

    private static DirectInputDeviceIdentity AnAb9Identity() => new(
        Guid.NewGuid(), "MOZA AB9 Base", "MOZA AB9 Base", DeviceModel.MozaAb9);

    private static DirectInputDeviceIdentity AnUnknownIdentity() => new(
        Guid.NewGuid(), "Definitely not a MOZA", "Generic Joystick", DeviceModel.Unknown);

    private static NullLogger<VorticeDirectInputDevice> ALogger() => NullLogger<VorticeDirectInputDevice>.Instance;

    [Fact]
    public void VorticeDirectInputDeviceConstructorThrowsArgumentExceptionOnUnknownDeviceModel()
    {
        var abstraction = Substitute.For<IDirectInputAbstraction>();

        var construct = () => new VorticeDirectInputDevice(abstraction, AnUnknownIdentity(), ALogger());

        construct.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("identity");
    }

    [Fact]
    public void VorticeDirectInputDeviceConstructorAcceptsMozaAb6Model()
    {
        var abstraction = Substitute.For<IDirectInputAbstraction>();
        var identity = AnAb6Identity();

        var device = new VorticeDirectInputDevice(abstraction, identity, ALogger());

        device.Model.Should().Be(DeviceModel.MozaAb6);
        device.DisplayName.Should().Be(identity.DisplayName);
        device.ProductName.Should().Be(identity.ProductName);
        device.InstanceGuid.Should().Be(identity.InstanceGuid);
        device.State.Should().Be(DeviceState.Disconnected);
    }

    [Fact]
    public void VorticeDirectInputDeviceConstructorAcceptsMozaAb9Model()
    {
        var abstraction = Substitute.For<IDirectInputAbstraction>();
        var identity = AnAb9Identity();

        var device = new VorticeDirectInputDevice(abstraction, identity, ALogger());

        device.Model.Should().Be(DeviceModel.MozaAb9);
        device.DisplayName.Should().Be(identity.DisplayName);
        device.ProductName.Should().Be(identity.ProductName);
        device.InstanceGuid.Should().Be(identity.InstanceGuid);
        device.State.Should().Be(DeviceState.Disconnected);
    }

    [Fact]
    public void ConstructorThrowsArgumentNullExceptionForNullAbstraction()
    {
        var construct = () => new VorticeDirectInputDevice(abstraction: null!, AnAb6Identity(), ALogger());

        construct.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("abstraction");
    }

    [Fact]
    public void ConstructorThrowsArgumentNullExceptionForNullIdentity()
    {
        var abstraction = Substitute.For<IDirectInputAbstraction>();

        var construct = () => new VorticeDirectInputDevice(abstraction, identity: null!, ALogger());

        construct.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("identity");
    }

    [Fact]
    public void ConstructorThrowsArgumentNullExceptionForNullLogger()
    {
        var abstraction = Substitute.For<IDirectInputAbstraction>();

        var construct = () => new VorticeDirectInputDevice(abstraction, AnAb6Identity(), logger: null!);

        construct.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("logger");
    }

    // ── ScaleDirection helper (plan §H tests 11–16) ──────────────────────────────────────────

    [Fact]
    public void ScaleDirectionReturnsScaledIntsForNonZeroInput()
    {
        var (x, y) = VorticeDirectInputDevice.ScaleDirection(0.5, -0.5);

        x.Should().Be(5000);
        y.Should().Be(-5000);
    }

    [Fact]
    public void ScaleDirectionReturnsLegacyDefaultForZeroZeroInput()
    {
        var (x, y) = VorticeDirectInputDevice.ScaleDirection(0.0, 0.0);

        // The legacy DirectInputForceFeedbackDevice wrote the literal direction array {1, 1} for every
        // effect. The (0,0) fallback preserves that exactly — note (1, 1), not (10000, 10000).
        x.Should().Be(1);
        y.Should().Be(1);
    }

    [Fact]
    public void ScaleDirectionRoundsToNearestInt()
    {
        // 0.12346 * 10000 = 1234.6 -> rounds to 1235. The input is chosen clear of the .5 midpoint so the
        // assertion is independent of Math.Round's MidpointRounding mode.
        var (x, y) = VorticeDirectInputDevice.ScaleDirection(0.12346, 0.0);

        x.Should().Be(1235);
        y.Should().Be(0);
    }

    [Fact]
    public void ScaleDirectionClampsValuesAboveOnePointZero()
    {
        var (x, y) = VorticeDirectInputDevice.ScaleDirection(1.5, 0.3);

        x.Should().Be(10000);
        y.Should().Be(3000);
    }

    [Fact]
    public void ScaleDirectionClampsValuesBelowNegativeOnePointZero()
    {
        var (x, y) = VorticeDirectInputDevice.ScaleDirection(-2.0, 0.3);

        x.Should().Be(-10000);
        y.Should().Be(3000);
    }

    [Fact]
    public void ScaleDirectionPreservesSignForNegativeValues()
    {
        var (x, y) = VorticeDirectInputDevice.ScaleDirection(-0.5, -0.25);

        x.Should().Be(-5000);
        y.Should().Be(-2500);
    }
}
