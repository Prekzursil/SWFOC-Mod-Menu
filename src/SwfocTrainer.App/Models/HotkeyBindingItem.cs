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
            var safeValue = value ?? string.Empty;
            if (_gesture == safeValue)
            {
                return;
            }

            _gesture = safeValue;
        }
    }

    public string ActionId
    {
        get => _actionId;
        set
        {
            var safeValue = value ?? string.Empty;
            if (_actionId == safeValue)
            {
                return;
            }

            _actionId = safeValue;
        }
    }

    public string PayloadJson
    {
        get => _payloadJson;
        set
        {
            var safeValue = value ?? "{}";
            if (_payloadJson == safeValue)
            {
                return;
            }

            _payloadJson = safeValue;
        }
    }
}
