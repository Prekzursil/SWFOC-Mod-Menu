using System.Reflection;
using System.Windows.Input;

namespace SwfocTrainer.Tests.V2;

/// <summary>
/// Phase 1 thread A — drives an <see cref="ICommand"/> body to completion
/// without a WPF dispatcher loop. <c>AsyncRelayCommand.Execute</c> uses
/// fire-and-forget Task.Run internally; this helper digs out the
/// underlying executeAsync delegate via reflection so tests can await it
/// directly. Keeps the App-side VM tests pump-free.
/// </summary>
public static class AsyncCommandPump
{
    public static Task PumpAsync(ICommand command)
    {
        var field = command.GetType().GetField(
            "_executeAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(command) is Func<Task> fn)
        {
            return fn();
        }
        // Fallback: synchronous Execute. Some sync RelayCommand shapes don't
        // expose an awaitable; we assume work completes synchronously.
        command.Execute(null);
        return Task.CompletedTask;
    }
}
