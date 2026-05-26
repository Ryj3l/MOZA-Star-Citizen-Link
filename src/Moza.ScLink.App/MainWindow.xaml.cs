using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Moza.ScLink.App.ViewModels;

namespace Moza.ScLink.App;

[SuppressMessage(
    "Microsoft.Design",
    "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
    Justification = "WPF Window instances are not disposed by callers; the view model is disposed in OnClosed per WPF lifecycle.")]
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
