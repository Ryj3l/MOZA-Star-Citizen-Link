using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;
using NSubstitute;
using Vortice.DirectInput;
// Disambiguate the T-06 effect record from the legacy Moza.ScLink.Core.Models.ForceEffect — same alias the
// production VorticeDirectInputDevice.cs uses; both die when the legacy Models.ForceEffect is removed.
using ForceEffect = Moza.ScLink.Core.Effects.ForceEffect;

namespace Moza.ScLink.DirectInput.Tests;

/// <summary>
/// Unit tests for <see cref="VorticeDirectInputDevice"/>. Covers the M5 constructor guards (plan §H tests
/// 8/9/10 plus three null-argument guards), the M6 <see cref="VorticeDirectInputDevice.ScaleDirection"/>
/// helper (plan §H tests 11–16), and the M7 <see cref="VorticeDirectInputDevice.ComputeCacheKey"/> /
/// <see cref="VorticeDirectInputDevice.DeviceCacheKey"/> cache-key semantics.
/// </summary>
/// <remarks>
/// Behavioral coverage of <c>InitializeAsync</c>, <c>ExecuteAsync</c>, and the re-acquire / re-download
/// retry loop lands in M8–M9. Each milestone adds its surface to this file with its own per-milestone
/// hand-review pause.
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

    // ── ComputeCacheKey / DeviceCacheKey (plan §K M7 cache-key semantics) ─────────────────────

    private static ForceEffect AnEffect(
        string effectId = "fx.test",
        ForceEffectType effectType = ForceEffectType.Periodic,
        double frequencyHz = 40.0,
        TimeSpan? duration = null,
        string? stateKey = null) => new()
    {
        EffectId = effectId,
        EffectType = effectType,
        Category = EffectCategory.Flight,
        FrequencyHz = frequencyHz,
        Duration = duration ?? TimeSpan.FromMilliseconds(250),
        StateKey = stateKey,
    };

    [Fact]
    public void DeviceCacheKeyEqualsReturnsTrueForIdenticalKeys()
    {
        var a = VorticeDirectInputDevice.ComputeCacheKey(AnEffect(), 0.5);
        var b = VorticeDirectInputDevice.ComputeCacheKey(AnEffect(), 0.5);

        a.Should().Be(b);
    }

    [Fact]
    public void DeviceCacheKeyEqualsReturnsFalseForDifferentEffectIds()
    {
        var a = VorticeDirectInputDevice.ComputeCacheKey(AnEffect(effectId: "fx.one"), 0.5);
        var b = VorticeDirectInputDevice.ComputeCacheKey(AnEffect(effectId: "fx.two"), 0.5);

        a.Should().NotBe(b);
    }

    [Fact]
    public void DeviceCacheKeyEqualsReturnsFalseForDifferentIntensityAtRoundedPrecision()
    {
        var a = VorticeDirectInputDevice.ComputeCacheKey(AnEffect(), 0.5);
        var b = VorticeDirectInputDevice.ComputeCacheKey(AnEffect(), 0.6);

        a.Should().NotBe(b);
    }

    [Fact]
    public void DeviceCacheKeyEqualsReturnsTrueForIntensitiesIdenticalAtRoundedPrecision()
    {
        // 0.5001 * 1000 = 500.1 and 0.5002 * 1000 = 500.2 both round to 500 thousandths.
        var a = VorticeDirectInputDevice.ComputeCacheKey(AnEffect(), 0.5001);
        var b = VorticeDirectInputDevice.ComputeCacheKey(AnEffect(), 0.5002);

        a.Should().Be(b);
    }

    [Fact]
    public void DeviceCacheKeyEqualsReturnsTrueForFrequenciesIdenticalAtRoundedPrecision()
    {
        // 30.0001 * 1000 = 30000.1 and 30.0002 * 1000 = 30000.2 both round to 30000 thousandths.
        var a = VorticeDirectInputDevice.ComputeCacheKey(AnEffect(frequencyHz: 30.0001), 0.5);
        var b = VorticeDirectInputDevice.ComputeCacheKey(AnEffect(frequencyHz: 30.0002), 0.5);

        a.Should().Be(b);
    }

    [Fact]
    public void DeviceCacheKeyEqualsTreatsNullAndEmptyStateKeyIdentically()
    {
        var a = VorticeDirectInputDevice.ComputeCacheKey(AnEffect(stateKey: null), 0.5);
        var b = VorticeDirectInputDevice.ComputeCacheKey(AnEffect(stateKey: string.Empty), 0.5);

        a.Should().Be(b);
    }

    // ── ExecuteAsync dispatch (plan §H tests 1, 2, 7 — M8 milestone) ─────────────────────────

    private sealed record UnknownCommand : ForceCommand;

    private static PlayEffectCommand APlayCommand(ForceEffect effect, double finalIntensity = 0.5) =>
        new(effect, finalIntensity)
        {
            CommandId = Guid.NewGuid().ToString(),
            IssuedAt = DateTimeOffset.UnixEpoch,
        };

    private static StopEffectCommand AStopCommand(string stateKey) =>
        new(stateKey)
        {
            CommandId = Guid.NewGuid().ToString(),
            IssuedAt = DateTimeOffset.UnixEpoch,
        };

    private static StopAllCommand AStopAllCommand() =>
        new()
        {
            CommandId = Guid.NewGuid().ToString(),
            IssuedAt = DateTimeOffset.UnixEpoch,
        };

    // Builds a VorticeDirectInputDevice already driven through InitializeAsync against fully-mocked
    // abstractions. CreateEffect returns a fresh effect mock per call by default; tests that need to hold
    // a reference override with an explicit .Returns(...).
    private static async Task<(VorticeDirectInputDevice Sut,
                               IDirectInputAbstraction Abstraction,
                               IDirectInputDeviceAbstraction Device)> AnInitializedSutAsync()
    {
        var abstraction = Substitute.For<IDirectInputAbstraction>();
        var device = Substitute.For<IDirectInputDeviceAbstraction>();
        abstraction.CreateDevice(Arg.Any<Guid>()).Returns(device);
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>())
            .Returns(_ => Substitute.For<IDirectInputEffectAbstraction>());

        var sut = new VorticeDirectInputDevice(abstraction, AnAb6Identity(), ALogger());
        await sut.InitializeAsync(CancellationToken.None);
        return (sut, abstraction, device);
    }

    [Fact]
    public async Task CreatesEffectOnFirstPlay()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        await sut.ExecuteAsync(APlayCommand(AnEffect()), CancellationToken.None);

        device.Received(1).CreateEffect(EffectGuid.Sine, Arg.Any<EffectParameters>());
        effectMock.Received(1).Start(1, EffectPlayFlags.None);
    }

    [Fact]
    public async Task ReusesCachedEffectOnRepeatPlay()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        var effect = AnEffect();
        await sut.ExecuteAsync(APlayCommand(effect, 0.5), CancellationToken.None);
        await sut.ExecuteAsync(APlayCommand(effect, 0.5), CancellationToken.None);

        device.Received(1).CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>());
        effectMock.Received(2).Start(1, EffectPlayFlags.None);
    }

    [Fact]
    public async Task DisposesAllCachedEffectsOnDispose()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var firstEffect = Substitute.For<IDirectInputEffectAbstraction>();
        var secondEffect = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>())
            .Returns(firstEffect, secondEffect);

        await sut.ExecuteAsync(APlayCommand(AnEffect(effectId: "fx.one")), CancellationToken.None);
        await sut.ExecuteAsync(APlayCommand(AnEffect(effectId: "fx.two")), CancellationToken.None);

        await sut.DisposeAsync();

        firstEffect.Received(1).Dispose();
        secondEffect.Received(1).Dispose();
        device.Received(1).Dispose();
    }

    [Fact]
    public async Task PlayWithStateKeyStopsPriorEffectThenStartsNewOnParamsChange()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var firstEffect = Substitute.For<IDirectInputEffectAbstraction>();
        var secondEffect = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>())
            .Returns(firstEffect, secondEffect);

        // Intensities 0.5 and 0.9 are chosen because ComputeCacheKey rounds them to distinct buckets
        // (IntensityRoundedThousandths 500 vs 900) — the second play is a genuine params change, not a
        // same-key replay. This locks the test to the rounding rule rather than "different values".
        var effect = AnEffect(stateKey: "sk1");
        await sut.ExecuteAsync(APlayCommand(effect, 0.5), CancellationToken.None);
        await sut.ExecuteAsync(APlayCommand(effect, 0.9), CancellationToken.None);

        Received.InOrder(() =>
        {
            firstEffect.Stop();
            secondEffect.Start(1, EffectPlayFlags.None);
        });
        firstEffect.Received(1).Stop();
        secondEffect.Received(1).Start(1, EffectPlayFlags.None);
    }

    [Fact]
    public async Task PlayWithSameStateKeySameParamsRestartsSameEffect()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        var effect = AnEffect(stateKey: "sk1");
        await sut.ExecuteAsync(APlayCommand(effect, 0.5), CancellationToken.None);
        await sut.ExecuteAsync(APlayCommand(effect, 0.5), CancellationToken.None);

        // Same StateKey + same params == same cache key: the second play resolves the *same* cached
        // effect instance, stops it (step 3), then restarts it (step 4) — a clean restart on one object.
        // Asserting on the held effectMock reference pins the same-reference invariant directly.
        device.Received(1).CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>());
        effectMock.Received(1).Stop();
        effectMock.Received(2).Start(1, EffectPlayFlags.None);
    }

    [Fact]
    public async Task StopEffectStopsActiveEffectForKnownStateKey()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        await sut.ExecuteAsync(APlayCommand(AnEffect(stateKey: "sk1")), CancellationToken.None);
        await sut.ExecuteAsync(AStopCommand("sk1"), CancellationToken.None);

        effectMock.Received(1).Stop();
    }

    [Fact]
    public async Task StopEffectIsNoOpForUnknownStateKey()
    {
        var (sut, _, device) = await AnInitializedSutAsync();

        var act = async () => await sut.ExecuteAsync(AStopCommand("never-played"), CancellationToken.None);

        await act.Should().NotThrowAsync();
        device.DidNotReceive().CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>());
    }

    [Fact]
    public async Task StopAllStopsEveryActiveEffectAndSendsHardwareStopAll()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var effectA = Substitute.For<IDirectInputEffectAbstraction>();
        var effectB = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>())
            .Returns(effectA, effectB);

        await sut.ExecuteAsync(APlayCommand(AnEffect(effectId: "fx.a", stateKey: "a")), CancellationToken.None);
        await sut.ExecuteAsync(APlayCommand(AnEffect(effectId: "fx.b", stateKey: "b")), CancellationToken.None);
        await sut.ExecuteAsync(AStopAllCommand(), CancellationToken.None);

        effectA.Received(1).Stop();
        effectB.Received(1).Stop();
        device.Received(1).SendForceFeedbackCommand(ForceFeedbackCommand.StopAll);
    }

    [Fact]
    public async Task StopAllAsyncEntrypointMatchesStopAllCommandDispatch()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        await sut.ExecuteAsync(APlayCommand(AnEffect(stateKey: "sk1")), CancellationToken.None);
        await sut.StopAllAsync(CancellationToken.None);

        effectMock.Received(1).Stop();
        device.Received(1).SendForceFeedbackCommand(ForceFeedbackCommand.StopAll);
    }

    [Fact]
    public async Task ExecuteAsyncWithUnknownCommandTypeFaultsWithInvalidOperationException()
    {
        var (sut, _, _) = await AnInitializedSutAsync();
        var unknown = new UnknownCommand
        {
            CommandId = Guid.NewGuid().ToString(),
            IssuedAt = DateTimeOffset.UnixEpoch,
        };

        var act = async () => await sut.ExecuteAsync(unknown, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UnknownCommand*");
    }

    [Fact]
    public async Task ExecuteAsyncThrowsObjectDisposedExceptionAfterDispose()
    {
        var (sut, _, _) = await AnInitializedSutAsync();
        await sut.DisposeAsync();

        var act = () => sut.ExecuteAsync(APlayCommand(AnEffect()), CancellationToken.None);

        // ExecuteAsync's guard throws synchronously, but FA 6.x exposes only ThrowAsync for Func<Task>;
        // ThrowAsync catches a synchronous throw from the func just the same.
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task ExecuteAsyncThrowsArgumentNullExceptionForNullCommand()
    {
        var (sut, _, _) = await AnInitializedSutAsync();

        var act = () => sut.ExecuteAsync(null!, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentNullException>())
            .Which.ParamName.Should().Be("command");
    }
}
