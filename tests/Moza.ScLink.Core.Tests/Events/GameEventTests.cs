using System.Text.Json;
using FluentAssertions;
using Moza.ScLink.Core.Events;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Core.Tests.Events;

public sealed class GameEventTests
{
    private static readonly DateTimeOffset FixedTs = new(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);

    private static GameEvent BuildMinimal(string? eventId = null) => new()
    {
        EventId = eventId ?? Guid.NewGuid().ToString(),
        EventType = GameEventType.WeaponFireBallistic,
        Timestamp = FixedTs,
    };

    [Fact]
    public void ConstructionWithRequiredPropertiesSucceeds()
    {
        var id = Guid.NewGuid().ToString();
        var evt = new GameEvent
        {
            EventId = id,
            EventType = GameEventType.QuantumSpoolStarted,
            Timestamp = FixedTs,
        };

        evt.EventId.Should().Be(id);
        evt.EventType.Should().Be(GameEventType.QuantumSpoolStarted);
        evt.Timestamp.Should().Be(FixedTs);
    }

    [Fact]
    public void DefaultValuesCollectionsAreEmptyScalarsAreZero()
    {
        var evt = BuildMinimal();

        evt.Sources.Should().BeEmpty();
        evt.ReasonCodes.Should().BeEmpty();
        evt.Evidence.Should().BeEmpty();
        evt.Metadata.Should().BeEmpty();
        evt.Confidence.Should().Be(0.0);
        evt.Intensity.Should().Be(0.0);
        evt.Duration.Should().BeNull();
    }

    [Fact]
    public void EqualityTwoInstancesWithSameScalarValuesAndSameDictionaryReferenceAreEqual()
    {
        var id = Guid.NewGuid().ToString();
        var evidence = ImmutableDictionary<string, double>.Empty.Add("rms", 0.9);

        var e1 = new GameEvent
        {
            EventId = id, EventType = GameEventType.HullImpact, Timestamp = FixedTs,
            Evidence = evidence,
        };
        var e2 = new GameEvent
        {
            EventId = id, EventType = GameEventType.HullImpact, Timestamp = FixedTs,
            Evidence = evidence,
        };

        e1.Should().Be(e2);
    }

    [Fact]
    public void EqualityTwoInstancesWithSameScalarValuesAndDifferentDictionaryInstancesWithSameContentAreNotEqual()
    {
        var id = Guid.NewGuid().ToString();
        var evidence1 = ImmutableDictionary<string, double>.Empty.Add("rms", 0.9);
        var evidence2 = ImmutableDictionary<string, double>.Empty.Add("rms", 0.9);

        var e1 = new GameEvent
        {
            EventId = id, EventType = GameEventType.HullImpact, Timestamp = FixedTs,
            Evidence = evidence1,
        };
        var e2 = new GameEvent
        {
            EventId = id, EventType = GameEventType.HullImpact, Timestamp = FixedTs,
            Evidence = evidence2,
        };

        e1.Should().NotBe(e2);
    }

    [Fact]
    public void EqualityTwoInstancesWithDifferentScalarValuesAreNotEqual()
    {
        var e1 = BuildMinimal("id-a");
        var e2 = BuildMinimal("id-b");

        e1.Should().NotBe(e2);
    }

    [Fact]
    public void EqualityTwoInstancesWithSameSourcesArrayContentAreEqual()
    {
        // ImmutableArray<T> is a struct; assigning the same value to two records copies the
        // same backing array reference into both, so record equality holds.
        // Contrast with ImmutableDictionary (reference type): same-reference case also equals,
        // but different-instance-same-content does not (see the Not-Equal dictionary test above).
        var id = Guid.NewGuid().ToString();
        var sources = ImmutableArray.Create("sensor-audio", "sensor-screen");

        var e1 = new GameEvent
        {
            EventId = id, EventType = GameEventType.WeaponFireEnergy, Timestamp = FixedTs,
            Sources = sources,
        };
        var e2 = new GameEvent
        {
            EventId = id, EventType = GameEventType.WeaponFireEnergy, Timestamp = FixedTs,
            Sources = sources,
        };

        e1.Should().Be(e2);
    }

    [Fact]
    public void EqualityTwoInstancesWithDifferentSourcesArrayContentAreNotEqual()
    {
        var id = Guid.NewGuid().ToString();

        var e1 = new GameEvent
        {
            EventId = id, EventType = GameEventType.WeaponFireEnergy, Timestamp = FixedTs,
            Sources = ImmutableArray.Create("sensor-audio"),
        };
        var e2 = new GameEvent
        {
            EventId = id, EventType = GameEventType.WeaponFireEnergy, Timestamp = FixedTs,
            Sources = ImmutableArray.Create("sensor-screen"),
        };

        e1.Should().NotBe(e2);
    }

    [Fact]
    public void JsonRoundTripPreservesAllScalarFields()
    {
        var id = Guid.NewGuid().ToString();
        var original = new GameEvent
        {
            EventId = id,
            EventType = GameEventType.AtmosphericBuffet,
            Timestamp = FixedTs,
            Confidence = 0.85,
            Intensity = 0.6,
            Duration = TimeSpan.FromSeconds(3),
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<GameEvent>(json)!;

        restored.EventId.Should().Be(original.EventId);
        restored.EventType.Should().Be(original.EventType);
        restored.Timestamp.Should().Be(original.Timestamp);
        restored.Confidence.Should().Be(original.Confidence);
        restored.Intensity.Should().Be(original.Intensity);
        restored.Duration.Should().Be(original.Duration);
    }

    [Fact]
    public void JsonRoundTripPreservesImmutableArrayFields()
    {
        var original = BuildMinimal() with
        {
            Sources = ImmutableArray.Create("sensor-audio", "sensor-log"),
            ReasonCodes = ImmutableArray.Create("audio.confidence_high", "log.corroborates"),
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<GameEvent>(json)!;

        restored.Sources.Should().Equal("sensor-audio", "sensor-log");
        restored.ReasonCodes.Should().Equal("audio.confidence_high", "log.corroborates");
    }

    [Fact]
    public void WithExpressionCreatesNewInstanceWithModifiedField()
    {
        var original = BuildMinimal();
        var modified = original with { Intensity = 0.88 };

        modified.Intensity.Should().Be(0.88);
        modified.EventId.Should().Be(original.EventId);
        ReferenceEquals(original, modified).Should().BeFalse();
    }
}
