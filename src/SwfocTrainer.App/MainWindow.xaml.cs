using System.Windows;
using System.Windows.Input;
using SwfocTrainer.App.ViewModels;

namespace SwfocTrainer.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            var vm = (MainViewModel)DataContext;
            vm.LoadProfilesCommand.Execute(null);
            vm.LoadHotkeysCommand.Execute(null);
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private static async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not MainWindow window || window.DataContext is not MainViewModel vm)
        {
            return;
        }

        var gesture = NormalizeGesture(e);
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return;
        }

        var consumed = await vm.ExecuteHotkeyAsync(gesture);
        if (consumed)
        {
            e.Handled = true;
        }
    }

    private static string NormalizeGesture(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.None)
        {
            return string.Empty;
        }

        var parts = new List<string>(4);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            parts.Add("Shift");
        }

        var keyToken = key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)(key - Key.NumPad0)).ToString(),
            _ => key.ToString()
        };

        parts.Add(keyToken);
        return string.Join("+", parts);
    }
}
