using System.IO;
using FluentAssertions;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Sensors;
using Moza.ScLink.Fusion.Rules;
using static Moza.ScLink.Fusion.Tests.RuleJson;

namespace Moza.ScLink.Fusion.Tests;

public sealed class FusionEngineTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"moza-rules-engine-{Guid.NewGuid():N}.json");

    private static SensorEvent Event(
        string eventType,
        DateTimeOffset timestamp,
        SensorKind kind = SensorKind.Log,
        double intensity = 0.5,
        string sensorId = "log.game-log") =>
        new()
        {
            EventId = Guid.NewGuid().ToString(),
            SensorId = sensorId,
            SensorKind = kind,
            EventType = eventType,
            Timestamp = timestamp,
            Intensity = intensity,
        };

    [Fact]
    public void SingleSensorRuleFiresOnMatch()
    {
        Write(_path, Document(1, Rule(
            "log-only-weapon-fire", "WeaponFireGeneric", "weapon-fire", 0, 0.6,
            Requirement("Log", "log.weapon_fire"))));
        using var rules = new RuleLibrary(_path, FastDebounce);
        var bus = new EventBus();
        var engine = new FusionEngine(bus, rules);

        engine.ProcessEvent(Event("log.weapon_fire", T0, intensity: 0.8));

        bus.GameEventReader.TryRead(out var gameEvent).Should().BeTrue();
        // Distinguishing claim: the RIGHT rule fired (not merely "something fired").
        gameEvent!.EventType.Should().Be(GameEventType.WeaponFireGeneric);
        gameEvent.Confidence.Should().Be(1.0);
        gameEvent.Intensity.Should().Be(0.8);
        gameEvent.ReasonCodes.Should().ContainSingle().Which.Should().Be("log-only-weapon-fire");
        gameEvent.Sources.Should().ContainSingle().Which.Should().Be("log.game-log");
    }

    [Theory]
    [InlineData("log.quantum_spool_start", GameEventType.QuantumSpoolStarted)]
    [InlineData("log.quantum_spool_end", GameEventType.QuantumSpoolEnded)]
    [InlineData("log.quantum_jump_exit", GameEventType.QuantumJumpExit)]
    [InlineData("log.atmosphere_entered", GameEventType.AtmosphereEntered)]
    [InlineData("log.atmosphere_exited", GameEventType.AtmosphereExited)]
    [InlineData("log.landing_impact_candidate", GameEventType.LandingGearContact)]
    [InlineData("log.weapon_fire", GameEventType.WeaponFireGeneric)]
    [InlineData("log.vehicle_destruction", GameEventType.VehicleDestruction)]
    public void EachShippedRuleFiresOnItsMatchingEvent(string eventType, GameEventType expected)
    {
        // Acceptance #1: every shipped Phase 1 rule fires the correct GameEvent given its matching evidence.
        using var rules = RuleLibrary.LoadDefault();
        var bus = new EventBus();
        var engine = new FusionEngine(bus, rules);

        engine.ProcessEvent(Event(eventType, T0));

        bus.GameEventReader.TryRead(out var gameEvent).Should().BeTrue();
        gameEvent!.EventType.Should().Be(expected);
        bus.GameEventReader.TryRead(out _).Should().BeFalse();  // exactly one rule matched
    }

    [Fact]
    public void RuleDoesNotFireBelowMinConfidence()
    {
        // Two REQUIRED requirements; only one matches -> confidence 0.5 < 0.6.
        Write(_path, Document(1, Rule(
            "two-sensor", "HullDamage", "hull", 1000, 0.6,
            Requirement("Log", "log.a"),
            Requirement("Audio", "audio.b"))));
        using var rules = new RuleLibrary(_path, FastDebounce);
        var bus = new EventBus();
        var engine = new FusionEngine(bus, rules);

        engine.ProcessEvent(Event("log.a", T0));

        bus.GameEventReader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void SuppressionKeyDebouncesWithinWindow()
    {
        // Preserves the legacy 750 ms landing-impact debounce: first wins, then suppress within window.
        Write(_path, Document(1, Rule(
            "log-only-landing-impact", "LandingGearContact", "landing-impact", 750, 0.6,
            Requirement("Log", "log.landing_impact_candidate"))));
        using var rules = new RuleLibrary(_path, FastDebounce);
        var bus = new EventBus();
        var engine = new FusionEngine(bus, rules);

        engine.ProcessEvent(Event("log.landing_impact_candidate", T0));
        engine.ProcessEvent(Event("log.landing_impact_candidate", T0.AddMilliseconds(500)));

        // Exactly one fired; the second (within 750 ms) was suppressed — not zero, not two.
        bus.GameEventReader.TryRead(out var first).Should().BeTrue();
        first!.EventType.Should().Be(GameEventType.LandingGearContact);
        bus.GameEventReader.TryRead(out _).Should().BeFalse();
        engine.Metrics["log-only-landing-impact"].Should().Be(new FusionRuleMetrics(Firings: 1, Suppressions: 1));
    }

    [Fact]
    public void SuppressionKeyAllowsDifferentKeysThrough()
    {
        // Two rules, distinct suppression keys, both windowed: a fire on one must not suppress the other.
        Write(_path, Document(1,
            Rule("rule-landing", "LandingGearContact", "landing-impact", 750, 0.6,
                Requirement("Log", "log.landing_impact_candidate")),
            Rule("rule-weapon", "WeaponFireGeneric", "weapon-fire", 750, 0.6,
                Requirement("Log", "log.weapon_fire"))));
        using var rules = new RuleLibrary(_path, FastDebounce);
        var bus = new EventBus();
        var engine = new FusionEngine(bus, rules);

        engine.ProcessEvent(Event("log.landing_impact_candidate", T0));
        engine.ProcessEvent(Event("log.weapon_fire", T0.AddMilliseconds(100)));  // within the other rule's window

        bus.GameEventReader.TryRead(out var first).Should().BeTrue();
        first!.EventType.Should().Be(GameEventType.LandingGearContact);
        bus.GameEventReader.TryRead(out var second).Should().BeTrue();
        second!.EventType.Should().Be(GameEventType.WeaponFireGeneric);  // different key passed through
    }

    [Fact]
    public void EvidenceWindowExpiresOldEvidence()
    {
        // Two required sensors within a 500 ms window. Evidence older than the window cannot corroborate.
        Write(_path, Document(1, Rule(
            "corroborated", "HullDamage", "hull", 500, 0.6,
            Requirement("Log", "log.a"),
            Requirement("Audio", "audio.b"))));
        using var rules = new RuleLibrary(_path, FastDebounce);
        var bus = new EventBus();
        var engine = new FusionEngine(bus, rules);

        // A at T0, B 800 ms later: A has expired by the time B arrives -> no corroboration.
        engine.ProcessEvent(Event("log.a", T0));
        engine.ProcessEvent(Event("audio.b", T0.AddMilliseconds(800), kind: SensorKind.Audio));
        bus.GameEventReader.TryRead(out _).Should().BeFalse();

        // A and B at the same later instant (within the window): corroboration succeeds.
        var t1 = T0.AddSeconds(10);
        engine.ProcessEvent(Event("log.a", t1));
        engine.ProcessEvent(Event("audio.b", t1, kind: SensorKind.Audio));
        bus.GameEventReader.TryRead(out var fired).Should().BeTrue();
        fired!.EventType.Should().Be(GameEventType.HullDamage);
    }

    [Fact]
    public async Task HotReloadPicksUpNewRules()
    {
        Write(_path, Document(1, Rule(
            "v1-weapon", "WeaponFireGeneric", "weapon-fire", 0, 0.6,
            Requirement("Log", "log.weapon_fire"))));
        using var rules = new RuleLibrary(_path, FastDebounce);
        var bus = new EventBus();
        var engine = new FusionEngine(bus, rules);

        engine.ProcessEvent(Event("log.weapon_fire", T0));
        bus.GameEventReader.TryRead(out var before).Should().BeTrue();
        before!.EventType.Should().Be(GameEventType.WeaponFireGeneric);

        // Replace the ruleset: the weapon rule is gone, a vehicle-destruction rule takes its place.
        Write(_path, Document(1, Rule(
            "v2-vehicle", "VehicleDestruction", "vehicle-destruction", 0, 0.6,
            Requirement("Log", "log.vehicle_destruction"))));
        await WaitUntilAsync(
            () => rules.Current.Count == 1 && rules.Current[0].RuleId == "v2-vehicle",
            TimeSpan.FromSeconds(2));

        // New rule fires; the retired rule no longer does.
        engine.ProcessEvent(Event("log.vehicle_destruction", T0.AddSeconds(1)));
        engine.ProcessEvent(Event("log.weapon_fire", T0.AddSeconds(1)));
        bus.GameEventReader.TryRead(out var after).Should().BeTrue();
        after!.EventType.Should().Be(GameEventType.VehicleDestruction);
        bus.GameEventReader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsyncConsumesBusAndPublishes()
    {
        // Covers the hosted-service path: bus drain + ProcessEvent + cooperative cancellation on stop.
        Write(_path, Document(1, Rule(
            "log-only-weapon-fire", "WeaponFireGeneric", "weapon-fire", 0, 0.6,
            Requirement("Log", "log.weapon_fire"))));
        using var rules = new RuleLibrary(_path, FastDebounce);
        var bus = new EventBus();
        var engine = new FusionEngine(bus, rules);

        await engine.StartAsync(CancellationToken.None);
        bus.SensorEvents.TryWrite(Event("log.weapon_fire", T0)).Should().BeTrue();
        await WaitUntilAsync(() => bus.GameEventReader.Count > 0, TimeSpan.FromSeconds(2));
        await engine.StopAsync(CancellationToken.None);

        bus.GameEventReader.TryRead(out var gameEvent).Should().BeTrue();
        gameEvent!.EventType.Should().Be(GameEventType.WeaponFireGeneric);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
        }
    }
}
