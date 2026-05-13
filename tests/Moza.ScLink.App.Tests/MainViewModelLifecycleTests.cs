using Moza.ScLink.App.ViewModels;

namespace Moza.ScLink.App.Tests;

[Collection(nameof(WpfApplicationFixtureScope))]
public sealed class MainViewModelLifecycleTests
{
    [Fact]
    public void ConstructorDoesNotThrow()
    {
        using var vm = new MainViewModel();

        Assert.NotNull(vm);
        Assert.False(vm.IsMonitoring);
        Assert.NotNull(vm.Status);
        Assert.NotNull(vm.OutputName);
    }

    [Fact]
    public void StartCommandCannotExecuteWhenGameLogPathIsMissing()
    {
        using var vm = new MainViewModel
        {
            GameLogPath = "Z:\\does-not-exist\\Game.log"
        };

        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.False(vm.IsMonitoring);
    }

    [Fact]
    public async Task DisposeFromIdleStateStopsCleanlyAndLeavesIsMonitoringFalse()
    {
        var vm = new MainViewModel();

        vm.Dispose();
        await Task.Delay(50);

        Assert.False(vm.IsMonitoring);
        Assert.False(string.IsNullOrWhiteSpace(vm.Status));
    }
}
