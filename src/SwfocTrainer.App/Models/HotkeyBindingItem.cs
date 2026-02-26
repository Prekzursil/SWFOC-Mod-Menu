using System.ComponentModel;

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Gesture)));
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActionId)));
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PayloadJson)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
