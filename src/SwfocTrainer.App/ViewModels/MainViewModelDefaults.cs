using System.Collections.Generic;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelDefaults
{
    internal static readonly IReadOnlyDictionary<string, string> DefaultSymbolByActionId =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_symbol"] = "credits",
            ["set_credits"] = "credits",
            ["freeze_timer"] = "game_timer_freeze",
            ["toggle_fog_reveal"] = "fog_reveal",
            ["toggle_ai"] = "ai_enabled",
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
            ["toggle_tactical_god_mode"] = "tactical_god_mode",
            ["toggle_tactical_one_hit_mode"] = "tactical_one_hit_mode",
            ["set_game_speed"] = "game_speed",
            ["freeze_symbol"] = "credits",
            ["unfreeze_symbol"] = "credits",
        };

    internal static readonly IReadOnlyDictionary<string, string> DefaultHelperHookByActionId =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["spawn_unit_helper"] = "spawn_bridge",
            ["set_hero_state_helper"] = "aotr_hero_state_bridge",
            ["toggle_roe_respawn_helper"] = "roe_respawn_bridge",
        };
}
