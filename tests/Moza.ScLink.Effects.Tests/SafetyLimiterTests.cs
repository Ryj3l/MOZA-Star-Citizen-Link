using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;

// Disambiguate the §5 domain ForceEffect (wrapped by PlayEffectCommand) from the legacy Core.Models.ForceEffect
// (CS0104) — same alias the resolver uses.
using ForceEffect = Moza.ScLink.Core.Effects.ForceEffect;

namespace Moza.ScLink.Effects.Tests;

public sealed class SafetyLimiterTests
{
    // Spec test names (deliverable #5) use underscores as notation; CA1707 forbids them in identifiers, so the
    // tests are PascalCase. Name map (spec -> here):
    //   Clamps_Intensity_Above_DeviceMax                  -> ClampsIntensityAboveDeviceMax
    //   Caps_Sustained_Intensity_At_Point_Seven           -> CapsSustainedIntensityAtPointSeven
    //   Enforces_RateOfChange_Limit                        -> EnforcesRateOfChangeLimit
    //   Preempts_Oldest_NonSustained_When_AtMaxSimultaneous-> PreemptsOldestNonSustainedWhenAtMaxSimultaneous
    //   Caps_Duration_At_AbsoluteMax                       -> CapsDurationAtAbsoluteMax
    //   Passes_Through_Commands_Below_Limits               -> PassesThroughCommandsBelowLimits
    //   Logs_Warning_On_Each_Clamp                         -> LogsWarningOnEachClamp

    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void ClampsIntensityAboveDeviceMax()
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);

        var result = limiter.Apply(Play(1.5), Context(device: Device(maxIntensity: 0.85)));

        var play = result.Should().ContainSingle().Which.Should().BeOfType<PlayEffectCommand>().Subject;
        play.FinalIntensity.Should().Be(0.85);
        logger.Warnings.Should().ContainSingle().Which.Name.Should().Be("DeviceCapClamp");
    }

    [Fact]
    public void CapsSustainedIntensityAtPointSeven()
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);
        var effect = Effect(sustained: true, duration: TimeSpan.Zero);

        // Device ceiling 1.0 so the device clamp does not fire — isolates the sustained cap as the only change.
        var result = limiter.Apply(Play(0.9, effect), Context(device: Device(maxIntensity: 1.0)));

        var play = result.Should().ContainSingle().Which.Should().BeOfType<PlayEffectCommand>().Subject;
        play.FinalIntensity.Should().Be(0.7);
        logger.Warnings.Should().ContainSingle().Which.Name.Should().Be("SustainedCap");
    }

    [Fact]
    public void EnforcesRateOfChangeLimit()
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);
        var history = History("test-effect", intensity: 0.0, at: T0.AddMilliseconds(-100));

        // 0.0 -> 1.0 over 0.1s is 10/s; the 4.0/s cap allows only 0.4 of change in 0.1s, so it admits 0.4.
        var result = limiter.Apply(Play(1.0, at: T0), Context(device: Device(1.0), now: T0, history: history));

        var play = result.Should().ContainSingle().Which.Should().BeOfType<PlayEffectCommand>().Subject;
        play.FinalIntensity.Should().BeApproximately(0.4, 1e-9);
        logger.Warnings.Should().ContainSingle().Which.Name.Should().Be("RateClamp");
    }

    [Fact]
    public void PreemptsOldestNonSustainedWhenAtMaxSimultaneous()
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);

        var sustainedOldest = Play(0.5, Effect("sus-a", sustained: true, duration: TimeSpan.Zero), T0);
        var nonSustainedOldest = Play(0.5, Effect("non-a"), T0.AddSeconds(1));
        var nonSustainedNewer = Play(0.5, Effect("non-b"), T0.AddSeconds(2));
        var sustainedNewer = Play(0.5, Effect("sus-b", sustained: true, duration: TimeSpan.Zero), T0.AddSeconds(3));
        // Deliberately unordered to prove selection is by IssuedAt, not list position.
        var active = new[] { nonSustainedNewer, sustainedNewer, nonSustainedOldest, sustainedOldest };
        var incoming = Play(0.5, Effect("incoming"), T0.AddSeconds(4));

        var result = limiter.Apply(incoming, Context(device: Device(1.0), active: active));

        result.Should().HaveCount(2);
        // Stop precedes play, and it targets the OLDEST non-sustained (non-a), keyed by its CommandId.
        result[0].Should().BeOfType<StopEffectCommand>().Which.StateKey.Should().Be(nonSustainedOldest.CommandId);
        result[1].Should().BeOfType<PlayEffectCommand>().Which.Effect.EffectId.Should().Be("incoming");
        logger.Warnings.Should().ContainSingle().Which.Name.Should().Be("Preempt");
    }

    [Fact]
    public void CapsDurationAtAbsoluteMax()
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);
        var effect = Effect(duration: TimeSpan.FromMinutes(15));

        var result = limiter.Apply(Play(0.5, effect), Context(device: Device(1.0)));

        var play = result.Should().ContainSingle().Which.Should().BeOfType<PlayEffectCommand>().Subject;
        play.Effect.Duration.Should().Be(TimeSpan.FromMinutes(10));
        play.FinalIntensity.Should().Be(0.5, "duration capping leaves intensity untouched");
        logger.Warnings.Should().ContainSingle().Which.Name.Should().Be("DurationCap");
    }

    [Fact]
    public void PassesThroughCommandsBelowLimits()
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);
        var command = Play(0.5);

        var result = limiter.Apply(command, Context(device: Device(1.0)));

        result.Should().ContainSingle().Which.Should().Be(command);
        logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void LogsWarningOnEachClamp()
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);
        var effect = Effect(sustained: true, duration: TimeSpan.Zero);

        // One command trips two limits: device-cap clamp (1.5 -> 0.85) then sustained cap (0.85 -> 0.7).
        var result = limiter.Apply(Play(1.5, effect), Context(device: Device(maxIntensity: 0.85)));

        result.Should().ContainSingle().Which.Should().BeOfType<PlayEffectCommand>()
            .Which.FinalIntensity.Should().Be(0.7);
        logger.Entries.Should().OnlyContain(e => e.Level == LogLevel.Warning);
        logger.Warnings.Select(w => w.Name).Should().Equal("DeviceCapClamp", "SustainedCap");
    }

    [Fact]
    public void RejectsAtCapWhenAllActiveEffectsAreSustained()
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);
        var active = Enumerable.Range(0, 4)
            .Select(i => Play(0.5, Effect($"sus-{i}", sustained: true, duration: TimeSpan.Zero), T0.AddSeconds(i)))
            .ToArray();

        var result = limiter.Apply(Play(0.5, Effect("incoming")), Context(device: Device(1.0), active: active));

        result.Should().BeEmpty();
        logger.Warnings.Should().ContainSingle().Which.Name.Should().Be("RejectedAtCap");
    }

    [Fact]
    public void RejectsWhenIntensityIsAtTheFloor()
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);

        var result = limiter.Apply(Play(0.0), Context(device: Device(1.0)));

        result.Should().BeEmpty();
        logger.Warnings.Should().ContainSingle().Which.Name.Should().Be("RejectedZeroIntensity");
    }

    [Fact]
    public void RejectsNegativeIntensityAfterClampingToFloor()
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);

        var result = limiter.Apply(Play(-0.5), Context(device: Device(1.0)));

        result.Should().BeEmpty();
        // Negative is first clamped to the floor (device-cap clamp), then rejected as a zero-force command.
        logger.Warnings.Select(w => w.Name).Should().Equal("DeviceCapClamp", "RejectedZeroIntensity");
    }

    [Fact]
    public void SkipsRateLimitWhenNoElapsedTime()
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);
        var history = History("test-effect", intensity: 0.0, at: T0);

        // Prior snapshot at the same instant -> Δtime == 0 -> rate is undefined -> pass through unchanged.
        var result = limiter.Apply(Play(1.0, at: T0), Context(device: Device(1.0), now: T0, history: history));

        result.Should().ContainSingle().Which.Should().BeOfType<PlayEffectCommand>()
            .Which.FinalIntensity.Should().Be(1.0);
        logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotRateLimitChangesWithinTheLimit()
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);
        var history = History("test-effect", intensity: 0.5, at: T0.AddSeconds(-1));

        // 0.5 -> 0.6 over 1s is 0.1/s, well under the 4.0/s cap -> no clamp.
        var result = limiter.Apply(Play(0.6, at: T0), Context(device: Device(1.0), now: T0, history: history));

        result.Should().ContainSingle().Which.Should().BeOfType<PlayEffectCommand>()
            .Which.FinalIntensity.Should().Be(0.6);
        logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void PassesThroughStopCommandsUnchanged()
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);
        var stop = new StopEffectCommand("quantum-spool") { CommandId = "c1", IssuedAt = T0 };

        var result = limiter.Apply(stop, Context());

        result.Should().ContainSingle().Which.Should().BeSameAs(stop);
        logger.Warnings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, 0, 0.9, 0.7)]       // sustained, Duration == Zero, above ceiling -> capped
    [InlineData(true, 10_000, 0.9, 0.7)]  // sustained, Duration 10s (> 5s), above ceiling -> capped
    [InlineData(true, 3_000, 0.9, 0.9)]   // sustained, Duration 3s (< 5s, not zero) -> not capped
    [InlineData(true, 0, 0.6, 0.6)]       // sustained but already below ceiling -> not capped
    [InlineData(false, 1_000, 0.9, 0.9)]  // not sustained -> never capped
    public void SustainedCapAppliesOnlyToLongSustainedEffectsAboveCeiling(
        bool sustained, int durationMs, double intensity, double expected)
    {
        var logger = new RecordingLogger();
        var limiter = new SafetyLimiter(logger);
        var effect = Effect(sustained: sustained, duration: TimeSpan.FromMilliseconds(durationMs));

        var result = limiter.Apply(Play(intensity, effect), Context(device: Device(1.0)));

        result.Should().ContainSingle().Which.Should().BeOfType<PlayEffectCommand>()
            .Which.FinalIntensity.Should().Be(expected);
    }

    [Fact]
    public void ConstructorRejectsNullLogger() =>
        ((Action)(() => _ = new SafetyLimiter(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void ApplyRejectsNullCommand()
    {
        var limiter = new SafetyLimiter(new RecordingLogger());
        ((Action)(() => limiter.Apply(null!, Context()))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ApplyRejectsNullContext()
    {
        var limiter = new SafetyLimiter(new RecordingLogger());
        ((Action)(() => limiter.Apply(Play(0.5), null!))).Should().Throw<ArgumentNullException>();
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

    private static Dictionary<string, CommandSnapshot> History(string effectId, double intensity, DateTimeOffset at) =>
        new() { [effectId] = new CommandSnapshot(intensity, at) };

    private static SafetyContext Context(
        DeviceCapabilities? device = null,
        IReadOnlyList<PlayEffectCommand>? active = null,
        DateTimeOffset? now = null,
        IReadOnlyDictionary<string, CommandSnapshot>? history = null) =>
        new(
            active ?? Array.Empty<PlayEffectCommand>(),
            device ?? Device(),
            now ?? T0,
            history ?? new Dictionary<string, CommandSnapshot>());

    private sealed class RecordingLogger : ILogger<SafetyLimiter>
    {
        private readonly List<(LogLevel Level, EventId EventId)> _entries = [];

        public IReadOnlyList<(LogLevel Level, EventId EventId)> Entries => _entries;

        public IReadOnlyList<EventId> Warnings =>
            _entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.EventId).ToList();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _entries.Add((logLevel, eventId));
    }
}
