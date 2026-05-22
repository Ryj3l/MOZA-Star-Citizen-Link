using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Events;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Resolver;
using Moza.ScLink.Effects.Catalogs;

namespace Moza.ScLink.Effects.Tests;

public sealed class EffectResolverServiceTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }
    }

    private static GameEvent Event(GameEventType type) =>
        new()
        {
            EventId = Guid.NewGuid().ToString(),
            EventType = type,
            Timestamp = DateTimeOffset.UtcNow,
            Intensity = 1.0,
        };

    [Fact]
    public async Task ConsumesGameEventsAndPublishesForceCommands()
    {
        // Full chain: GameEvent on the bus -> service drains -> resolver -> ForceCommand on the bus.
        // Same shape as T-12's ExecuteAsyncConsumesBusAndPublishes (hosted-service drain + cancellation).
        using var catalog = EffectCatalog.LoadDefault();
        var bus = new EventBus();
        var stage = new SafetyLimiterStage(new SafetyLimiter(NullLogger<SafetyLimiter>.Instance));
        var service = new EffectResolverService(
            bus, new EffectResolver(catalog), new DefaultResolverContextProvider(), stage);

        await service.StartAsync(CancellationToken.None);
        bus.GameEvents.TryWrite(Event(GameEventType.QuantumSpoolStarted)).Should().BeTrue();
        await WaitUntilAsync(() => bus.ForceCommandReader.Count > 0, TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        bus.ForceCommandReader.TryRead(out var command).Should().BeTrue();
        command.Should().BeOfType<PlayEffectCommand>()
            .Which.Effect.EffectId.Should().Be("quantum-spool-v1");
    }

    [Fact]
    public async Task LimiterClampsInPath()
    {
        // quantum-spool-v1 is sustained (8s, base 0.42). With master gain 2.0 the resolver's gain stack emits
        // 0.84 (clamped only to the device ceiling 1.0); the stage's sustained cap then brings it to 0.7 — a
        // limiter-only enforcement the resolver does not perform, proving the stage is in the live path.
        using var catalog = EffectCatalog.LoadDefault();
        var bus = new EventBus();
        var stage = new SafetyLimiterStage(new SafetyLimiter(NullLogger<SafetyLimiter>.Instance));
        var service = new EffectResolverService(
            bus, new EffectResolver(catalog), new HighGainContextProvider(), stage);

        await service.StartAsync(CancellationToken.None);
        bus.GameEvents.TryWrite(Event(GameEventType.QuantumSpoolStarted)).Should().BeTrue();
        await WaitUntilAsync(() => bus.ForceCommandReader.Count > 0, TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        bus.ForceCommandReader.TryRead(out var command).Should().BeTrue();
        command.Should().BeOfType<PlayEffectCommand>().Which.FinalIntensity.Should().Be(0.7);
    }

    // A context provider with a high master gain so a resolved sustained effect exceeds the 0.7 ceiling,
    // letting LimiterClampsInPath observe a stage-only clamp end-to-end.
    private sealed class HighGainContextProvider : IResolverContextProvider
    {
        public ResolverContext GetContext() =>
            new(
                new ShipProfile { ShipId = "test", DisplayName = "Test" },
                new UserGains { MasterGain = 2.0 },
                new DeviceCapabilities(DeviceModel.Unknown, 0, 0, false, false, false, 0, 1.0),
                DateTimeOffset.UtcNow);
    }
}
