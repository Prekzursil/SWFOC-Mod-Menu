using System.Windows.Input;
using System.Diagnostics;
using System.Windows;

namespace SwfocTrainer.App.Infrastructure;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncCommand(Func<Task> execute)
        : this(execute, null)
    {
    }

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        return !_isRunning && (_canExecute?.Invoke() ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            // Unhandled exceptions in async void crash the WPF process. Prefer surfacing an error and keeping the app alive.
            Debug.WriteLine(ex);
            try
            {
                MessageBox.Show(ex.Message, "Operation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                // ignored
            }
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public static void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
