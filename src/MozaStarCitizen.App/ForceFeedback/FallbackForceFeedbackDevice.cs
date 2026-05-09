using MozaStarCitizen.App.Diagnostics;
using MozaStarCitizen.App.Models;

namespace MozaStarCitizen.App.ForceFeedback;

public sealed class FallbackForceFeedbackDevice : IForceFeedbackDevice
{
    private readonly IReadOnlyList<IForceFeedbackDevice> _devices;
    private readonly HashSet<IForceFeedbackDevice> _initializedDevices = [];
    private IForceFeedbackDevice? _currentDevice;

    public FallbackForceFeedbackDevice(IEnumerable<IForceFeedbackDevice> devices)
    {
        _devices = devices.ToArray();
    }

    public string Name => _currentDevice is null
        ? "Auto output"
        : $"Auto output ({_currentDevice.Name})";

    public string Status => string.Join(" -> ", _devices.Select(d => d.Name));

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_currentDevice is not null)
        {
            return;
        }

        foreach (var device in _devices)
        {
            try
            {
                await InitializeDeviceAsync(device, cancellationToken);
                _currentDevice = device;
                return;
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"{device.Name} initialize failed");
            }
        }
    }

    public async Task PrepareAsync(IEnumerable<ForceEffect> effects, CancellationToken cancellationToken)
    {
        if (_currentDevice is null)
        {
            return;
        }

        await _currentDevice.PrepareAsync(effects, cancellationToken);
    }

    public Task PlayAsync(ForceEffect effect, CancellationToken cancellationToken) =>
        TryDevicesAsync(device => device.PlayAsync(effect, cancellationToken), $"play {effect.Name}");

    public Task StopAsync(string stateKey, CancellationToken cancellationToken) =>
        StopInitializedAsync(device => device.StopAsync(stateKey, cancellationToken), $"stop {stateKey}");

    public Task StopAllAsync(CancellationToken cancellationToken) =>
        StopInitializedAsync(device => device.StopAllAsync(cancellationToken), "stop all");

    private async Task TryDevicesAsync(Func<IForceFeedbackDevice, Task> action, string operation)
    {
        Exception? lastException = null;

        for (var i = 0; i < _devices.Count; i++)
        {
            var device = _devices[i];
            try
            {
                await InitializeDeviceAsync(device, CancellationToken.None);
                await action(device);
                _currentDevice = device;
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                AppLog.Write(ex, $"{device.Name} {operation} failed");
            }
        }

        if (lastException is not null)
        {
            throw new InvalidOperationException($"All force feedback outputs failed to {operation}. Last failure: {lastException.Message}", lastException);
        }
    }

    private async Task StopInitializedAsync(Func<IForceFeedbackDevice, Task> action, string operation)
    {
        foreach (var device in _initializedDevices.ToArray())
        {
            try
            {
                await action(device);
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"{device.Name} {operation} failed");
            }
        }
    }

    private async Task InitializeDeviceAsync(IForceFeedbackDevice device, CancellationToken cancellationToken)
    {
        if (_initializedDevices.Contains(device))
        {
            return;
        }

        await device.InitializeAsync(cancellationToken);
        _initializedDevices.Add(device);
    }
}
