using System.Collections.Generic;
using System.Text.Json.Nodes;
using SwfocTrainer.App.Models;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelDefaults
{
    internal const string UnknownValue = "unknown";
    internal const string BundleBlockedValue = "blocked";
    internal const string BundlePassValue = "bundle_pass";

    internal const string BaseSwfocProfileId = "base_swfoc";

    internal const string SetCreditsActionId = "set_credits";
    internal const string FreezeTimerActionId = "freeze_timer";
    internal const string ToggleFogRevealActionId = "toggle_fog_reveal";
    internal const string ToggleAiActionId = "toggle_ai";
    internal const string SetUnitCapActionId = "set_unit_cap";
    internal const string ToggleInstantBuildPatchActionId = "toggle_instant_build_patch";
    internal const string ToggleTacticalGodModeActionId = "toggle_tactical_god_mode";
    internal const string ToggleTacticalOneHitModeActionId = "toggle_tactical_one_hit_mode";
    internal const string SetGameSpeedActionId = "set_game_speed";
    internal const string FreezeSymbolActionId = "freeze_symbol";
    internal const string UnfreezeSymbolActionId = "unfreeze_symbol";

    internal const string CreditsSymbol = "credits";
    internal const string GameTimerFreezeSymbol = "game_timer_freeze";
    internal const string FogRevealSymbol = "fog_reveal";
    internal const string AiEnabledSymbol = "ai_enabled";
    internal const string UnitCapSymbol = "unit_cap";
    internal const string InstantBuildNopSymbol = "instant_build_nop";
    internal const string TacticalGodModeSymbol = "tactical_god_mode";
    internal const string TacticalOneHitModeSymbol = "tactical_one_hit_mode";
    internal const string GameSpeedSymbol = "game_speed";

    internal const string SymbolKey = "symbol";
    internal const string IntValueKey = "intValue";
    internal const string BoolValueKey = "boolValue";
    internal const string FloatValueKey = "floatValue";
    internal const string LockCreditsKey = "lockCredits";
    internal const string EnableKey = "enable";
    internal const string FreezeKey = "freeze";

    internal const string HookLockState = "HOOK_LOCK";
    internal const string HookOneShotState = "HOOK_ONESHOT";

    internal static readonly IReadOnlyDictionary<string, string> DefaultSymbolByActionId =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_symbol"] = CreditsSymbol,
            [SetCreditsActionId] = CreditsSymbol,
            [FreezeTimerActionId] = GameTimerFreezeSymbol,
            [ToggleFogRevealActionId] = FogRevealSymbol,
            [ToggleAiActionId] = AiEnabledSymbol,
            ["set_instant_build_multiplier"] = "instant_build",
            ["set_selected_hp"] = "selected_hp",
            ["set_selected_shield"] = "selected_shield",
            ["set_selected_speed"] = "selected_speed",
            ["set_selected_damage_multiplier"] = "selected_damage_multiplier",
            ["set_selected_cooldown_multiplier"] = "selected_cooldown_multiplier",
            ["set_selected_veterancy"] = "selected_veterancy",
            ["set_selected_owner_faction"] = "selected_owner_faction",
            ["set_planet_owner"] = "planet_owner",
            ["set_hero_respawn_timer"] = "hero_respawn_timer",
            [ToggleTacticalGodModeActionId] = TacticalGodModeSymbol,
            [ToggleTacticalOneHitModeActionId] = TacticalOneHitModeSymbol,
            [SetGameSpeedActionId] = GameSpeedSymbol,
            [FreezeSymbolActionId] = CreditsSymbol,
            [UnfreezeSymbolActionId] = CreditsSymbol,
        };

    internal static readonly IReadOnlyDictionary<string, string> DefaultHelperHookByActionId =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["spawn_unit_helper"] = "spawn_bridge",
            ["set_hero_state_helper"] = "aotr_hero_state_bridge",
            ["toggle_roe_respawn_helper"] = "roe_respawn_bridge",
        };

    internal static IReadOnlyList<HotkeyBindingItem> CreateDefaultHotkeyBindings()
    {
        return new List<HotkeyBindingItem>
        {
            CreateDefaultHotkeyBinding("Ctrl+Shift+1", SetCreditsActionId),
            CreateDefaultHotkeyBinding("Ctrl+Shift+2", FreezeTimerActionId),
            CreateDefaultHotkeyBinding("Ctrl+Shift+3", ToggleFogRevealActionId),
            CreateDefaultHotkeyBinding("Ctrl+Shift+4", ToggleInstantBuildPatchActionId),
            CreateDefaultHotkeyBinding("Ctrl+Shift+5", FreezeSymbolActionId),
        };
    }

    internal static JsonObject BuildDefaultHotkeyPayload(string actionId)
    {
        return actionId switch
        {
            SetCreditsActionId => new JsonObject
            {
                [SymbolKey] = CreditsSymbol,
                [IntValueKey] = 1000000,
                [LockCreditsKey] = false,
            },
            FreezeTimerActionId => new JsonObject
            {
                [SymbolKey] = GameTimerFreezeSymbol,
                [BoolValueKey] = true,
            },
            ToggleFogRevealActionId => new JsonObject
            {
                [SymbolKey] = FogRevealSymbol,
                [BoolValueKey] = true,
            },
            SetUnitCapActionId => new JsonObject
            {
                [SymbolKey] = UnitCapSymbol,
                [IntValueKey] = 99999,
                [EnableKey] = true,
            },
            ToggleInstantBuildPatchActionId => new JsonObject
            {
                [EnableKey] = true,
            },
            SetGameSpeedActionId => new JsonObject
            {
                [SymbolKey] = GameSpeedSymbol,
                [FloatValueKey] = 2.0f,
            },
            FreezeSymbolActionId => new JsonObject
            {
                [SymbolKey] = CreditsSymbol,
                [FreezeKey] = true,
                [IntValueKey] = 1000000,
            },
            UnfreezeSymbolActionId => new JsonObject
            {
                [SymbolKey] = CreditsSymbol,
                [FreezeKey] = false,
            },
            _ => new JsonObject(),
        };
    }

    private static HotkeyBindingItem CreateDefaultHotkeyBinding(string gesture, string actionId)
    {
        return new HotkeyBindingItem
        {
            Gesture = gesture,
            ActionId = actionId,
            PayloadJson = BuildDefaultHotkeyPayload(actionId).ToJsonString(),
        };
    }
}
