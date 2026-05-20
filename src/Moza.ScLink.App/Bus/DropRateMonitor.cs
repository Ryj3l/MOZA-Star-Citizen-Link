using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moza.ScLink.Core.Bus;

namespace Moza.ScLink.App.Bus;

/// <summary>
/// Periodically samples <see cref="EventBusMetrics"/> and logs a warning when the SensorEvent channel's
/// drop rate exceeds 1% over the sample window (PRP §2.7 line 105). Registered as a hosted service, but
/// dormant until the generic host is started — see issue #43.
/// </summary>
public sealed class DropRateMonitor : BackgroundService
{
    private const double DropRateThreshold = 0.01;
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

    private readonly IEventBus _bus;
    private readonly ILogger<DropRateMonitor> _logger;
    private readonly TimeSpan _interval;

    /// <summary>Production constructor — samples the SensorEvent drop rate every 60 seconds.</summary>
    public DropRateMonitor(IEventBus bus, ILogger<DropRateMonitor> logger)
        : this(bus, logger, DefaultInterval)
    {
    }

    /// <summary>Constructor with a configurable sample interval (used by the boundary test for a short cadence).</summary>
    public DropRateMonitor(IEventBus bus, ILogger<DropRateMonitor> logger, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(logger);
        _bus = bus;
        _logger = logger;
        _interval = interval;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        var lastPublished = 0L;
        var lastDropped = 0L;

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var counters = _bus.Metrics.SensorEvents;
                var published = counters.Published;
                var dropped = counters.Dropped;
                var publishedDelta = published - lastPublished;
                var droppedDelta = dropped - lastDropped;
                lastPublished = published;
                lastDropped = dropped;

                if (EventBusMetrics.ExceedsDropRate(publishedDelta, droppedDelta, DropRateThreshold))
                {
                    Log.DropRateExceeded(_logger, droppedDelta, publishedDelta);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown: StopAsync cancels stoppingToken.
        }
    }

    private static class Log
    {
        private static readonly Action<ILogger, long, long, Exception?> _dropRateExceeded =
            LoggerMessage.Define<long, long>(
                LogLevel.Warning,
                new EventId(1, nameof(DropRateExceeded)),
                "SensorEvent drop rate exceeded 1% over the sample window: {Dropped} dropped / {Published} published");

        public static void DropRateExceeded(ILogger logger, long dropped, long published)
            => _dropRateExceeded(logger, dropped, published, null);
    }
}
