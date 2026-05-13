using System.Windows;
using System.Windows.Threading;

namespace Moza.ScLink.App.Tests;

// xUnit collection fixture that ensures a System.Windows.Application singleton exists
// on the test-process STA thread so that MainViewModel.Dispatch(...) can resolve a
// Dispatcher via Application.Current?.Dispatcher.
// Built-in xUnit fixture pattern: no Xunit.StaFact or other extension package is required.
public sealed class WpfApplicationFixture : IDisposable
{
    private readonly Thread _staThread;
    private readonly ManualResetEventSlim _ready = new(initialState: false);
    private Dispatcher? _dispatcher;

    public WpfApplicationFixture()
    {
        _staThread = new Thread(() =>
        {
            if (Application.Current is null)
            {
                _ = new Application();
            }

            _dispatcher = Dispatcher.CurrentDispatcher;
            _ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WpfApplicationFixture-STA"
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
        _ready.Wait();
    }

    public void Dispose()
    {
        _dispatcher?.InvokeShutdown();
        _ready.Dispose();
    }
}

[CollectionDefinition(nameof(WpfApplicationFixtureScope))]
public sealed class WpfApplicationFixtureScope : ICollectionFixture<WpfApplicationFixture>
{
}
