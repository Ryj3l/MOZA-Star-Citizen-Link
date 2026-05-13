using System.Text.Json;
using FluentAssertions;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Sensors;

namespace Moza.ScLink.Core.Tests.Sensors;

public sealed class SensorEventTests
{
    private static readonly DateTimeOffset FixedTs = new(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);

    private static SensorEvent BuildMinimal(string? eventId = null) => new()
    {
        EventId = eventId ?? Guid.NewGuid().ToString(),
        SensorId = "audio.endpoint-loopback",
        SensorKind = SensorKind.Audio,
        EventType = "audio.weapon_fire_ballistic",
        Timestamp = FixedTs,
    };

    [Fact]
    public void ConstructionWithRequiredPropertiesSucceeds()
    {
        var id = Guid.NewGuid().ToString();
        var evt = new SensorEvent
        {
            EventId = id,
            SensorId = "log.game",
            SensorKind = SensorKind.Log,
            EventType = "log.quantum_spool",
            Timestamp = FixedTs,
        };

        evt.EventId.Should().Be(id);
        evt.SensorId.Should().Be("log.game");
        evt.SensorKind.Should().Be(SensorKind.Log);
        evt.EventType.Should().Be("log.quantum_spool");
        evt.Timestamp.Should().Be(FixedTs);
    }

    [Fact]
    public void DefaultValuesFeaturesIsEmptyMetadataIsEmpty()
    {
        var evt = BuildMinimal();

        evt.Features.Should().BeEmpty();
        evt.Metadata.Should().BeEmpty();
        evt.Confidence.Should().Be(0.0);
        evt.Intensity.Should().Be(0.0);
        evt.Duration.Should().BeNull();
    }

    [Fact]
    public void EqualityTwoInstancesWithSameScalarValuesAndSameDictionaryReferenceAreEqual()
    {
        var id = Guid.NewGuid().ToString();
        var features = ImmutableDictionary<string, double>.Empty.Add("rms", 0.8);

        var e1 = new SensorEvent
        {
            EventId = id, SensorId = "audio.endpoint-loopback", SensorKind = SensorKind.Audio,
            EventType = "audio.weapon_fire", Timestamp = FixedTs, Features = features,
        };
        var e2 = new SensorEvent
        {
            EventId = id, SensorId = "audio.endpoint-loopback", SensorKind = SensorKind.Audio,
            EventType = "audio.weapon_fire", Timestamp = FixedTs, Features = features,
        };

        e1.Should().Be(e2);
    }

    [Fact]
    public void EqualityTwoInstancesWithSameScalarValuesAndDifferentDictionaryInstancesWithSameContentAreNotEqual()
    {
        // ImmutableDictionary uses reference equality in record comparison — two distinct
        // dictionary instances with identical content are NOT equal. This is intentional;
        // see the Features/Metadata field doc comments.
        var id = Guid.NewGuid().ToString();
        var features1 = ImmutableDictionary<string, double>.Empty.Add("rms", 0.8);
        var features2 = ImmutableDictionary<string, double>.Empty.Add("rms", 0.8);

        var e1 = new SensorEvent
        {
            EventId = id, SensorId = "audio.endpoint-loopback", SensorKind = SensorKind.Audio,
            EventType = "audio.weapon_fire", Timestamp = FixedTs, Features = features1,
        };
        var e2 = new SensorEvent
        {
            EventId = id, SensorId = "audio.endpoint-loopback", SensorKind = SensorKind.Audio,
            EventType = "audio.weapon_fire", Timestamp = FixedTs, Features = features2,
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
    public void JsonRoundTripPreservesAllScalarFields()
    {
        var id = Guid.NewGuid().ToString();
        var original = new SensorEvent
        {
            EventId = id,
            SensorId = "screen.roi",
            SensorKind = SensorKind.Screen,
            EventType = "screen.shield_flash",
            Timestamp = FixedTs,
            Confidence = 0.92,
            Intensity = 0.75,
            Duration = TimeSpan.FromMilliseconds(400),
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<SensorEvent>(json)!;

        restored.EventId.Should().Be(original.EventId);
        restored.SensorId.Should().Be(original.SensorId);
        restored.SensorKind.Should().Be(original.SensorKind);
        restored.EventType.Should().Be(original.EventType);
        restored.Timestamp.Should().Be(original.Timestamp);
        restored.Confidence.Should().Be(original.Confidence);
        restored.Intensity.Should().Be(original.Intensity);
        restored.Duration.Should().Be(original.Duration);
    }

    [Fact]
    public void JsonRoundTripPreservesDictionaryContent()
    {
        var original = BuildMinimal() with
        {
            Features = ImmutableDictionary<string, double>.Empty
                .Add("rms", 0.82)
                .Add("peak", 0.95),
            Metadata = ImmutableDictionary<string, string>.Empty
                .Add("source", "stereo-mix"),
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<SensorEvent>(json)!;

        restored.Features.Should().ContainKey("rms");
        restored.Features["rms"].Should().BeApproximately(0.82, 1e-9);
        restored.Features.Should().ContainKey("peak");
        restored.Features["peak"].Should().BeApproximately(0.95, 1e-9);
        restored.Metadata.Should().ContainKey("source");
        restored.Metadata["source"].Should().Be("stereo-mix");
    }

    [Fact]
    public void WithExpressionCreatesNewInstanceWithModifiedField()
    {
        var original = BuildMinimal();
        var modified = original with { Confidence = 0.99 };

        modified.Confidence.Should().Be(0.99);
        modified.EventId.Should().Be(original.EventId);
        ReferenceEquals(original, modified).Should().BeFalse();
    }
}
