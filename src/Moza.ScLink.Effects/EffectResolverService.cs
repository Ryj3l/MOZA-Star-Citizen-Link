using Microsoft.Extensions.Hosting;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Resolver;

namespace Moza.ScLink.Effects;

/// <summary>
/// Hosted service (PRP §2.7): reads <see cref="GameEvent"/>s from <see cref="IEventBus.GameEventReader"/>,
/// resolves each into <see cref="ForceCommand"/>s via <see cref="IEffectResolver"/>, and writes them to the
/// <see cref="IEventBus.ForceCommands"/> channel — the sole producer of force commands. The channel is
/// bounded with Wait/backpressure (PRP §2.7), so writes await rather than drop.
/// </summary>
/// <remarks>
/// MIGRATION (T-14, tracked in #43/#45): registered as a hosted service but dormant until the generic host
/// starts (#43) — same Option-B staging as <c>LogSensor</c> (T-11), <c>FusionEngine</c> (T-12), and the
/// drop-rate monitor (T-10). The live <see cref="ResolverContext"/> source (settings-driven gains, active
/// device capabilities) arrives with the convergence; Phase 1 uses <see cref="DefaultResolverContextProvider"/>.
/// </remarks>
public sealed class EffectResolverService : BackgroundService
{
    private readonly IEventBus _bus;
    private readonly IEffectResolver _resolver;
    private readonly IResolverContextProvider _contextProvider;

    public EffectResolverService(IEventBus bus, IEffectResolver resolver, IResolverContextProvider contextProvider)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(contextProvider);
        _bus = bus;
        _resolver = resolver;
        _contextProvider = contextProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var gameEvent in _bus.GameEventReader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                var context = _contextProvider.GetContext();
                foreach (var command in _resolver.Resolve(gameEvent, context))
                {
                    await _bus.ForceCommands.WriteAsync(command, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown: StopAsync cancels stoppingToken.
        }
    }
}
