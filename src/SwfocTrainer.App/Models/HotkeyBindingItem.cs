namespace SwfocTrainer.App.Models;

public sealed class HotkeyBindingItem
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
        }
    }
}
