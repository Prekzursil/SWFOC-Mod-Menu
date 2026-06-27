using System.Windows;
using SwfocTrainer.App.V2.ViewModels;

namespace SwfocTrainer.App.V2;

// ============================================================================
// MainWindowV2 code-behind
//
// Minimal. Accepts the view-model via constructor (supplied by the V2
// bootstrap in Program.cs), wires DataContext, fires the one-time async
// initialization on Loaded, and disposes the view-model on Closing. There is
// no hotkey handler, no tab-switch logic, no command injection here.
// ============================================================================

public partial class MainWindowV2 : Window
{
    private readonly MainViewModelV2 _viewModel;

    public MainWindowV2(MainViewModelV2 viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The V2 view-model traps runtime probe failures itself. This handler
        // only catches expected WPF/bootstrap failures that can occur while the
        // window is still usable.
        try
        {
            await _viewModel.OnWindowLoadedAsync().ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            ShowStartupWarning(ex);
        }
        catch (NotSupportedException ex)
        {
            ShowStartupWarning(ex);
        }
        catch (ArgumentException ex)
        {
            ShowStartupWarning(ex);
        }
    }

    private void ShowStartupWarning(Exception ex)
    {
        MessageBox.Show(
            this,
            $"V2 startup probe failed:\n{ex.Message}\n\nThe window is still usable; click the Refresh button on the Connection tab to retry.",
            "V2 startup",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.Dispose();
    }
}
