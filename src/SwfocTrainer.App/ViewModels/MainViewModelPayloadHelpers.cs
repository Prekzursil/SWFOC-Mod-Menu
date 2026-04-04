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
        ArgumentNullException.ThrowIfNull(actionId);
        ArgumentNullException.ThrowIfNull(required);
        ArgumentNullException.ThrowIfNull(defaultSymbolByActionId);
        ArgumentNullException.ThrowIfNull(defaultHelperHookByActionId);
        var payload = new JsonObject();

        foreach (var (key, value) in required
            .Select(node => node?.GetValue<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => (key!, BuildRequiredPayloadValue(actionId, key!, defaultSymbolByActionId, defaultHelperHookByActionId))))
        {
            payload[key] = value;
        }

        return payload;
    }

    internal static void ApplyActionSpecificPayloadDefaults(string actionId, JsonObject payload)
    {
        ArgumentNullException.ThrowIfNull(actionId);
        ArgumentNullException.ThrowIfNull(payload);

        if (actionId.Equals(MainViewModelDefaults.ActionSetCredits, StringComparison.OrdinalIgnoreCase))
        {
            payload[MainViewModelDefaults.PayloadLockCredits] = false;
        }

        if (actionId.Equals(MainViewModelDefaults.ActionFreezeSymbol, StringComparison.OrdinalIgnoreCase) &&
            !payload.ContainsKey(MainViewModelDefaults.PayloadIntValue))
        {
            payload[MainViewModelDefaults.PayloadIntValue] = MainViewModelDefaults.DefaultCreditsValue;
        }
    }

    internal static JsonObject BuildCreditsPayload(int value, bool lockCredits)
    {
        return new JsonObject
        {
            [MainViewModelDefaults.PayloadSymbol] = MainViewModelDefaults.SymbolCredits,
            [MainViewModelDefaults.PayloadIntValue] = value,
            [MainViewModelDefaults.PayloadLockCredits] = lockCredits
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
            MainViewModelDefaults.PayloadSymbol => JsonValue.Create(defaultSymbolByActionId.TryGetValue(actionId, out var sym) ? sym : string.Empty),
            MainViewModelDefaults.PayloadIntValue => JsonValue.Create(actionId switch
            {
                MainViewModelDefaults.ActionSetCredits => MainViewModelDefaults.DefaultCreditsValue,
                MainViewModelDefaults.ActionSetUnitCap => MainViewModelDefaults.DefaultUnitCapValue,
                _ => 0
            }),
            MainViewModelDefaults.PayloadFloatValue => JsonValue.Create(1.0f),
            MainViewModelDefaults.PayloadBoolValue => JsonValue.Create(true),
            MainViewModelDefaults.PayloadEnable => JsonValue.Create(true),
            MainViewModelDefaults.PayloadFreeze => JsonValue.Create(!actionId.Equals(MainViewModelDefaults.ActionUnfreezeSymbol, StringComparison.OrdinalIgnoreCase)),
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
