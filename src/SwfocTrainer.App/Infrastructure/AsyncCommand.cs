using System.IO;
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
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        return !_isRunning && (_canExecute?.Invoke() ?? true);
    }

    public void Execute(object? parameter)
    {
        _ = ExecuteAsync(parameter);
    }

    private async Task ExecuteAsync(object? parameter)
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
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine(ex);
            TryShowError(ex.Message);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Debug.WriteLine(ex);
            TryShowError(ex.Message);
        }
        catch (IOException ex)
        {
            Debug.WriteLine(ex);
            TryShowError(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            Debug.WriteLine(ex);
            TryShowError(ex.Message);
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

    private static void TryShowError(string message)
    {
        try
        {
            MessageBox.Show(message, "Operation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (InvalidOperationException)
        {
            // Window may not be available during shutdown.
        }
    }
}
