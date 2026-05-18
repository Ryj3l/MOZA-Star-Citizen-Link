using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;
using NSubstitute;
using SharpGen.Runtime;
using LegacyForceEffect = Moza.ScLink.Core.Models.ForceEffect;
using NewDevice = Moza.ScLink.Core.Devices.IForceFeedbackDevice;

// The entire purpose of this file is to exercise the [Obsolete] LegacyForceFeedbackDeviceAdapter, so the
// CS0618 obsolete-usage warning is disabled file-wide rather than peppered at every instantiation.
#pragma warning disable CS0618

namespace Moza.ScLink.DirectInput.Tests;

/// <summary>
/// Unit tests for <see cref="LegacyForceFeedbackDeviceAdapter"/> — the T-07 M10 transitional shim that
/// implements the legacy <see cref="Moza.ScLink.Core.IForceFeedbackDevice"/> over the new
/// <see cref="NewDevice"/>. Covers the §H tests 27–33 (translation, stop, stop-all, initialize, prepare)
/// plus two operator-authorized scope-expansion tests — 8: the classified-failure swallow path;
/// 9: the non-classified-exception propagation path (the other half of the Q1 stop-path contract).
/// </summary>
public sealed class LegacyForceFeedbackDeviceAdapterTests
{
    private static NewDevice ASubstituteDevice() => Substitute.For<NewDevice>();

    private static LegacyForceEffect ALegacyEffect(
        ForceEffectKind kind = ForceEffectKind.PeriodicVibration,
        string name = "fx.test",
        double intensity = 0.5,
        TimeSpan? duration = null,
        double frequencyHz = 30.0,
        string? stateKey = null) =>
        new(kind, name, intensity, duration ?? TimeSpan.FromMilliseconds(250), frequencyHz, stateKey);

    private static LegacyForceFeedbackDeviceAdapter AnAdapter(
        NewDevice device,
        ILogger<LegacyForceFeedbackDeviceAdapter>? logger = null) =>
        new(device, logger ?? NullLogger<LegacyForceFeedbackDeviceAdapter>.Instance);

    private static SharpGenException ASharpGenException() =>
        // 0x80040205 = DIERR_NOTEXCLUSIVEACQUIRED. The exact HRESULT is irrelevant here — the adapter
        // catches SharpGenException unconditionally, without classifying it — but a real DIERR keeps the
        // fixture honest.
        new(new Result(unchecked((int)0x80040205)), "DI test failure", innerException: null);

    // Minimal ILogger that records the level of every entry — mirrors the RecordingLogger in
    // VorticeDirectInputDeviceTests, avoiding an NSubstitute open-generic Log() verification dance.
    private sealed class RecordingLogger : ILogger<LegacyForceFeedbackDeviceAdapter>
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

    // ── §H tests 27–33: legacy → ForceCommand translation and delegation ─────────────────────

    [Fact]
    public async Task PlayAsyncWithPeriodicVibrationKindEmitsPlayEffectCommandWithPeriodicEffectType()
    {
        var device = ASubstituteDevice();
        ForceCommand? captured = null;
        _ = device.ExecuteAsync(Arg.Do<ForceCommand>(c => captured = c), Arg.Any<CancellationToken>());
        var adapter = AnAdapter(device);
        var effect = ALegacyEffect(ForceEffectKind.PeriodicVibration, name: "Quantum spool", intensity: 0.42);

        await adapter.PlayAsync(effect, CancellationToken.None);

        var play = captured.Should().BeOfType<PlayEffectCommand>().Subject;
        play.Effect.EffectType.Should().Be(ForceEffectType.Periodic);
        play.Effect.EffectId.Should().Be("Quantum spool");
        play.Effect.Category.Should().Be(EffectCategory.System);
        play.FinalIntensity.Should().Be(0.42);
        play.Effect.BaseIntensity.Should().Be(0.42);
    }

    [Fact]
    public async Task PlayAsyncWithBumpKindEmitsPlayEffectCommandWithConstantForceEffectType()
    {
        var device = ASubstituteDevice();
        ForceCommand? captured = null;
        _ = device.ExecuteAsync(Arg.Do<ForceCommand>(c => captured = c), Arg.Any<CancellationToken>());
        var adapter = AnAdapter(device);

        await adapter.PlayAsync(ALegacyEffect(ForceEffectKind.Bump, stateKey: null), CancellationToken.None);

        var play = captured.Should().BeOfType<PlayEffectCommand>().Subject;
        play.Effect.EffectType.Should().Be(ForceEffectType.ConstantForce);
        play.Effect.IsSustained.Should().BeFalse();
    }

    [Fact]
    public async Task PlayAsyncWithStateVibrationKindEmitsPlayEffectCommandWithPeriodicAndIsSustainedTrue()
    {
        var device = ASubstituteDevice();
        ForceCommand? captured = null;
        _ = device.ExecuteAsync(Arg.Do<ForceCommand>(c => captured = c), Arg.Any<CancellationToken>());
        var adapter = AnAdapter(device);

        await adapter.PlayAsync(
            ALegacyEffect(ForceEffectKind.StateVibration, stateKey: "atmosphere", duration: TimeSpan.Zero),
            CancellationToken.None);

        var play = captured.Should().BeOfType<PlayEffectCommand>().Subject;
        play.Effect.EffectType.Should().Be(ForceEffectType.Periodic);
        play.Effect.IsSustained.Should().BeTrue();
        play.Effect.StateKey.Should().Be("atmosphere");
    }

