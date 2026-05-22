using FluentAssertions;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Events;
using Moza.ScLink.Core.Models;
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
        var service = new EffectResolverService(
            bus, new EffectResolver(catalog), new DefaultResolverContextProvider());

        await service.StartAsync(CancellationToken.None);
        bus.GameEvents.TryWrite(Event(GameEventType.QuantumSpoolStarted)).Should().BeTrue();
        await WaitUntilAsync(() => bus.ForceCommandReader.Count > 0, TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        bus.ForceCommandReader.TryRead(out var command).Should().BeTrue();
        command.Should().BeOfType<PlayEffectCommand>()
            .Which.Effect.EffectId.Should().Be("quantum-spool-v1");
    }
}
