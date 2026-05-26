using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.App.ForceFeedback;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.App.Tests;

public sealed class DeviceInitializerTests
{
    [Fact]
    public async Task StartAsyncInitializesTheDevice()
    {
        var device = new RecordingCanonicalDevice();
        var initializer = new DeviceInitializer(device, NullLogger<DeviceInitializer>.Instance);

        await initializer.StartAsync(CancellationToken.None);

        Assert.Equal(1, device.InitializeCount);
        Assert.Equal(DeviceState.Ready, device.State);
    }

    [Fact]
    public async Task StopAsyncDoesNotDriveOrInitializeTheDevice()
    {
        var device = new RecordingCanonicalDevice();
        var initializer = new DeviceInitializer(device, NullLogger<DeviceInitializer>.Instance);

        await initializer.StopAsync(CancellationToken.None);

        Assert.Equal(0, device.InitializeCount);
        Assert.Equal(0, device.StopAllCount);
        Assert.Empty(device.Executed);
    }
}
