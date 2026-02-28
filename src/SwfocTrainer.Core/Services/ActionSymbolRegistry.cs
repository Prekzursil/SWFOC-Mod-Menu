namespace SwfocTrainer.Core.Services;

internal static class ActionSymbolRegistry
{
    private const string SymbolCredits = "credits";

    private static readonly IReadOnlyDictionary<string, string> ActionSymbols =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_symbol"] = SymbolCredits,
            ["set_credits"] = SymbolCredits,
            ["set_credits_extender_experimental"] = SymbolCredits,
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
            ["freeze_symbol"] = SymbolCredits,
            ["unfreeze_symbol"] = SymbolCredits,
            ["set_unit_cap"] = "unit_cap",
        };

    public static bool TryGetSymbol(string actionId, out string symbol)
    {
        if (ActionSymbols.TryGetValue(actionId, out symbol!))
        {
            return true;
        }

        symbol = string.Empty;
        return false;
    }
}
