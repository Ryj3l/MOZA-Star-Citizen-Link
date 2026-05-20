using Microsoft.Extensions.Logging;
using Moza.ScLink.App.Bus;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Sensors;

namespace Moza.ScLink.App.Tests;

public sealed class DropRateMonitorTests
{
    private static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(20);

    private static SensorEvent ASensorEvent() => new()
    {
        EventId = Guid.NewGuid().ToString(),
        SensorId = "audio.endpoint-loopback",
        SensorKind = SensorKind.Audio,
        EventType = "audio.weapon_fire_ballistic",
        Timestamp = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task LogsWarningWhenSensorDropRateExceedsThreshold()
    {
        var bus = new EventBus();
        // Force a real >1% drop state: write past the 1024 capacity with no reader.
        // published = 1124, dropped = 100 -> ~8.9% > 1%.
        for (var i = 0; i < 1024 + 100; i++)
        {
            bus.SensorEvents.TryWrite(ASensorEvent());
        }

        var logger = new RecordingLogger();
        using var monitor = new DropRateMonitor(bus, logger, FastInterval);

        await monitor.StartAsync(CancellationToken.None);
        var completed = await Task.WhenAny(logger.FirstWarning, Task.Delay(2000));
        await monitor.StopAsync(CancellationToken.None);

        Assert.Equal(logger.FirstWarning, completed);
        Assert.True(logger.Warnings > 0);
    }

    [Fact]
    public async Task DoesNotLogWhenDropRateBelowThreshold()
    {
        var bus = new EventBus();
        // Within capacity -> zero drops -> 0% rate.
        for (var i = 0; i < 500; i++)
        {
            bus.SensorEvents.TryWrite(ASensorEvent());
        }

        var logger = new RecordingLogger();
        using var monitor = new DropRateMonitor(bus, logger, FastInterval);

        await monitor.StartAsync(CancellationToken.None);
        await Task.Delay(150); // several sample ticks at 20ms
        await monitor.StopAsync(CancellationToken.None);

        Assert.Equal(0, logger.Warnings);
    }

    private sealed class RecordingLogger : ILogger<DropRateMonitor>
    {
        private readonly TaskCompletionSource _firstWarning =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _warnings;

        public int Warnings => Volatile.Read(ref _warnings);

        public Task FirstWarning => _firstWarning.Task;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Interlocked.Increment(ref _warnings);
                _firstWarning.TrySetResult();
            }
        }
    }
}
