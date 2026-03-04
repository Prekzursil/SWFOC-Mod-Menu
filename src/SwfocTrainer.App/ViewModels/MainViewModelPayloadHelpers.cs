using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelPayloadHelpers
{
    private const string PayloadPlacementModeKey = "placementMode";
    private const string PayloadAllowCrossFactionKey = "allowCrossFaction";
    private const string PayloadForceOverrideKey = "forceOverride";
    private const string PayloadPopulationPolicyKey = "populationPolicy";
    private const string PayloadPersistencePolicyKey = "persistencePolicy";

    private static readonly IReadOnlyDictionary<string, Action<JsonObject>> ActionPayloadDefaults =
        new Dictionary<string, Action<JsonObject>>(StringComparer.OrdinalIgnoreCase)
        {
            ["spawn_tactical_entity"] = ApplySpawnTacticalDefaults,
            ["spawn_galactic_entity"] = ApplySpawnGalacticDefaults,
            ["place_planet_building"] = ApplyPlanetBuildingDefaults,
            ["transfer_fleet_safe"] = ApplyTransferFleetDefaults,
            ["flip_planet_owner"] = ApplyPlanetFlipDefaults,
            ["switch_player_faction"] = ApplySwitchPlayerFactionDefaults,
            ["edit_hero_state"] = ApplyEditHeroStateDefaults,
            ["create_hero_variant"] = ApplyCreateHeroVariantDefaults
        };

    internal static JsonObject BuildRequiredPayloadTemplate(
        string actionId,
        JsonArray required,
        IReadOnlyDictionary<string, string> defaultSymbolByActionId,
        IReadOnlyDictionary<string, string> defaultHelperHookByActionId)
    {
        if (actionId is null)
        {
            throw new ArgumentNullException(nameof(actionId));
        }
        if (required is null)
        {
            throw new ArgumentNullException(nameof(required));
        }
        if (defaultSymbolByActionId is null)
        {
            throw new ArgumentNullException(nameof(defaultSymbolByActionId));
        }
        if (defaultHelperHookByActionId is null)
        {
            throw new ArgumentNullException(nameof(defaultHelperHookByActionId));
        }

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
        if (actionId is null)
        {
            throw new ArgumentNullException(nameof(actionId));
        }
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (actionId.Equals(MainViewModelDefaults.ActionSetCredits, StringComparison.OrdinalIgnoreCase))
        {
            payload[MainViewModelDefaults.PayloadKeyLockCredits] = false;
        }

        if (actionId.Equals(MainViewModelDefaults.ActionFreezeSymbol, StringComparison.OrdinalIgnoreCase) &&
            !payload.ContainsKey(MainViewModelDefaults.PayloadKeyIntValue))
        {
            payload[MainViewModelDefaults.PayloadKeyIntValue] = MainViewModelDefaults.DefaultCreditsValue;
        }

        if (ActionPayloadDefaults.TryGetValue(actionId, out var applyDefaults))
        {
            applyDefaults?.Invoke(payload);
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
        if (actionId is null)
        {
            throw new ArgumentNullException(nameof(actionId));
        }
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }
        if (defaultSymbolByActionId is null)
        {
            throw new ArgumentNullException(nameof(defaultSymbolByActionId));
        }
        if (defaultHelperHookByActionId is null)
        {
            throw new ArgumentNullException(nameof(defaultHelperHookByActionId));
        }

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
            "desiredState" => JsonValue.Create("alive"),
            "populationPolicy" => JsonValue.Create("Normal"),
            "persistencePolicy" => JsonValue.Create("PersistentGalactic"),
            PayloadPlacementModeKey => JsonValue.Create(string.Empty),
            PayloadAllowCrossFactionKey => JsonValue.Create(true),
            "allowDuplicate" => JsonValue.Create(false),
            PayloadForceOverrideKey => JsonValue.Create(false),
            "planetFlipMode" => JsonValue.Create("convert_everything"),
            "flipMode" => JsonValue.Create("convert_everything"),
            "variantGenerationMode" => JsonValue.Create("patch_mod_overlay"),
            "nodePath" => JsonValue.Create(string.Empty),
            "value" => JsonValue.Create(string.Empty),
            _ => JsonValue.Create(string.Empty)
        };
    }

    private static void ApplySpawnTacticalDefaults(JsonObject payload)
    {
        var targetPayload = payload ?? throw new ArgumentNullException(nameof(payload));
        ApplySpawnDefaults(targetPayload, "ForceZeroTactical", "EphemeralBattleOnly");
        targetPayload[PayloadPlacementModeKey] ??= "reinforcement_zone";
    }

    private static void ApplySpawnGalacticDefaults(JsonObject payload)
    {
        var targetPayload = payload ?? throw new ArgumentNullException(nameof(payload));
        ApplySpawnDefaults(targetPayload, "Normal", "PersistentGalactic");
    }

    private static void ApplyPlanetBuildingDefaults(JsonObject payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }
        payload[PayloadPlacementModeKey] ??= "safe_rules";
        payload[PayloadAllowCrossFactionKey] ??= true;
        payload[PayloadForceOverrideKey] ??= false;
    }

    private static void ApplyTransferFleetDefaults(JsonObject payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }
        payload[PayloadPlacementModeKey] ??= "safe_transfer";
        payload[PayloadAllowCrossFactionKey] ??= true;
        payload[PayloadForceOverrideKey] ??= false;
    }

    private static void ApplyPlanetFlipDefaults(JsonObject payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }
        payload["flipMode"] ??= "convert_everything";
        payload["planetFlipMode"] ??= payload["flipMode"]?.GetValue<string>() ?? "convert_everything";
        payload[PayloadAllowCrossFactionKey] ??= true;
        payload[PayloadForceOverrideKey] ??= false;
    }

    private static void ApplySwitchPlayerFactionDefaults(JsonObject payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }
        payload[PayloadAllowCrossFactionKey] ??= true;
    }

    private static void ApplyEditHeroStateDefaults(JsonObject payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }
        payload["desiredState"] ??= "alive";
        payload["allowDuplicate"] ??= false;
    }

    private static void ApplyCreateHeroVariantDefaults(JsonObject payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }
        payload["variantGenerationMode"] ??= "patch_mod_overlay";
        payload[PayloadAllowCrossFactionKey] ??= true;
    }

    private static void ApplySpawnDefaults(JsonObject payload, string populationPolicy, string persistencePolicy)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        payload[PayloadPopulationPolicyKey] ??= populationPolicy;
        payload[PayloadPersistencePolicyKey] ??= persistencePolicy;
        payload[PayloadAllowCrossFactionKey] ??= true;
    }
}
