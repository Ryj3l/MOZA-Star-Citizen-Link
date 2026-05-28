using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Effects.Tests;

public sealed class SafetyLimiterStageTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void AdmittedPlayEntersActiveAndHistory()
    {
        var limiter = new RecordingLimiter();
        var stage = new SafetyLimiterStage(limiter);
        var play = Play(0.5, Effect("alpha"), T0);

        stage.Process(play, Device());
        stage.Process(Probe(T0), Device());

        // The probe's context (the 2nd Apply call) reflects the state left by the admitted play.
        var context = limiter.Contexts[1];
        context.ActiveEffects.Should().ContainSingle().Which.CommandId.Should().Be(play.CommandId);
        context.RecentIntensityHistory.Should().ContainKey("alpha")
            .WhoseValue.Should().Be(new CommandSnapshot(0.5, T0));
    }

    [Fact]
    public void HistoryRecordsAdmittedIntensityNotRequested()
    {
        // The limiter clamps 1.0 -> 0.6; history must record the admitted (clamped) value, not the request.
        var limiter = new RecordingLimiter((cmd, _) =>
            cmd is PlayEffectCommand p ? [p with { FinalIntensity = 0.6 }] : [cmd]);
        var stage = new SafetyLimiterStage(limiter);

        stage.Process(Play(1.0, Effect("alpha"), T0), Device());
        stage.Process(Probe(T0), Device());

        limiter.Contexts[1].RecentIntensityHistory["alpha"].Intensity.Should().Be(0.6);
    }

    [Fact]
    public void RejectionLeavesActiveAndHistoryUnchanged()
    {
        var limiter = new RecordingLimiter((_, _) => []);  // reject everything
        var stage = new SafetyLimiterStage(limiter);

        var result = stage.Process(Play(0.5, Effect("alpha"), T0), Device());
        stage.Process(Probe(T0), Device());

        result.Should().BeEmpty();
        limiter.Contexts[1].ActiveEffects.Should().BeEmpty();
        limiter.Contexts[1].RecentIntensityHistory.Should().NotContainKey("alpha");
    }

    [Fact]
    public void StopByCommandIdRemovesActiveEffect()
    {
        var limiter = new RecordingLimiter();  // pass-through admit
        var stage = new SafetyLimiterStage(limiter);
        var play = Play(0.5, Effect("alpha"), T0);

        stage.Process(play, Device());
        stage.Process(new StopEffectCommand(play.CommandId) { CommandId = "s1", IssuedAt = T0 }, Device());
        stage.Process(Probe(T0), Device());

        limiter.Contexts[2].ActiveEffects.Should().BeEmpty();
    }

    [Fact]
    public void StopByStateKeyRemovesSustainedEffect()
    {
        var limiter = new RecordingLimiter();
        var stage = new SafetyLimiterStage(limiter);
        var play = Play(0.5, Effect("atmosphere", sustained: true, duration: TimeSpan.Zero), T0);

        stage.Process(play, Device());
        // A resolver-issued stop keys by the effect's StateKey ("atmosphere"), not the play's CommandId.
        stage.Process(new StopEffectCommand("atmosphere") { CommandId = "s1", IssuedAt = T0 }, Device());
        stage.Process(Probe(T0), Device());

        limiter.Contexts[2].ActiveEffects.Should().BeEmpty();
    }

    [Fact]
    public void NonSustainedEffectExpiresAfterItsDuration()
    {
        var limiter = new RecordingLimiter();
        var stage = new SafetyLimiterStage(limiter);
        var play = Play(0.5, Effect("alpha", duration: TimeSpan.FromMilliseconds(100)), T0);

        stage.Process(play, Device());
        stage.Process(Probe(T0.AddMilliseconds(200)), Device());  // past alpha's 100ms duration

        limiter.Contexts[1].ActiveEffects.Should().BeEmpty();
    }

    [Fact]
    public void SustainedEffectDoesNotExpireByTime()
    {
        var limiter = new RecordingLimiter();
        var stage = new SafetyLimiterStage(limiter);
        var play = Play(0.5, Effect("atmosphere", sustained: true, duration: TimeSpan.Zero), T0);

        stage.Process(play, Device());
        stage.Process(Probe(T0.AddHours(1)), Device());  // an hour later

        limiter.Contexts[1].ActiveEffects.Should().ContainSingle().Which.CommandId.Should().Be(play.CommandId);
    }

    [Fact]
    public void PreemptionResultRemovesPreemptedAndAddsAdmitted()
    {
        var seed = Play(0.5, Effect("old"), T0);
        var incoming = Play(0.5, Effect("incoming"), T0.AddSeconds(1));
        var admitted = incoming with { FinalIntensity = 0.5 };
        var limiter = new RecordingLimiter((cmd, _) =>
            cmd.CommandId == incoming.CommandId
                ? [new StopEffectCommand(seed.CommandId) { CommandId = "s1", IssuedAt = T0.AddSeconds(1) }, admitted]
                : [cmd]);
        var stage = new SafetyLimiterStage(limiter);

        stage.Process(seed, Device());
        var result = stage.Process(incoming, Device());
        stage.Process(Probe(T0.AddSeconds(1)), Device());

        result.Should().HaveCount(2);
        result[0].Should().BeOfType<StopEffectCommand>().Which.StateKey.Should().Be(seed.CommandId);
        var active = limiter.Contexts[2].ActiveEffects;
        active.Should().ContainSingle().Which.Effect.EffectId.Should().Be("incoming");
    }

    [Fact]
    public void ContextCarriesEventTimeAndDevice()
    {
        var limiter = new RecordingLimiter();
        var stage = new SafetyLimiterStage(limiter);
        var device = Device(maxIntensity: 0.85);

        stage.Process(Play(0.5, Effect("alpha"), T0.AddMinutes(3)), device);

        limiter.Contexts[0].Now.Should().Be(T0.AddMinutes(3));
        limiter.Contexts[0].Device.Should().Be(device);
    }

    [Fact]
    public void StopAllCommandPassesThroughWithoutMutatingActiveSet()
    {
        var limiter = new RecordingLimiter();
        var stage = new SafetyLimiterStage(limiter);
        var play = Play(0.5, Effect("alpha"), T0);

        stage.Process(play, Device());
        // StopAllCommand carries no per-effect state for this stage (its clear-all is a T-16 concern).
        var result = stage.Process(new StopAllCommand { CommandId = "all", IssuedAt = T0 }, Device());
        stage.Process(Probe(T0), Device());

        result.Should().ContainSingle().Which.Should().BeOfType<StopAllCommand>();
        limiter.Contexts[2].ActiveEffects.Should().ContainSingle().Which.CommandId.Should().Be(play.CommandId);
    }

    [Fact]
    public void RealLimiterClampsOverCapIntensityEndToEnd()
    {
        // Smoke test against the real policy: an over-ceiling intensity is admitted at the device cap.
        var stage = new SafetyLimiterStage(new SafetyLimiter(new NullLogger<SafetyLimiter>()));

        var result = stage.Process(Play(1.5, Effect("alpha"), T0), Device(maxIntensity: 0.85));

        result.Should().ContainSingle().Which.Should().BeOfType<PlayEffectCommand>()
            .Which.FinalIntensity.Should().Be(0.85);
    }

    [Fact]
    public void ConstructorRejectsNullLimiter() =>
        ((Action)(() => _ = new SafetyLimiterStage(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void ProcessRejectsNullCommand()
    {
        var stage = new SafetyLimiterStage(new RecordingLimiter());
        ((Action)(() => stage.Process(null!, Device()))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ProcessRejectsNullDevice()
    {
        var stage = new SafetyLimiterStage(new RecordingLimiter());
        ((Action)(() => stage.Process(Play(0.5), null!))).Should().Throw<ArgumentNullException>();
    }

    private static ForceEffect Effect(string id = "test-effect", bool sustained = false, TimeSpan? duration = null) =>
        new()
        {
            EffectId = id,
            EffectType = ForceEffectType.Periodic,
            Category = EffectCategory.Combat,
            BaseIntensity = 0.5,
            Duration = duration ?? TimeSpan.FromSeconds(1),
            IsSustained = sustained,
            StateKey = sustained ? id : null,
        };

    private static PlayEffectCommand Play(double intensity, ForceEffect? effect = null, DateTimeOffset? at = null) =>
        new(effect ?? Effect(), intensity)
        {
            CommandId = Guid.NewGuid().ToString(),
            IssuedAt = at ?? T0,
        };

    private static DeviceCapabilities Device(double maxIntensity = 1.0) =>
        new(DeviceModel.MozaAb6, 2, 4, true, true, true, 10_000, maxIntensity);

    // A no-op command whose only purpose is to make the stage build (and the limiter capture) a fresh context
    // reflecting current state at the given time. Its key matches nothing, so it mutates no state.
    private static StopEffectCommand Probe(DateTimeOffset at) =>
        new("__probe__") { CommandId = "__probe__", IssuedAt = at };

    private sealed class RecordingLimiter : ISafetyLimiter
    {
        private readonly Func<ForceCommand, SafetyContext, IReadOnlyList<ForceCommand>> _script;

        public RecordingLimiter(Func<ForceCommand, SafetyContext, IReadOnlyList<ForceCommand>>? script = null) =>
            _script = script ?? ((cmd, _) => [cmd]);

        public List<SafetyContext> Contexts { get; } = [];

        public IReadOnlyList<ForceCommand> Apply(ForceCommand command, SafetyContext context)
        {
            Contexts.Add(context);
            return _script(command, context);
        }
    }
}
