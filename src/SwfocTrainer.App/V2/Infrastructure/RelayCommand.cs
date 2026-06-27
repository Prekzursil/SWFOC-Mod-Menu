using System.Windows.Input;
using System.ComponentModel;
using System.IO;
using System.Security;

namespace SwfocTrainer.App.V2.Infrastructure;

// ============================================================================
// V2 self-contained RelayCommand. Intentionally minimal, zero dependencies on
// CommunityToolkit.Mvvm or any other MVVM framework. The legacy editor uses
// SwfocTrainer.App.Infrastructure.AsyncCommand; V2 keeps its own so the two
// code paths stay independent and the legacy window can be quarantined later
// without breaking V2.
// ============================================================================

/// <summary>
/// Synchronous <see cref="ICommand"/> implementation with a parameterless
/// action and an optional predicate.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public static void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>
/// 2026-04-28 (iter 75): generic RelayCommand variant that takes a typed
/// parameter from the binding (e.g. the selected DataGrid row). Used by
/// the activity-log Pin/Unpin context menus where the command needs to
/// know which row was clicked.
/// </summary>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(Cast(parameter)) ?? true;

    public void Execute(object? parameter) => _execute(Cast(parameter));

    private static T? Cast(object? parameter) => parameter is T t ? t : default;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

/// <summary>
/// Asynchronous <see cref="ICommand"/> implementation. Swallows exceptions
/// from the provided task and forwards them to the supplied error handler so
/// button handlers never crash the WPF dispatcher.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _isRunning;

    public AsyncRelayCommand(
        Func<Task> executeAsync,
        Func<bool>? canExecute = null,
        Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(executeAsync);
        _executeAsync = executeAsync;
        _canExecute = canExecute;
        _onError = onError;
    }

    public bool CanExecute(object? parameter)
    {
        return !_isRunning && (_canExecute?.Invoke() ?? true);
    }

    public void Execute(object? parameter)
    {
        _ = ExecuteAsyncInternal();
    }

    private async Task ExecuteAsyncInternal()
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _executeAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error.
        }
        catch (IOException ex)
        {
            _onError?.Invoke(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            _onError?.Invoke(ex);
        }
        catch (SecurityException ex)
        {
            _onError?.Invoke(ex);
        }
        catch (InvalidOperationException ex)
        {
            _onError?.Invoke(ex);
        }
        catch (ArgumentException ex)
        {
            _onError?.Invoke(ex);
        }
        catch (Win32Exception ex)
        {
            _onError?.Invoke(ex);
        }
        finally
        {
            _isRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
