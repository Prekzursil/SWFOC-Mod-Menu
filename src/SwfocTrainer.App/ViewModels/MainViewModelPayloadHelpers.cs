using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelPayloadHelpers
{
    internal static JsonObject BuildRequiredPayloadTemplate(
        string actionId,
        JsonArray required,
        IReadOnlyDictionary<string, string> defaultSymbolByActionId,
        IReadOnlyDictionary<string, string> defaultHelperHookByActionId)
    {
        var payload = new JsonObject();

        foreach (var node in required)
        {
            var key = node?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            payload[key] = BuildRequiredPayloadValue(
                actionId,
                key,
                defaultSymbolByActionId,
                defaultHelperHookByActionId);
        }

        return payload;
    }

    internal static void ApplyActionSpecificPayloadDefaults(string actionId, JsonObject payload)
    {
        if (actionId.Equals(MainViewModelDefaults.ActionSetCredits, StringComparison.OrdinalIgnoreCase))
        {
            payload[MainViewModelDefaults.PayloadKeyLockCredits] = false;
        }

        if (actionId.Equals(MainViewModelDefaults.ActionFreezeSymbol, StringComparison.OrdinalIgnoreCase) &&
            !payload.ContainsKey(MainViewModelDefaults.PayloadKeyIntValue))
        {
            payload[MainViewModelDefaults.PayloadKeyIntValue] = MainViewModelDefaults.DefaultCreditsValue;
        }
    }

    internal static JsonObject BuildCreditsPayload(int value, bool lockCredits)
    {
        return new JsonObject
        {
            [MainViewModelDefaults.PayloadKeySymbol] = MainViewModelDefaults.SymbolCredits,
            [MainViewModelDefaults.PayloadKeyIntValue] = value,
            [MainViewModelDefaults.PayloadKeyLockCredits] = lockCredits
        };
    }

    private static JsonNode? BuildRequiredPayloadValue(
        string actionId,
        string key,
        IReadOnlyDictionary<string, string> defaultSymbolByActionId,
        IReadOnlyDictionary<string, string> defaultHelperHookByActionId)
    {
        return key switch
        {
            MainViewModelDefaults.PayloadKeySymbol => JsonValue.Create(defaultSymbolByActionId.TryGetValue(actionId, out var sym) ? sym : string.Empty),
            MainViewModelDefaults.PayloadKeyIntValue => JsonValue.Create(actionId switch
            {
                MainViewModelDefaults.ActionSetCredits => MainViewModelDefaults.DefaultCreditsValue,
                MainViewModelDefaults.ActionSetUnitCap => MainViewModelDefaults.DefaultUnitCapValue,
                _ => 0
            }),
            MainViewModelDefaults.PayloadKeyFloatValue => JsonValue.Create(1.0f),
            MainViewModelDefaults.PayloadKeyBoolValue => JsonValue.Create(true),
            MainViewModelDefaults.PayloadKeyEnable => JsonValue.Create(true),
            MainViewModelDefaults.PayloadKeyFreeze => JsonValue.Create(!actionId.Equals(MainViewModelDefaults.ActionUnfreezeSymbol, StringComparison.OrdinalIgnoreCase)),
            "patchBytes" => JsonValue.Create("90 90 90 90 90"),
            "originalBytes" => JsonValue.Create("48 8B 74 24 68"),
            "helperHookId" => JsonValue.Create(defaultHelperHookByActionId.TryGetValue(actionId, out var hook) ? hook : actionId),
            "unitId" => JsonValue.Create(string.Empty),
            "entryMarker" => JsonValue.Create(string.Empty),
            "faction" => JsonValue.Create(string.Empty),
            "globalKey" => JsonValue.Create(string.Empty),
            "nodePath" => JsonValue.Create(string.Empty),
            "value" => JsonValue.Create(string.Empty),
            _ => JsonValue.Create(string.Empty)
        };
    }
}
