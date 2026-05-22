using Microsoft.Extensions.Hosting;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Resolver;

namespace Moza.ScLink.Effects;

/// <summary>
/// Hosted service (PRP §2.7): reads <see cref="GameEvent"/>s from <see cref="IEventBus.GameEventReader"/>,
/// resolves each into <see cref="ForceCommand"/>s via <see cref="IEffectResolver"/>, runs them through the
/// <see cref="SafetyLimiterStage"/> (PRP §5.8 safety limiting, T-15), and writes the admitted commands to the
/// <see cref="IEventBus.ForceCommands"/> channel — the sole producer of force commands. The channel is
/// bounded with Wait/backpressure (PRP §2.7), so writes await rather than drop. A single
/// <see cref="IResolverContextProvider.GetContext"/> call per event feeds both the resolver and the stage, so
/// the gain stack and the safety limiter always see the same device capabilities.
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
    private readonly SafetyLimiterStage _stage;

    public EffectResolverService(
        IEventBus bus,
        IEffectResolver resolver,
        IResolverContextProvider contextProvider,
        SafetyLimiterStage stage)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(contextProvider);
        ArgumentNullException.ThrowIfNull(stage);
        _bus = bus;
        _resolver = resolver;
        _contextProvider = contextProvider;
        _stage = stage;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var gameEvent in _bus.GameEventReader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                var context = _contextProvider.GetContext();
                foreach (var resolved in _resolver.Resolve(gameEvent, context))
                {
                    foreach (var command in _stage.Process(resolved, context.DeviceCapabilities))
                    {
                        await _bus.ForceCommands.WriteAsync(command, stoppingToken).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown: StopAsync cancels stoppingToken.
        }
    }
}
