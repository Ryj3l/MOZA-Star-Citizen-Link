using Moza.ScLink.Core.Models;
using Moza.ScLink.Diagnostics;

namespace Moza.ScLink.App.Tests;

public sealed class DeviceStateExtensionsTests
{
    [Theory]
    [InlineData(DeviceState.Disconnected, "No device")]
    [InlineData(DeviceState.Detecting, "Detecting")]
    [InlineData(DeviceState.Initializing, "Initializing")]
    [InlineData(DeviceState.Ready, "Ready")]
    [InlineData(DeviceState.Faulted, "Faulted")]
    public void MapsEachStateToUserFacingString(DeviceState state, string expected) =>
        Assert.Equal(expected, state.ToUserFacingString());
}
