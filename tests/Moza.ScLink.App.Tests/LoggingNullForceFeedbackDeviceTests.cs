using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.App.ForceFeedback;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.App.Tests;

public sealed class LoggingNullForceFeedbackDeviceTests
{
    private static LoggingNullForceFeedbackDevice CreateDevice() =>
        new(NullLogger<LoggingNullForceFeedbackDevice>.Instance);

    private static StopAllCommand SampleCommand() =>
        new() { CommandId = "test-command", IssuedAt = DateTimeOffset.UtcNow };

    [Fact]
    public void StateIsDisconnectedBeforeInitialize()
    {
        var device = CreateDevice();
        Assert.Equal(DeviceState.Disconnected, device.State);
    }

    [Fact]
    public async Task StateIsReadyAfterInitialize()
    {
        await using var device = CreateDevice();

        await device.InitializeAsync(CancellationToken.None);

        Assert.Equal(DeviceState.Ready, device.State);
    }

    [Fact]
    public async Task ExecuteAsyncCompletesWithoutDrivingHardware()
    {
        await using var device = CreateDevice();
        await device.InitializeAsync(CancellationToken.None);

        // Completes synchronously; the contract is "logs intent, never throws for a valid command".
        await device.ExecuteAsync(SampleCommand(), CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsyncThrowsOnNullCommand()
    {
        await using var device = CreateDevice();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => device.ExecuteAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task StopAllAsyncCompletes()
    {
        await using var device = CreateDevice();
        await device.InitializeAsync(CancellationToken.None);

        await device.StopAllAsync(CancellationToken.None);
    }

    [Fact]
    public void ReportsUnknownModelAndPlaceholderCapabilities()
    {
        var device = CreateDevice();

        Assert.Equal(DeviceModel.Unknown, device.Model);
        Assert.Equal(DeviceModel.Unknown, device.Capabilities.Model);
        Assert.Equal(0, device.Capabilities.AxisCount);
        Assert.Equal(Guid.Empty, device.InstanceGuid);
    }
}
