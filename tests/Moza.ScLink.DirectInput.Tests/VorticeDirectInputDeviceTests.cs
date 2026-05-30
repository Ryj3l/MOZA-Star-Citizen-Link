using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;
using NSubstitute;
using SharpGen.Runtime;
using Vortice.DirectInput;

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
        string? stateKey = null,
        ForceEnvelope? envelope = null,
        double directionX = 0.0,
        double directionY = 0.0) => new()
    {
        EffectId = effectId,
        EffectType = effectType,
        Category = EffectCategory.Flight,
        FrequencyHz = frequencyHz,
        Duration = duration ?? TimeSpan.FromMilliseconds(250),
        StateKey = stateKey,
        Envelope = envelope,
        DirectionX = directionX,
        DirectionY = directionY,
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
    // a reference override with an explicit .Returns(...). delayStrategy defaults to an immediate no-op so
    // M9 retry tests never incur real Task.Delay waits; logger defaults to NullLogger.
    private static async Task<(VorticeDirectInputDevice Sut,
                               IDirectInputAbstraction Abstraction,
                               IDirectInputDeviceAbstraction Device)> AnInitializedSutAsync(
        Func<int, CancellationToken, Task>? delayStrategy = null,
        ILogger<VorticeDirectInputDevice>? logger = null)
    {
        var abstraction = Substitute.For<IDirectInputAbstraction>();
        var device = Substitute.For<IDirectInputDeviceAbstraction>();
        abstraction.CreateDevice(Arg.Any<Guid>()).Returns(device);
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>())
            .Returns(_ => Substitute.For<IDirectInputEffectAbstraction>());

        var sut = new VorticeDirectInputDevice(
            abstraction,
            AnAb6Identity(),
            logger ?? ALogger(),
            delayStrategy ?? ((_, _) => Task.CompletedTask));
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

    // ── M9 re-acquire / re-download / transient retry loop (plan §H tests 3/4/5 + support) ────

    // HRESULT constants mirror DirectInputErrorClassifier's internal HResults (re-stated here so the test
    // project needs no InternalsVisibleTo). 0x80040205 = DIERR_NOTEXCLUSIVEACQUIRED (NeedsReacquire),
    // 0x80040203 = DIERR_NOTDOWNLOADED (NeedsRedownload), 0x80040208 = DIERR_EFFECTPLAYING (Transient).
    private const int HrNotExclusiveAcquired = unchecked((int)0x80040205);
    private const int HrNotDownloaded = unchecked((int)0x80040203);
    private const int HrEffectPlaying = unchecked((int)0x80040208);
    private const int HrDeviceNotConnected = unchecked((int)0x8007048F);

    private static SharpGenException ASharpGenException(int hresult, string message = "DI test failure") =>
        new(new Result(hresult), message, innerException: null);

    // Minimal ILogger that records the level of every entry — lets GivesUpAfterThreeReacquireAttempts
    // assert "exactly one Warning-level log entry" (§H test 5) without an NSubstitute open-generic dance.
    private sealed class RecordingLogger : ILogger<VorticeDirectInputDevice>
    {
        public List<LogLevel> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(logLevel);
    }

    [Fact]
    public async Task ReacquiresOnDirectInputNotExclusiveAcquired()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        var startCalls = 0;
        effectMock
            .When(e => e.Start(Arg.Any<int>(), Arg.Any<EffectPlayFlags>()))
            .Do(_ =>
            {
                startCalls++;
                if (startCalls == 1)
                {
                    throw ASharpGenException(HrNotExclusiveAcquired);
                }
            });

        await sut.ExecuteAsync(APlayCommand(AnEffect()), CancellationToken.None);

        device.Received(1).Unacquire();   // only the M9 reacquire path calls Unacquire()
        startCalls.Should().Be(2);        // initial throw + successful retry
        effectMock.Received(2).Start(1, EffectPlayFlags.None);
    }

    [Fact]
    public async Task RedownloadsOnDirectInputNotDownloaded()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        var startCalls = 0;
        effectMock
            .When(e => e.Start(Arg.Any<int>(), Arg.Any<EffectPlayFlags>()))
            .Do(_ =>
            {
                startCalls++;
                if (startCalls == 1)
                {
                    throw ASharpGenException(HrNotDownloaded);
                }
            });

        await sut.ExecuteAsync(APlayCommand(AnEffect()), CancellationToken.None);

        effectMock.Received(1).Download();   // redownload path called Download() once
        startCalls.Should().Be(2);           // initial throw + successful retry
        effectMock.Received(2).Start(1, EffectPlayFlags.None);
        device.Received(1).CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>());   // no re-create
    }

    [Fact]
    public async Task GivesUpAfterThreeReacquireAttempts()
    {
        var delays = new List<int>();
        Func<int, CancellationToken, Task> recorder = (ms, _) =>
        {
            delays.Add(ms);
            return Task.CompletedTask;
        };
        var recordingLogger = new RecordingLogger();

        var (sut, _, device) = await AnInitializedSutAsync(recorder, recordingLogger);
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        effectMock
            .When(e => e.Start(Arg.Any<int>(), Arg.Any<EffectPlayFlags>()))
            .Do(_ => throw ASharpGenException(HrNotExclusiveAcquired));   // every Start throws

        var act = async () => await sut.ExecuteAsync(APlayCommand(AnEffect()), CancellationToken.None);

        await act.Should().NotThrowAsync();                      // M9 contract: logs Warning, returns
        delays.Should().Equal(50, 200, 500);                     // exactly 3 attempts, backoff sequence
        device.Received(3).Unacquire();                          // 3 reacquire pairs
        effectMock.Received(4).Start(1, EffectPlayFlags.None);   // initial + 3 re-issues
        recordingLogger.Entries.Count(level => level == LogLevel.Warning).Should().Be(1);
    }

    [Fact]
    public async Task ReacquireBudgetIsIndependentOfRedownloadBudget()
    {
        var delays = new List<int>();
        Func<int, CancellationToken, Task> recorder = (ms, _) =>
        {
            delays.Add(ms);
            return Task.CompletedTask;
        };

        var (sut, _, device) = await AnInitializedSutAsync(recorder);
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        var startCalls = 0;
        effectMock
            .When(e => e.Start(Arg.Any<int>(), Arg.Any<EffectPlayFlags>()))
            .Do(_ =>
            {
                startCalls++;
                // Call 1: NeedsRedownload (spends the one redownload bite). Calls 2+: NeedsReacquire
                // forever — must still get the full, independent 3-attempt reacquire budget.
                throw startCalls == 1
                    ? ASharpGenException(HrNotDownloaded)
                    : ASharpGenException(HrNotExclusiveAcquired);
            });

        await sut.ExecuteAsync(APlayCommand(AnEffect()), CancellationToken.None);

        effectMock.Received(1).Download();      // redownload bite ran once
        delays.Should().Equal(50, 200, 500);    // reacquire still got its full, independent 3
        device.Received(3).Unacquire();
        // 1 redownload re-issue + 4 reacquire-phase issues (3 that delay + 1 that hits exhaustion).
        effectMock.Received(5).Start(1, EffectPlayFlags.None);
    }

    [Fact]
    public async Task TransientFailureRetriesOnceImmediately()
    {
        var delays = new List<int>();
        Func<int, CancellationToken, Task> recorder = (ms, _) =>
        {
            delays.Add(ms);
            return Task.CompletedTask;
        };

        var (sut, _, device) = await AnInitializedSutAsync(recorder);
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        var startCalls = 0;
        effectMock
            .When(e => e.Start(Arg.Any<int>(), Arg.Any<EffectPlayFlags>()))
            .Do(_ =>
            {
                startCalls++;
                if (startCalls == 1)
                {
                    throw ASharpGenException(HrEffectPlaying);
                }
            });

        await sut.ExecuteAsync(APlayCommand(AnEffect()), CancellationToken.None);

        startCalls.Should().Be(2);
        effectMock.Received(2).Start(1, EffectPlayFlags.None);
        delays.Should().BeEmpty();              // transient path does NOT touch _delayStrategy
        device.DidNotReceive().Unacquire();     // transient path does NOT re-acquire
    }

    [Fact]
    public async Task TransientFailureGivesUpAfterOneRetry()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        effectMock
            .When(e => e.Start(Arg.Any<int>(), Arg.Any<EffectPlayFlags>()))
            .Do(_ => throw ASharpGenException(HrEffectPlaying));   // every Start throws Transient

        var act = async () => await sut.ExecuteAsync(APlayCommand(AnEffect()), CancellationToken.None);

        await act.Should().NotThrowAsync();
        // Initial attempt + exactly one transient retry, then the transientTried guard exhausts the path.
        effectMock.Received(2).Start(1, EffectPlayFlags.None);
    }

    [Fact]
    public async Task RedownloadGivesUpAfterOneRetry()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        effectMock
            .When(e => e.Start(Arg.Any<int>(), Arg.Any<EffectPlayFlags>()))
            .Do(_ => throw ASharpGenException(HrNotDownloaded));   // every Start throws NeedsRedownload

        var act = async () => await sut.ExecuteAsync(APlayCommand(AnEffect()), CancellationToken.None);

        await act.Should().NotThrowAsync();
        // Initial attempt + one redownload retry, then the redownloadTried guard exhausts the path.
        effectMock.Received(2).Start(1, EffectPlayFlags.None);
        effectMock.Received(1).Download();
    }

    [Fact]
    public async Task RedownloadActionFailureContinuesRetryLoop()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        var startCalls = 0;
        effectMock
            .When(e => e.Start(Arg.Any<int>(), Arg.Any<EffectPlayFlags>()))
            .Do(_ =>
            {
                startCalls++;
                if (startCalls == 1)
                {
                    throw ASharpGenException(HrNotDownloaded);
                }
            });
        // The Download() recovery action itself throws — the wrapper catches it (it does not classify
        // recovery-action failures), logs RedownloadCallThrew, and continues the loop.
        effectMock
            .When(e => e.Download())
            .Do(_ => throw ASharpGenException(HrNotExclusiveAcquired));

        var act = async () => await sut.ExecuteAsync(APlayCommand(AnEffect()), CancellationToken.None);

        await act.Should().NotThrowAsync();                      // Download() failure does not propagate
        effectMock.Received(1).Download();                       // recovery action attempted once
        effectMock.Received(2).Start(1, EffectPlayFlags.None);   // initial throw + successful retry
    }

    [Fact]
    public async Task FatalFailureIsLoggedAndDoesNotThrow()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        // 0x80004005 = E_FAIL — not a DIERR_* the classifier recognizes => DirectInputErrorClass.Fatal.
        effectMock
            .When(e => e.Start(Arg.Any<int>(), Arg.Any<EffectPlayFlags>()))
            .Do(_ => throw ASharpGenException(unchecked((int)0x80004005)));

        var act = async () => await sut.ExecuteAsync(APlayCommand(AnEffect()), CancellationToken.None);

        await act.Should().NotThrowAsync();                      // Fatal logged + swallowed, never thrown
        effectMock.Received(1).Start(1, EffectPlayFlags.None);   // no retry on Fatal
        device.DidNotReceive().Unacquire();
    }

    [Fact]
    public async Task CreateEffectExhaustionLeavesEffectUncachedAndDoesNotThrow()
    {
        var (sut, _, device) = await AnInitializedSutAsync();

        var createCalls = 0;
        device
            .When(d => d.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()))
            .Do(_ =>
            {
                createCalls++;
                throw ASharpGenException(HrNotExclusiveAcquired);   // always NeedsReacquire => exhausts
            });

        var effect = AnEffect();
        var act = async () => await sut.ExecuteAsync(APlayCommand(effect, 0.5), CancellationToken.None);

        await act.Should().NotThrowAsync();   // wrapper exhausted CreateEffect, logged Warning, returned
        createCalls.Should().Be(4);           // initial + 3 reacquire re-issues, then gave up

        // A second identical play must re-attempt CreateEffect — proof no null was published into the
        // cache (a cached null would NRE or be returned as a hit, skipping CreateEffect entirely).
        await sut.ExecuteAsync(APlayCommand(effect, 0.5), CancellationToken.None);
        createCalls.Should().Be(8);           // another full 4-attempt round, not a cache hit
    }

    [Fact]
    public async Task SuccessfulPlayDoesNotReacquireOrRetry()
    {
        var (sut, _, device) = await AnInitializedSutAsync();
        var effectMock = Substitute.For<IDirectInputEffectAbstraction>();
        device.CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>()).Returns(effectMock);

        await sut.ExecuteAsync(APlayCommand(AnEffect()), CancellationToken.None);

        device.Received(1).CreateEffect(Arg.Any<Guid>(), Arg.Any<EffectParameters>());
        effectMock.Received(1).Start(1, EffectPlayFlags.None);
        effectMock.DidNotReceive().Download();
        device.DidNotReceive().Unacquire();
    }

    // ── BuildEffectParameters flag/Axes pairing regression (Issue #26) ────────────────────────

    [Fact]
    public void BuildEffectParametersUsesObjectOffsetsFlagWithByteOffsetAxes()
    {
        // Issue #26: BuildEffectParameters previously declared EffectFlags.ObjectIds while passing
        // byte-offset Axes values (DijofsX = 0, DijofsY = 4), producing DIERR_INVALIDPARAM at
        // CreateEffect time on real AB9 hardware (M14, four reproducers on Test Quantum).
        // The Periodic branch is the one M14 directly exercised on hardware.
        var effect = AnEffect(
            effectType: ForceEffectType.Periodic,
            frequencyHz: 34.0,
            duration: TimeSpan.FromSeconds(8));

        var parameters = VorticeDirectInputDevice.BuildEffectParameters(effect, finalIntensity: 0.5);

        parameters.Flags.Should().HaveFlag(EffectFlags.Cartesian);
        parameters.Flags.Should().HaveFlag(EffectFlags.ObjectOffsets);
        parameters.Flags.Should().NotHaveFlag(EffectFlags.ObjectIds);
        parameters.Axes.Should().Equal(JoystickAxisOffsets.DijofsX, JoystickAxisOffsets.DijofsY);
    }

    [Fact]
    public void BuildEffectParametersConstantForceUsesObjectOffsetsFlag()
    {
        // Same flag-pairing invariant on the ConstantForce branch (Test Impact). BuildEffectParameters'
        // EffectParameters return shape is shared across both EffectType branches (Periodic /
        // ConstantForce), but explicit per-branch coverage matches Issue #26's per-effect-type
        // acceptance-criteria framing and pins both paths against future regression.
        var effect = AnEffect(
            effectType: ForceEffectType.ConstantForce,
            duration: TimeSpan.FromMilliseconds(120));

        var parameters = VorticeDirectInputDevice.BuildEffectParameters(effect, finalIntensity: 0.5);

        parameters.Flags.Should().HaveFlag(EffectFlags.Cartesian);
        parameters.Flags.Should().HaveFlag(EffectFlags.ObjectOffsets);
        parameters.Flags.Should().NotHaveFlag(EffectFlags.ObjectIds);
        parameters.Axes.Should().Equal(JoystickAxisOffsets.DijofsX, JoystickAxisOffsets.DijofsY);
    }

    // ── BuildEffectParameters Choice-A envelope mapping (T-28) ────────────────────────────────
    // The catalog-matrix tests below pin the Choice-A peak-anchored fold blessed in T-28 preamble
    // Fork 1 against each envelope-carrying phase1.json effect. Each test constructs the effect
    // literally (no JSON loading — keeps tests hermetic and the catalog values visible inline) and
    // asserts the exact AttackLevel/AttackTime/FadeLevel/FadeTime + main magnitude + Duration.
    // finalIntensity is the catalog baseIntensity throughout to mirror what the production pipeline
    // produces under unity gain (mental-model: "raw effect, pre-gain-stack reduction").

    [Fact]
    public void BuildEffectParametersMapsQuantumSpoolEnvelopeChoiceA()
    {
        // phase1.json: quantum-spool-v1 — PeriodicWithEnvelope; freq 34 Hz; duration 8000 ms;
        // envelope A=250ms / H=7000ms / D=500ms / R=250ms; AttackLevel=0.3; SustainLevel=1.0;
        // baseIntensity=0.42. Choice A: envelopeIntensity = 0.42 × 0.3 = 0.126 → magnitude 1260.
        var effect = AnEffect(
            effectType: ForceEffectType.PeriodicWithEnvelope,
            frequencyHz: 34.0,
            duration: TimeSpan.FromSeconds(8),
            envelope: new ForceEnvelope(
                Attack: TimeSpan.FromMilliseconds(250),
                Hold: TimeSpan.FromMilliseconds(7000),
                Decay: TimeSpan.FromMilliseconds(500),
                Release: TimeSpan.FromMilliseconds(250),
                AttackLevel: 0.3,
                SustainLevel: 1.0));

        var parameters = VorticeDirectInputDevice.BuildEffectParameters(effect, finalIntensity: 0.42);

        parameters.Envelope.Should().NotBeNull();
        parameters.Envelope.AttackLevel.Should().Be(0);
        parameters.Envelope.AttackTime.Should().Be(250_000);   // 250 ms × 1000 µs/ms
        parameters.Envelope.FadeLevel.Should().Be(0);
        parameters.Envelope.FadeTime.Should().Be(250_000);     // 250 ms × 1000 µs/ms
        parameters.Duration.Should().Be(8_000_000);            // 8 s × 1_000_000 µs/s
        parameters.Parameters.Should().BeOfType<PeriodicForce>()
            .Which.Magnitude.Should().Be(1260);                // ScaleMagnitude(0.42 × 0.3)
    }

    [Fact]
    public void BuildEffectParametersMapsQuantumJumpExitEnvelopeChoiceA()
    {
        // phase1.json: quantum-jump-exit-v1 — PeriodicWithEnvelope; freq 22 Hz; duration 450 ms;
        // envelope A=30ms / H=120ms / D=200ms / R=100ms; AttackLevel=1.0; SustainLevel=0.7;
        // baseIntensity=0.6. Choice A: envelopeIntensity = 0.6 × 1.0 = 0.6 → magnitude 6000.
        var effect = AnEffect(
            effectType: ForceEffectType.PeriodicWithEnvelope,
            frequencyHz: 22.0,
            duration: TimeSpan.FromMilliseconds(450),
            envelope: new ForceEnvelope(
                Attack: TimeSpan.FromMilliseconds(30),
                Hold: TimeSpan.FromMilliseconds(120),
                Decay: TimeSpan.FromMilliseconds(200),
                Release: TimeSpan.FromMilliseconds(100),
                AttackLevel: 1.0,
                SustainLevel: 0.7));

        var parameters = VorticeDirectInputDevice.BuildEffectParameters(effect, finalIntensity: 0.6);

        parameters.Envelope.AttackLevel.Should().Be(0);
        parameters.Envelope.AttackTime.Should().Be(30_000);    // 30 ms × 1000 µs/ms
        parameters.Envelope.FadeLevel.Should().Be(0);
        parameters.Envelope.FadeTime.Should().Be(100_000);     // 100 ms × 1000 µs/ms
        parameters.Duration.Should().Be(450_000);              // 450 ms × 1000 µs/ms
        parameters.Parameters.Should().BeOfType<PeriodicForce>()
            .Which.Magnitude.Should().Be(6000);                // ScaleMagnitude(0.6 × 1.0)
    }

    [Fact]
    public void BuildEffectParametersMapsAtmosphereEntryEnvelopeChoiceA()
    {
        // phase1.json: atmosphere-entry-v1 — PeriodicWithEnvelope; freq 18 Hz; duration 0 (sustained);
        // envelope A=1500ms / H=0 / D=0 / R=800ms; AttackLevel=0.5; SustainLevel=1.0;
        // baseIntensity=0.22. Choice A: envelopeIntensity = 0.22 × 0.5 = 0.11 → magnitude 1100.
        // Duration=0 routes through DurationToMicroseconds' sustained branch → InfiniteDuration (= -1).
        var effect = AnEffect(
            effectType: ForceEffectType.PeriodicWithEnvelope,
            frequencyHz: 18.0,
            duration: TimeSpan.Zero,
            envelope: new ForceEnvelope(
                Attack: TimeSpan.FromMilliseconds(1500),
                Hold: TimeSpan.Zero,
                Decay: TimeSpan.Zero,
                Release: TimeSpan.FromMilliseconds(800),
                AttackLevel: 0.5,
                SustainLevel: 1.0));

        var parameters = VorticeDirectInputDevice.BuildEffectParameters(effect, finalIntensity: 0.22);

        parameters.Envelope.AttackLevel.Should().Be(0);
        parameters.Envelope.AttackTime.Should().Be(1_500_000); // 1500 ms × 1000 µs/ms
        parameters.Envelope.FadeLevel.Should().Be(0);
        parameters.Envelope.FadeTime.Should().Be(800_000);     // 800 ms × 1000 µs/ms
        parameters.Duration.Should().Be(-1);                   // InfiniteDuration sentinel
        parameters.Parameters.Should().BeOfType<PeriodicForce>()
            .Which.Magnitude.Should().Be(1100);                // ScaleMagnitude(0.22 × 0.5)
    }

    [Fact]
    public void BuildEffectParametersMapsLandingContactEnvelopeChoiceA()
    {
        // phase1.json: landing-contact-v1 — ConstantForce; freq 0; duration 220 ms;
        // envelope A=20ms / H=80ms / D=100ms / R=20ms; AttackLevel=1.0; SustainLevel=0.6;
        // baseIntensity=0.55. Choice A: envelopeIntensity = 0.55 × 1.0 = 0.55 → magnitude 5500.
        // typeSpecific is ConstantForce (not PeriodicForce) — this exercises the envelope path on
        // the non-periodic branch.
        var effect = AnEffect(
            effectType: ForceEffectType.ConstantForce,
            frequencyHz: 0.0,
            duration: TimeSpan.FromMilliseconds(220),
            envelope: new ForceEnvelope(
                Attack: TimeSpan.FromMilliseconds(20),
                Hold: TimeSpan.FromMilliseconds(80),
                Decay: TimeSpan.FromMilliseconds(100),
                Release: TimeSpan.FromMilliseconds(20),
                AttackLevel: 1.0,
                SustainLevel: 0.6),
            directionY: -1.0);

        var parameters = VorticeDirectInputDevice.BuildEffectParameters(effect, finalIntensity: 0.55);

        parameters.Envelope.AttackLevel.Should().Be(0);
        parameters.Envelope.AttackTime.Should().Be(20_000);    // 20 ms × 1000 µs/ms
        parameters.Envelope.FadeLevel.Should().Be(0);
        parameters.Envelope.FadeTime.Should().Be(20_000);      // 20 ms × 1000 µs/ms
        parameters.Duration.Should().Be(220_000);              // 220 ms × 1000 µs/ms
        parameters.Parameters.Should().BeOfType<ConstantForce>()
            .Which.Magnitude.Should().Be(5500);                // ScaleMagnitude(0.55 × 1.0)
    }

    [Fact]
    public void BuildEffectParametersMapsWeaponFireGenericEnvelopeChoiceA()
    {
        // phase1.json: weapon-fire-generic-v1 — Periodic (NOT PeriodicWithEnvelope); freq 60 Hz;
        // duration 90 ms; envelope A=5ms / H=40ms / D=40ms / R=5ms; AttackLevel=1.0; SustainLevel=0.7;
        // baseIntensity=0.4. Choice A: envelopeIntensity = 0.4 × 1.0 = 0.4 → magnitude 4000.
        // This effect's EffectType is Periodic — exercises the envelope path on the plain-Periodic
        // switch arm (not the new PeriodicWithEnvelope arm), confirming envelope construction is
        // gated by effect.Envelope presence, not by EffectType.
        var effect = AnEffect(
            effectType: ForceEffectType.Periodic,
            frequencyHz: 60.0,
            duration: TimeSpan.FromMilliseconds(90),
            envelope: new ForceEnvelope(
                Attack: TimeSpan.FromMilliseconds(5),
                Hold: TimeSpan.FromMilliseconds(40),
                Decay: TimeSpan.FromMilliseconds(40),
                Release: TimeSpan.FromMilliseconds(5),
                AttackLevel: 1.0,
                SustainLevel: 0.7));

        var parameters = VorticeDirectInputDevice.BuildEffectParameters(effect, finalIntensity: 0.4);

        parameters.Envelope.AttackLevel.Should().Be(0);
        parameters.Envelope.AttackTime.Should().Be(5_000);     // 5 ms × 1000 µs/ms
        parameters.Envelope.FadeLevel.Should().Be(0);
        parameters.Envelope.FadeTime.Should().Be(5_000);       // 5 ms × 1000 µs/ms
        parameters.Duration.Should().Be(90_000);               // 90 ms × 1000 µs/ms
        parameters.Parameters.Should().BeOfType<PeriodicForce>()
            .Which.Magnitude.Should().Be(4000);                // ScaleMagnitude(0.4 × 1.0)
    }

    [Fact]
    public void BuildEffectParametersMapsVehicleDestructionEnvelopeChoiceA()
    {
        // phase1.json: vehicle-destruction-v1 — PeriodicWithEnvelope; freq 14 Hz; duration 1400 ms;
        // envelope A=50ms / H=400ms / D=800ms / R=150ms; AttackLevel=1.0; SustainLevel=0.7;
        // baseIntensity=0.8. Choice A: envelopeIntensity = 0.8 × 1.0 = 0.8 → magnitude 8000.
        var effect = AnEffect(
            effectType: ForceEffectType.PeriodicWithEnvelope,
            frequencyHz: 14.0,
            duration: TimeSpan.FromMilliseconds(1400),
            envelope: new ForceEnvelope(
                Attack: TimeSpan.FromMilliseconds(50),
                Hold: TimeSpan.FromMilliseconds(400),
                Decay: TimeSpan.FromMilliseconds(800),
                Release: TimeSpan.FromMilliseconds(150),
                AttackLevel: 1.0,
                SustainLevel: 0.7));

        var parameters = VorticeDirectInputDevice.BuildEffectParameters(effect, finalIntensity: 0.8);

        parameters.Envelope.AttackLevel.Should().Be(0);
        parameters.Envelope.AttackTime.Should().Be(50_000);    // 50 ms × 1000 µs/ms
        parameters.Envelope.FadeLevel.Should().Be(0);
        parameters.Envelope.FadeTime.Should().Be(150_000);     // 150 ms × 1000 µs/ms
        parameters.Duration.Should().Be(1_400_000);            // 1400 ms × 1000 µs/ms
        parameters.Parameters.Should().BeOfType<PeriodicForce>()
            .Which.Magnitude.Should().Be(8000);                // ScaleMagnitude(0.8 × 1.0)
    }

    [Fact]
    public void BuildEffectParametersOmitsEnvelopeWhenNull()
    {
        // Baseline: when effect.Envelope is null, EffectParameters.Envelope round-trips as the null!
        // marker that DirectInput interprets as lpEnvelope=NULL ("no envelope shaping"). magnitude
        // uses finalIntensity directly (no AttackLevel scaling), so 0.5 → 5000.
        var effect = AnEffect(effectType: ForceEffectType.Periodic, envelope: null);

        var parameters = VorticeDirectInputDevice.BuildEffectParameters(effect, finalIntensity: 0.5);

        parameters.Envelope.Should().BeNull();
        parameters.Parameters.Should().BeOfType<PeriodicForce>()
            .Which.Magnitude.Should().Be(5000);                // ScaleMagnitude(0.5) — no envelope scaling
    }

    [Fact]
    public void BuildEffectParametersScalesEnvelopeIntensityByFinalIntensity()
    {
        // The envelopeIntensity multiplication is linear in finalIntensity: doubling finalIntensity
        // doubles the resulting magnitude. The envelope shape (AttackTime / FadeTime / levels) does
        // NOT depend on finalIntensity — only the body-at-main magnitude scales.
        var envelope = new ForceEnvelope(
            Attack: TimeSpan.FromMilliseconds(100),
            Hold: TimeSpan.Zero,
            Decay: TimeSpan.Zero,
            Release: TimeSpan.FromMilliseconds(100),
            AttackLevel: 0.5,
            SustainLevel: 0.5);
        var effect = AnEffect(effectType: ForceEffectType.PeriodicWithEnvelope, envelope: envelope);

        var atFull = VorticeDirectInputDevice.BuildEffectParameters(effect, finalIntensity: 1.0);
        var atHalf = VorticeDirectInputDevice.BuildEffectParameters(effect, finalIntensity: 0.5);

        // Choice A: envelopeIntensity = finalIntensity × env.AttackLevel
        atFull.Parameters.Should().BeOfType<PeriodicForce>()
            .Which.Magnitude.Should().Be(5000);                // 1.0 × 0.5 → 0.5 → 5000
        atHalf.Parameters.Should().BeOfType<PeriodicForce>()
            .Which.Magnitude.Should().Be(2500);                // 0.5 × 0.5 → 0.25 → 2500

        // Envelope shape identical across the two calls — only magnitude scales.
        atFull.Envelope.AttackTime.Should().Be(100_000);
        atHalf.Envelope.AttackTime.Should().Be(100_000);
        atFull.Envelope.FadeTime.Should().Be(100_000);
        atHalf.Envelope.FadeTime.Should().Be(100_000);
    }

    [Fact]
    public void BuildEffectParametersStillRejectsComposite()
    {
        // T-28 narrowed the EffectType-rejection throw to Composite-only (PeriodicWithEnvelope is now
        // a supported switch arm). Composite remains out of scope per T-07.md non-goals (Phase 2+).
        var effect = AnEffect(effectType: ForceEffectType.Composite);

        var act = () => VorticeDirectInputDevice.BuildEffectParameters(effect, finalIntensity: 0.5);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Composite is out of scope per T-07.md non-goals (Phase 2+)*");
    }

    // ── HandleStopAllAsync defensive-narrowing regression (Issue #27 Pass 1) ───────────────────
    // Three tests pinning the device-level catch around _device?.SendForceFeedbackCommand(StopAll)
    // at HandleStopAllAsync line 449:
    //   1. classified NeedsReacquire (0x80040205) is swallowed and logged at Information level
    //      ("device contended; stop is a no-op");
    //   2. classified-Fatal DEVICE_NOT_CONNECTED (0x8007048F — discriminated at the catch site by
    //      raw HRESULT value, NOT a new classification class per plan D1's locked design call) is
    //      swallowed and logged at Information level ("device disconnected; stop is a no-op");
    //   3. non-SharpGenException exceptions propagate — the narrow catch must NOT widen to
    //      `catch (Exception)`, which would mask programmer-error shapes like InvalidOperationException.
    // The narrow catch sits at the call site rather than the FallbackForceFeedbackDevice
    // orchestration-level catch so HRESULT classification can produce shutdown-appropriate log
    // levels instead of the error-styled "DI...stop all failed" tone the fallback-level catch emits.

    [Fact]
    public async Task StopAllSwallowsNotExclusiveAcquiredWithoutRethrow()
    {
        var recordingLogger = new RecordingLogger();
        var (sut, _, device) = await AnInitializedSutAsync(logger: recordingLogger);
        // InitializeAsync emitted one Information entry (DeviceInitialized). Clear so the post-stop
        // assertion expresses "the stop call logged exactly one Information entry" directly, rather
        // than "init + stop together logged exactly two".
        recordingLogger.Entries.Clear();
        device
            .When(d => d.SendForceFeedbackCommand(ForceFeedbackCommand.StopAll))
            .Do(_ => throw ASharpGenException(HrNotExclusiveAcquired));

        var act = async () => await sut.StopAllAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        recordingLogger.Entries.Should().ContainSingle().Which.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task StopAllSwallowsDeviceNotConnectedWithoutRethrow()
    {
        // The classifier returns Fatal for 0x8007048F via its default arm — DEVICE_NOT_CONNECTED is
        // not in the recognized DIERR_* set. The catch site at HandleStopAllAsync line 449
        // discriminates by raw HRESULT value to produce the "device disconnected; stop is a no-op"
        // Information disposition instead of the other-Fatal Warning disposition.
        var recordingLogger = new RecordingLogger();
        var (sut, _, device) = await AnInitializedSutAsync(logger: recordingLogger);
        recordingLogger.Entries.Clear();
        device
            .When(d => d.SendForceFeedbackCommand(ForceFeedbackCommand.StopAll))
            .Do(_ => throw ASharpGenException(HrDeviceNotConnected));

        var act = async () => await sut.StopAllAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        recordingLogger.Entries.Should().ContainSingle().Which.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task StopAllRethrowsUnclassifiedException()
    {
        // The other half of the contract: catch (SharpGenException), NOT catch (Exception). A
        // genuine programmer error (e.g., InvalidOperationException) must propagate so it surfaces
        // rather than being swallowed as if it were a benign DirectInput failure. Zero post-clear
        // log entries proves the narrow catch did not even consider this exception type — a widened
        // catch would log here and fail this assertion.
        var recordingLogger = new RecordingLogger();
        var (sut, _, device) = await AnInitializedSutAsync(logger: recordingLogger);
        recordingLogger.Entries.Clear();
        device
            .When(d => d.SendForceFeedbackCommand(ForceFeedbackCommand.StopAll))
            .Do(_ => throw new InvalidOperationException("programmer error"));

        var act = async () => await sut.StopAllAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        recordingLogger.Entries.Should().BeEmpty();
    }
}