    [Fact]
    public async Task StopAsyncEmitsStopEffectCommandWithStateKey()
    {
        var device = ASubstituteDevice();
        ForceCommand? captured = null;
        _ = device.ExecuteAsync(Arg.Do<ForceCommand>(c => captured = c), Arg.Any<CancellationToken>());
        var adapter = AnAdapter(device);

        await adapter.StopAsync("quantum-spool", CancellationToken.None);

        var stop = captured.Should().BeOfType<StopEffectCommand>().Subject;
        stop.StateKey.Should().Be("quantum-spool");
        await device.Received(1).ExecuteAsync(Arg.Any<StopEffectCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAllAsyncForwardsToWrappedDevice()
    {
        // §H locks this test's name as StopAllAsyncEmitsStopAllCommand; renamed because the routing
        // decision (call wrapped.StopAllAsync directly) constructs no StopAllCommand. The locked name
        // predated that decision — surfaced as an internal §H spec defect in the PR description.
        var device = ASubstituteDevice();
        var adapter = AnAdapter(device);
        using var cts = new CancellationTokenSource();

        await adapter.StopAllAsync(cts.Token);

        await device.Received(1).StopAllAsync(cts.Token);
        await device.DidNotReceive().ExecuteAsync(Arg.Any<ForceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitializeAsyncForwardsToWrappedDevice()
    {
        var device = ASubstituteDevice();
        var adapter = AnAdapter(device);
        using var cts = new CancellationTokenSource();

        await adapter.InitializeAsync(cts.Token);

        await device.Received(1).InitializeAsync(cts.Token);
    }

    [Fact]
    public async Task PrepareAsyncIsNoOpInNewModel()
    {
        var device = ASubstituteDevice();
        var adapter = AnAdapter(device);
        var effects = new[]
        {
            ALegacyEffect(),
            ALegacyEffect(ForceEffectKind.Bump, name: "fx.bump"),
        };

        var act = async () => await adapter.PrepareAsync(effects, CancellationToken.None);

        await act.Should().NotThrowAsync();
        await device.DidNotReceive().ExecuteAsync(Arg.Any<ForceCommand>(), Arg.Any<CancellationToken>());
        await device.DidNotReceive().InitializeAsync(Arg.Any<CancellationToken>());
    }

    // ── §H scope expansion — tests 8 & 9 (operator-authorized, beyond the 7 locked names) ─────

    [Fact]
    public async Task StopAsyncSwallowsClassifiedDirectInputFailureAndLogsWarning()
    {
        var device = ASubstituteDevice();
        device
            .When(d => d.ExecuteAsync(Arg.Any<ForceCommand>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw ASharpGenException());
        var logger = new RecordingLogger();
        var adapter = AnAdapter(device, logger);

        var act = async () => await adapter.StopAsync("some-key", CancellationToken.None);

        await act.Should().NotThrowAsync();
        await device.Received(1).ExecuteAsync(Arg.Any<StopEffectCommand>(), Arg.Any<CancellationToken>());
        logger.Entries.Should().ContainSingle(level => level == LogLevel.Warning);
    }

    [Fact]
    public async Task StopAsyncPropagatesNonClassifiedException()
    {
        // The other half of the Q1 contract: only a classified SharpGenException is swallowed; every
        // other exception propagates. ObjectDisposedException is the realistic case — the wrapped
        // VorticeDirectInputDevice.ExecuteAsync throws exactly that from its post-dispose guard.
        var device = ASubstituteDevice();
        device
            .When(d => d.ExecuteAsync(Arg.Any<ForceCommand>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new ObjectDisposedException("VorticeDirectInputDevice"));
        var logger = new RecordingLogger();
        var adapter = AnAdapter(device, logger);

        var act = async () => await adapter.StopAsync("some-key", CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
        // The key assertion: zero log entries proves the SharpGenException catch did not even consider
        // this exception type — a catch widened to `catch (Exception)` would log here and fail this.
        logger.Entries.Should().BeEmpty();
        await device.Received(1).ExecuteAsync(Arg.Any<StopEffectCommand>(), Arg.Any<CancellationToken>());
    }

    // ── StopAllAsync defensive-narrowing parity (Issue #27 Pass 1) ──────────────────────────────
    // Restore symmetry with the existing StopAsync pair at lines 184/202: StopAllAsync gains the
    // same catch-narrow contract, pinned by the same two-test shape — swallow a classified
    // SharpGenException at Warning level; propagate every non-SharpGen exception with zero log
    // entries (so a future widening to `catch (Exception)` lands red).

    [Fact]
    public async Task StopAllAsyncSwallowsClassifiedSharpGenException()
    {
        var device = ASubstituteDevice();
        device
            .When(d => d.StopAllAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw ASharpGenException());
        var logger = new RecordingLogger();
        var adapter = AnAdapter(device, logger);

        var act = async () => await adapter.StopAllAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        await device.Received(1).StopAllAsync(Arg.Any<CancellationToken>());
        logger.Entries.Should().ContainSingle(level => level == LogLevel.Warning);
    }

    [Fact]
    public async Task StopAllAsyncPropagatesNonClassifiedException()
    {
        // Mirrors StopAsyncPropagatesNonClassifiedException (line 202): only classified
        // SharpGenException is swallowed; every other exception propagates. ObjectDisposedException
        // is the realistic case — the wrapped VorticeDirectInputDevice.StopAllAsync throws exactly
        // that from its post-dispose guard.
        var device = ASubstituteDevice();
        device
            .When(d => d.StopAllAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new ObjectDisposedException("VorticeDirectInputDevice"));
        var logger = new RecordingLogger();
        var adapter = AnAdapter(device, logger);

        var act = async () => await adapter.StopAllAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
        // Zero log entries proves the SharpGenException catch did not consider this exception type —
        // a catch widened to `catch (Exception)` would log here and fail this.
        logger.Entries.Should().BeEmpty();
        await device.Received(1).StopAllAsync(Arg.Any<CancellationToken>());
    }
}
