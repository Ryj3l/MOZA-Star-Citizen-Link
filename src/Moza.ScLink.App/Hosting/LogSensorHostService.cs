using Microsoft.Extensions.Hosting;
using Moza.ScLink.Logs;

namespace Moza.ScLink.App.Hosting;

/// <summary>
/// Bridges the <see cref="LogSensor"/> (an <c>ISensor</c>, not an <see cref="IHostedService"/>) into the
/// generic host so it tails the active Game.log for the application lifetime (T-27 — host-owned,
/// always-on sensor lifecycle). Avoids adding <see cref="IHostedService"/> to the T-11-locked
/// <see cref="LogSensor"/>; <see cref="LogSensor.StartAsync"/>/<see cref="LogSensor.StopAsync"/> already
/// match the hosted-service signatures, so this is a pure delegation. The sensor tolerates an
/// empty/missing path (the underlying tailer idles and picks up the file if it appears), so this is
/// safe to start on a clean machine.
/// </summary>
public sealed class LogSensorHostService : IHostedService
{
    private readonly LogSensor _sensor;

    public LogSensorHostService(LogSensor sensor)
    {
        ArgumentNullException.ThrowIfNull(sensor);
        _sensor = sensor;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _sensor.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => _sensor.StopAsync(cancellationToken);
}
