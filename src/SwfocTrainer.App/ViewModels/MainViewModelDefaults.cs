using System.Collections.Generic;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelDefaults
{
    internal const string ActionSetCredits = "set_credits";
    internal const string ActionFreezeTimer = "freeze_timer";
    internal const string ActionToggleFogReveal = "toggle_fog_reveal";
    internal const string ActionToggleAi = "toggle_ai";
    internal const string ActionSetUnitCap = "set_unit_cap";
    internal const string ActionToggleInstantBuildPatch = "toggle_instant_build_patch";
    internal const string ActionToggleTacticalGodMode = "toggle_tactical_god_mode";
    internal const string ActionToggleTacticalOneHitMode = "toggle_tactical_one_hit_mode";
    internal const string ActionSetGameSpeed = "set_game_speed";
    internal const string ActionFreezeSymbol = "freeze_symbol";
    internal const string ActionUnfreezeSymbol = "unfreeze_symbol";

    internal const string SymbolCredits = "credits";
    internal const string SymbolGameTimerFreeze = "game_timer_freeze";
    internal const string SymbolFogReveal = "fog_reveal";
    internal const string SymbolAiEnabled = "ai_enabled";
    internal const string SymbolUnitCap = "unit_cap";
    internal const string SymbolInstantBuildNop = "instant_build_nop";
    internal const string SymbolTacticalGodMode = "tactical_god_mode";
    internal const string SymbolTacticalOneHitMode = "tactical_one_hit_mode";
    internal const string SymbolGameSpeed = "game_speed";

    internal const string PayloadKeySymbol = "symbol";
    internal const string PayloadKeyIntValue = "intValue";
    internal const string PayloadKeyBoolValue = "boolValue";
    internal const string PayloadKeyEnable = "enable";
    internal const string PayloadKeyFloatValue = "floatValue";
    internal const string PayloadKeyFreeze = "freeze";
    internal const string PayloadKeyLockCredits = "lockCredits";

    internal const string BaseSwfocProfileId = "base_swfoc";

    internal const int DefaultCreditsValue = 1000000;
    internal const int DefaultUnitCapValue = 99999;
    internal const float DefaultGameSpeedValue = 2.0f;
    internal const string DefaultLaunchTarget = "Swfoc";
    internal const string DefaultLaunchMode = "Vanilla";
    internal const string DefaultCreditsValueText = "1000000";
    internal const string DefaultPayloadJsonTemplate = "{\n  \"symbol\": \"credits\",\n  \"intValue\": 1000000,\n  \"lockCredits\": false\n}";

    internal static readonly IReadOnlyDictionary<string, string> DefaultSymbolByActionId =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_symbol"] = SymbolCredits,
            [ActionSetCredits] = SymbolCredits,
            [ActionFreezeTimer] = SymbolGameTimerFreeze,
            [ActionToggleFogReveal] = SymbolFogReveal,
            [ActionToggleAi] = SymbolAiEnabled,
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
            [ActionToggleTacticalGodMode] = SymbolTacticalGodMode,
            [ActionToggleTacticalOneHitMode] = SymbolTacticalOneHitMode,
            [ActionSetGameSpeed] = SymbolGameSpeed,
            [ActionFreezeSymbol] = SymbolCredits,
            [ActionUnfreezeSymbol] = SymbolCredits,
        };

    internal static readonly IReadOnlyDictionary<string, string> DefaultHelperHookByActionId =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["spawn_unit_helper"] = "spawn_bridge",
            ["set_hero_state_helper"] = "aotr_hero_state_bridge",
            ["toggle_roe_respawn_helper"] = "roe_respawn_bridge",
        };
}
