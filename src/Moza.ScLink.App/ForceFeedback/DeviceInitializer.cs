using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moza.ScLink.Core.Devices;

namespace Moza.ScLink.App.ForceFeedback;

/// <summary>
/// Initializes the canonical <see cref="IForceFeedbackDevice"/> at host start (T-27). Registered as a
/// plain <see cref="IHostedService"/> BEFORE <c>ForceCommandPipeline</c>, so its awaited
/// <see cref="StartAsync"/> completes — leaving the device <see cref="Core.Models.DeviceState.Ready"/> —
/// before the pipeline's <c>BackgroundService.ExecuteAsync</c> loop processes its first command (which
/// would otherwise throw, since <c>VorticeDirectInputDevice.ExecuteAsync</c> requires prior init).
/// </summary>
/// <remarks>
/// The App resolves and shows <c>MainWindow</c> before <c>host.Start()</c> (T-27 Fork-1 refinement) so
/// the main-window HWND is valid here: <c>VorticeDirectInputDevice.InitializeAsync</c> passes it to
/// <c>SetCooperativeLevel(Exclusive|Background)</c>. The no-hardware <c>PreviewForceFeedbackDevice</c>
/// needs no HWND. Device disposal is the container's responsibility (the device is an
/// <see cref="IAsyncDisposable"/> singleton), so <see cref="StopAsync"/> is a no-op.
/// </remarks>
public sealed class DeviceInitializer : IHostedService
{
    private readonly IForceFeedbackDevice _device;
    private readonly ILogger<DeviceInitializer> _logger;

    public DeviceInitializer(IForceFeedbackDevice device, ILogger<DeviceInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);
        _device = device;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _device.InitializeAsync(cancellationToken).ConfigureAwait(false);
        Log.DeviceInitialized(_logger, _device.DisplayName, _device.State.ToString());
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static class Log
    {
        private static readonly Action<ILogger, string, string, Exception?> _deviceInitialized =
            LoggerMessage.Define<string, string>(
                LogLevel.Information,
                new EventId(1, nameof(DeviceInitialized)),
                "Canonical force-feedback device initialized at host start: {DisplayName} (state {State}).");

        public static void DeviceInitialized(ILogger logger, string displayName, string state) =>
            _deviceInitialized(logger, displayName, state, null);
    }
}
