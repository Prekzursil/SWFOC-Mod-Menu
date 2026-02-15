using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SwfocTrainer.App.Models;

public sealed class HotkeyBindingItem : INotifyPropertyChanged
{
    private string _gesture = "Ctrl+Shift+1";
    private string _actionId = "set_credits";
    private string _payloadJson = "{}";

    public string Gesture
    {
        get => _gesture;
        set
        {
            if (_gesture == value)
            {
                return;
            }

            _gesture = value;
            OnPropertyChanged();
        }
    }

    public string ActionId
    {
        get => _actionId;
        set
        {
            if (_actionId == value)
            {
                return;
            }

            _actionId = value;
            OnPropertyChanged();
        }
    }

    public string PayloadJson
    {
        get => _payloadJson;
        set
        {
            if (_payloadJson == value)
            {
                return;
            }

            _payloadJson = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? memberName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(memberName));
}
