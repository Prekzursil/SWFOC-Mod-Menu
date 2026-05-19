using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base for V2 view-models. Kept
/// deliberately tiny so V2 code is obvious at a glance. Any V2 view-model that
/// needs more sophisticated behavior can override <see cref="OnPropertyChanged"/>.
/// </summary>
public abstract class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Sets a backing field, raises <see cref="PropertyChanged"/> when the value
    /// actually changes, and returns whether the value was updated. Uses
    /// <see cref="EqualityComparer{T}.Default"/> so records and reference types
    /// behave consistently.
    /// </summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
