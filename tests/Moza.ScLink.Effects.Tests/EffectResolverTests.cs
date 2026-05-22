using System.IO;
using FluentAssertions;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Events;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Resolver;
using Moza.ScLink.Effects.Catalogs;

namespace Moza.ScLink.Effects.Tests;

public sealed class EffectResolverTests : IDisposable
{
    private readonly EffectCatalog _catalog = EffectCatalog.LoadDefault();

    // Phase-1 default context: empty profile/gains, Unknown placeholder device, unit ceiling so the
    // gain-stack clamp doesn't mask the intensity assertions.
    private static ResolverContext Context(double masterGain = 1.0) =>
        new(
            new ShipProfile { ShipId = "default", DisplayName = "Default" },
            new UserGains { MasterGain = masterGain },
            new DeviceCapabilities(DeviceModel.Unknown, 2, 4, true, true, true, 10000, 1.0),
            DateTimeOffset.UtcNow);

    private static GameEvent Event(GameEventType type, double intensity = 1.0) =>
        new()
        {
            EventId = Guid.NewGuid().ToString(),
            EventType = type,
            Timestamp = DateTimeOffset.UtcNow,
            Intensity = intensity,
        };

    [Theory]
    [InlineData(GameEventType.QuantumSpoolStarted, "quantum-spool-v1")]
    [InlineData(GameEventType.QuantumJumpExit, "quantum-jump-exit-v1")]
    [InlineData(GameEventType.AtmosphereEntered, "atmosphere-entry-v1")]
    [InlineData(GameEventType.LandingGearContact, "landing-contact-v1")]
    [InlineData(GameEventType.WeaponFireBallistic, "weapon-fire-generic-v1")]
    [InlineData(GameEventType.WeaponFireEnergy, "weapon-fire-generic-v1")]
    [InlineData(GameEventType.WeaponFireGeneric, "weapon-fire-generic-v1")]
    [InlineData(GameEventType.VehicleDestruction, "vehicle-destruction-v1")]
    public void PlayEventsResolveToPlayCommandForExpectedEffect(GameEventType type, string effectId)
    {
        var commands = new EffectResolver(_catalog).Resolve(Event(type), Context());

        commands.Should().ContainSingle().Which.Should().BeOfType<PlayEffectCommand>()
            .Which.Effect.EffectId.Should().Be(effectId);
    }

    [Theory]
    [InlineData(GameEventType.QuantumSpoolEnded, "quantum-spool")]
    [InlineData(GameEventType.AtmosphereExited, "atmosphere")]
    public void StopEventsResolveToStopCommandWithStartEffectStateKey(GameEventType type, string stateKey)
    {
        var commands = new EffectResolver(_catalog).Resolve(Event(type), Context());

        commands.Should().ContainSingle().Which.Should().BeOfType<StopEffectCommand>()
            .Which.StateKey.Should().Be(stateKey);
    }

    [Fact]
    public void GainStackAppliesThroughTheResolvedIntensity()
    {
        // quantum-spool-v1 baseIntensity 0.42 * masterGain 0.6 * (others 1.0) = 0.252.
        // Distinguishing claim: catches both "multiply wrong" and "read the wrong factor".
        var commands = new EffectResolver(_catalog)
            .Resolve(Event(GameEventType.QuantumSpoolStarted), Context(masterGain: 0.6));

        commands.OfType<PlayEffectCommand>().Single().FinalIntensity.Should().BeApproximately(0.42 * 0.6, 1e-9);
    }

    [Fact]
    public void ZeroEventIntensityFallsBackToCatalogBaseIntensity()
    {
        // Finding 3: Intensity 0 -> modifier 1.0 -> baseIntensity 0.42 drives (not zeroed).
        var commands = new EffectResolver(_catalog)
            .Resolve(Event(GameEventType.QuantumSpoolStarted, intensity: 0.0), Context());

        commands.OfType<PlayEffectCommand>().Single().FinalIntensity.Should().BeApproximately(0.42, 1e-9);
    }

    [Fact]
    public void TranslatesCatalogDefinitionToCoreForceEffect()
    {
        // quantum-spool-v1: PeriodicWithEnvelope / Flight / envelope attackMs 250 -> Attack 250ms.
        var play = new EffectResolver(_catalog)
            .Resolve(Event(GameEventType.QuantumSpoolStarted), Context())
            .OfType<PlayEffectCommand>().Single();

        play.Effect.EffectType.Should().Be(ForceEffectType.PeriodicWithEnvelope);
        play.Effect.Category.Should().Be(EffectCategory.Flight);
        play.Effect.IsSustained.Should().BeTrue();
        play.Effect.Envelope.Should().NotBeNull();
        play.Effect.Envelope!.Attack.Should().Be(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void UnmappedEventTypeResolvesToNoCommand()
    {
        new EffectResolver(_catalog).Resolve(Event(GameEventType.HullImpact), Context())
            .Should().BeEmpty();
    }

    [Fact]
    public void MissingCatalogEffectResolvesToNoCommandGracefully()
    {
        // A catalog missing the mapped effect -> graceful empty, no throw (hot-reload-drop guard).
        var path = Path.Combine(Path.GetTempPath(), $"moza-resolver-empty-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "schemaVersion": 1, "catalogId": "empty", "effects": [] }""");
        using var emptyCatalog = new EffectCatalog(path);
        try
        {
            new EffectResolver(emptyCatalog).Resolve(Event(GameEventType.QuantumSpoolStarted), Context())
                .Should().BeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    public void Dispose() => _catalog.Dispose();
}
