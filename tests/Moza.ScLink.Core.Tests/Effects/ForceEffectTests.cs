using System.Text.Json;
using FluentAssertions;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;
using ForceEffect = Moza.ScLink.Core.Effects.ForceEffect; // disambiguates from legacy Moza.ScLink.Core.Models.ForceEffect

namespace Moza.ScLink.Core.Tests.Effects;

public sealed class ForceEffectTests
{
    private static ForceEffect BuildEffect(
        string effectId = "effect.weapon_fire_ballistic",
        ForceEffectType type = ForceEffectType.ConstantForce,
        EffectCategory category = EffectCategory.Combat) => new()
    {
        EffectId = effectId,
        EffectType = type,
        Category = category,
    };

    // ── ForceEffect construction ────────────────────────────────────────────────

    [Fact]
    public void ConstructionWithRequiredPropertiesSucceeds()
    {
        var effect = BuildEffect();

        effect.EffectId.Should().Be("effect.weapon_fire_ballistic");
        effect.EffectType.Should().Be(ForceEffectType.ConstantForce);
        effect.Category.Should().Be(EffectCategory.Combat);
    }

    [Fact]
    public void DefaultValuesAreExpected()
    {
        var effect = BuildEffect();

        effect.BaseIntensity.Should().Be(0.0);
        effect.FrequencyHz.Should().Be(0.0);
        effect.Duration.Should().Be(TimeSpan.Zero);
        effect.DirectionX.Should().Be(0.0);
        effect.DirectionY.Should().Be(0.0);
        effect.Envelope.Should().BeNull();
        effect.IsSustained.Should().BeFalse();
        effect.StateKey.Should().BeNull();
    }

    [Fact]
    public void EqualityTwoInstancesWithSameValuesAreEqual()
    {
        var e1 = BuildEffect();
        var e2 = BuildEffect();

        e1.Should().Be(e2);
    }

    [Fact]
    public void EqualityTwoInstancesWithDifferentEffectIdAreNotEqual()
    {
        var e1 = BuildEffect("effect.a");
        var e2 = BuildEffect("effect.b");

        e1.Should().NotBe(e2);
    }

    [Fact]
    public void JsonRoundTripPreservesAllFields()
    {
        var original = new ForceEffect
        {
            EffectId = "effect.quantum_spool",
            EffectType = ForceEffectType.PeriodicWithEnvelope,
            Category = EffectCategory.Flight,
            BaseIntensity = 0.7,
            FrequencyHz = 80.0,
            Duration = TimeSpan.FromSeconds(2),
            DirectionX = 0.5,
            DirectionY = -0.5,
            IsSustained = true,
            StateKey = "spool-state",
            Envelope = new ForceEnvelope(
                Attack: TimeSpan.FromMilliseconds(100),
                Hold: TimeSpan.FromMilliseconds(50),
                Decay: TimeSpan.FromMilliseconds(200),
                Release: TimeSpan.FromMilliseconds(150),
                AttackLevel: 1.0,
                SustainLevel: 0.7),
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<ForceEffect>(json)!;

        restored.EffectId.Should().Be(original.EffectId);
        restored.EffectType.Should().Be(original.EffectType);
        restored.Category.Should().Be(original.Category);
        restored.BaseIntensity.Should().Be(original.BaseIntensity);
        restored.FrequencyHz.Should().Be(original.FrequencyHz);
        restored.Duration.Should().Be(original.Duration);
        restored.DirectionX.Should().Be(original.DirectionX);
        restored.DirectionY.Should().Be(original.DirectionY);
        restored.IsSustained.Should().Be(original.IsSustained);
        restored.StateKey.Should().Be(original.StateKey);
        restored.Envelope.Should().NotBeNull();
        restored.Envelope!.AttackLevel.Should().Be(1.0);
        restored.Envelope.SustainLevel.Should().Be(0.7);
    }

    [Fact]
    public void WithExpressionCreatesNewInstanceWithModifiedField()
    {
        var original = BuildEffect();
        var modified = original with { BaseIntensity = 0.9 };

        modified.BaseIntensity.Should().Be(0.9);
        modified.EffectId.Should().Be(original.EffectId);
        ReferenceEquals(original, modified).Should().BeFalse();
    }

    // ── ForceCommand hierarchy ──────────────────────────────────────────────────

    [Fact]
    public void PlayEffectCommandConstructsWithRequiredBaseProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var effect = BuildEffect();

        var cmd = new PlayEffectCommand(effect, 0.72)
        {
            CommandId = "cmd-play-1",
            IssuedAt = now,
        };

        cmd.CommandId.Should().Be("cmd-play-1");
        cmd.IssuedAt.Should().Be(now);
        cmd.Effect.Should().Be(effect);
        cmd.FinalIntensity.Should().Be(0.72);
    }

    [Fact]
    public void StopEffectCommandConstructsWithRequiredBaseProperties()
    {
        var now = DateTimeOffset.UtcNow;

        var cmd = new StopEffectCommand("spool-state")
        {
            CommandId = "cmd-stop-1",
            IssuedAt = now,
        };

        cmd.CommandId.Should().Be("cmd-stop-1");
        cmd.IssuedAt.Should().Be(now);
        cmd.StateKey.Should().Be("spool-state");
    }

    [Fact]
    public void StopAllCommandConstructsWithRequiredBaseProperties()
    {
        var now = DateTimeOffset.UtcNow;

        var cmd = new StopAllCommand()
        {
            CommandId = "cmd-stopall-1",
            IssuedAt = now,
        };

        cmd.CommandId.Should().Be("cmd-stopall-1");
        cmd.IssuedAt.Should().Be(now);
    }

    [Fact]
    public void ForceCommandsAreEqualWhenFieldsAreEqual()
    {
        var now = new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);
        var effect = BuildEffect();

        var play1 = new PlayEffectCommand(effect, 0.5) { CommandId = "c1", IssuedAt = now };
        var play2 = new PlayEffectCommand(effect, 0.5) { CommandId = "c1", IssuedAt = now };
        var stop1 = new StopEffectCommand("key") { CommandId = "c2", IssuedAt = now };
        var stop2 = new StopEffectCommand("key") { CommandId = "c2", IssuedAt = now };
        var stopAll1 = new StopAllCommand() { CommandId = "c3", IssuedAt = now };
        var stopAll2 = new StopAllCommand() { CommandId = "c3", IssuedAt = now };

        play1.Should().Be(play2);
        stop1.Should().Be(stop2);
        stopAll1.Should().Be(stopAll2);
    }

    [Fact]
    public void ForceCommandsSwitchExpressionDispatchesToCorrectDerivedType()
    {
        var now = DateTimeOffset.UtcNow;
        var effect = BuildEffect();

        ForceCommand play = new PlayEffectCommand(effect, 0.5) { CommandId = "c1", IssuedAt = now };
        ForceCommand stop = new StopEffectCommand("key") { CommandId = "c2", IssuedAt = now };
        ForceCommand stopAll = new StopAllCommand() { CommandId = "c3", IssuedAt = now };

        static string Dispatch(ForceCommand c) => c switch
        {
            PlayEffectCommand => "play",
            StopEffectCommand => "stop",
            StopAllCommand => "stopAll",
            _ => "unknown",
        };

        Dispatch(play).Should().Be("play");
        Dispatch(stop).Should().Be("stop");
        Dispatch(stopAll).Should().Be("stopAll");
    }
}
