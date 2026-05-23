using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Safety;

namespace Moza.ScLink.Effects;

/// <summary>
/// The output worker (PRP §2.7): the single reader of <see cref="IEventBus.ForceCommandReader"/>. In normal
/// operation it forwards each <see cref="ForceCommand"/> to the device via <c>ExecuteAsync</c>. On emergency
/// stop it bypasses the channel entirely (Fork 1, T-16): a linked <see cref="CancellationTokenSource"/>
/// driven by <see cref="IEmergencyStop.Activated"/> wakes the (possibly idle) channel read so the worker can
/// call <c>device.StopAllAsync()</c> immediately — meeting the 50 ms budget even when no commands are flowing,
/// the state in which an idle per-command IsActive check would never run. While the stop is engaged it
/// discards new plays and passes stops through; it resumes automatically when the stop clears
/// (<see cref="IEmergencyStop.IsActive"/> is the single source of truth — three-clean-roles: state in
/// EmergencyStop, consume here, device-halt in the device).
/// </summary>
public sealed class ForceCommandPipeline : BackgroundService
{
    private readonly IEventBus _bus;
    private readonly IForceFeedbackDevice _device;
    private readonly IEmergencyStop _emergencyStop;
    private readonly ILogger<ForceCommandPipeline> _logger;
    private readonly object _triggerGate = new();

    // Cancelled by the Activated handler to wake an idle channel read; recreated after each handled wake so a
    // later stop episode can wake again. Guarded by _triggerGate (handler thread vs consume-loop thread).
    private CancellationTokenSource _estopTrigger = new();

    // Stopwatch tick stamped when Activated fires; read after the bypass StopAll to log wall-clock latency.
    private long _activatedTimestamp;

    public ForceCommandPipeline(
        IEventBus bus,
        IForceFeedbackDevice device,
        IEmergencyStop emergencyStop,
        ILogger<ForceCommandPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(emergencyStop);
        ArgumentNullException.ThrowIfNull(logger);
        _bus = bus;
        _device = device;
        _emergencyStop = emergencyStop;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _emergencyStop.Activated += OnEmergencyStopActivated;
        var reader = _bus.ForceCommandReader;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var woken = false;
                using (var linked =
                    CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, CurrentEstopToken()))
                {
                    try
                    {
                        if (!await reader.WaitToReadAsync(linked.Token).ConfigureAwait(false))
                        {
                            break; // channel completed
                        }
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        woken = true; // emergency stop woke an idle/blocked read, not host shutdown
                    }
                }

                if (woken)
                {
                    await RunEmergencyStopBypassAsync(stoppingToken).ConfigureAwait(false);
                }

                while (reader.TryRead(out var command))
                {
                    await ProcessAsync(command, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown: StopAsync cancelled stoppingToken.
        }
        finally
        {
            _emergencyStop.Activated -= OnEmergencyStopActivated;
        }
    }

    // The immediate channel bypass: halt the device, log the activation→halt latency, then re-arm the wake
    // trigger so a later stop episode can wake the loop again.
    private async Task RunEmergencyStopBypassAsync(CancellationToken stoppingToken)
    {
        await _device.StopAllAsync(stoppingToken).ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(Volatile.Read(ref _activatedTimestamp));
        Log.EmergencyStopBypass(_logger, elapsed.TotalMilliseconds);
        RearmEstopTrigger();
    }

    private async Task ProcessAsync(ForceCommand command, CancellationToken stoppingToken)
    {
        if (_emergencyStop.IsActive && command is PlayEffectCommand play)
        {
            // Refuse new plays while the stop is engaged; stops (StopEffect/StopAll) still reach the device.
            Log.PlayRefused(_logger, play.Effect.EffectId);
            return;
        }

        await _device.ExecuteAsync(command, stoppingToken).ConfigureAwait(false);
    }

    private void OnEmergencyStopActivated(object? sender, EmergencyStopActivatedEventArgs e)
    {
        Volatile.Write(ref _activatedTimestamp, Stopwatch.GetTimestamp());
        lock (_triggerGate)
        {
            _estopTrigger.Cancel();
        }
    }

    private CancellationToken CurrentEstopToken()
    {
        lock (_triggerGate)
        {
            return _estopTrigger.Token;
        }
    }

    private void RearmEstopTrigger()
    {
        lock (_triggerGate)
        {
            _estopTrigger.Dispose();
            _estopTrigger = new CancellationTokenSource();
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        lock (_triggerGate)
        {
            _estopTrigger.Dispose();
        }

        base.Dispose();
    }

    private static class Log
    {
        private static readonly Action<ILogger, double, Exception?> _emergencyStopBypass =
            LoggerMessage.Define<double>(
                LogLevel.Information,
                new EventId(1, nameof(EmergencyStopBypass)),
                "Emergency stop: device StopAll issued via channel bypass ({LatencyMs:F2} ms from activation)");

        private static readonly Action<ILogger, string, Exception?> _playRefused =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(2, nameof(PlayRefused)),
                "Emergency stop active: refused PlayEffectCommand for effect {EffectId}");

        public static void EmergencyStopBypass(ILogger logger, double latencyMs) =>
            _emergencyStopBypass(logger, latencyMs, null);

        public static void PlayRefused(ILogger logger, string effectId) => _playRefused(logger, effectId, null);
    }
}
