using FluentAssertions;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Effects.Catalogs;

namespace Moza.ScLink.Effects.Tests;

public sealed class GameEventToEffectMapTests
{
    [Fact]
    public void MapsExactlyTheTenPhase1EventTypes()
    {
        GameEventToEffectMap.All.Should().HaveCount(10);
    }

    [Theory]
    [InlineData(GameEventType.QuantumSpoolStarted, "quantum-spool-v1")]
    [InlineData(GameEventType.QuantumJumpExit, "quantum-jump-exit-v1")]
    [InlineData(GameEventType.AtmosphereEntered, "atmosphere-entry-v1")]
    [InlineData(GameEventType.LandingGearContact, "landing-contact-v1")]
    [InlineData(GameEventType.WeaponFireBallistic, "weapon-fire-generic-v1")]
    [InlineData(GameEventType.WeaponFireEnergy, "weapon-fire-generic-v1")]
    [InlineData(GameEventType.WeaponFireGeneric, "weapon-fire-generic-v1")]
    [InlineData(GameEventType.VehicleDestruction, "vehicle-destruction-v1")]
    public void PlayEntriesMapToExpectedEffectId(GameEventType eventType, string effectId)
    {
        GameEventToEffectMap.TryGet(eventType).Should().BeOfType<PlayEntry>()
            .Which.EffectId.Should().Be(effectId);
    }

    [Theory]
    [InlineData(GameEventType.QuantumSpoolEnded, "quantum-spool-v1")]
    [InlineData(GameEventType.AtmosphereExited, "atmosphere-entry-v1")]
    public void StopEntriesReferenceTheStartEffect(GameEventType eventType, string effectId)
    {
        GameEventToEffectMap.TryGet(eventType).Should().BeOfType<StopEntry>()
            .Which.EffectId.Should().Be(effectId);
    }

    [Fact]
    public void UnmappedEventTypeReturnsNull()
    {
        // HullImpact has no Phase-1 effect; it is deliberately unmapped.
        GameEventToEffectMap.TryGet(GameEventType.HullImpact).Should().BeNull();
    }

    [Fact]
    public void EveryMappedEffectIdExistsInShippedCatalog()
    {
        using var catalog = EffectCatalog.LoadDefault();
        var catalogIds = catalog.Current.Select(e => e.EffectId).ToHashSet();

        foreach (var entry in GameEventToEffectMap.All.Values)
        {
            catalogIds.Should().Contain(entry.EffectId,
                "map entry '{0}' must resolve to a shipped catalog effect", entry.EffectId);
        }
    }

    [Fact]
    public void EveryStopEntryTargetsAnEffectWithANonNullStateKey()
    {
        using var catalog = EffectCatalog.LoadDefault();
        var byId = catalog.Current.ToDictionary(e => e.EffectId!);

        foreach (var (eventType, entry) in GameEventToEffectMap.All)
        {
            if (entry is StopEntry)
            {
                byId[entry.EffectId].StateKey.Should().NotBeNullOrEmpty(
                    "{0} is a Stop entry and needs the referenced effect's StateKey to stop", eventType);
            }
        }
    }
}
