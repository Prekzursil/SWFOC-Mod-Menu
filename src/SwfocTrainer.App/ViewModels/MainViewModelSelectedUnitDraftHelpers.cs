namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelSelectedUnitDraftHelpers
{
    internal static bool TryParseSelectedUnitFloatValues(
        string hpInput,
        string shieldInput,
        string speedInput,
        string damageInput,
        string cooldownInput,
        out float? hp,
        out float? shield,
        out float? speed,
        out float? damage,
        out float? cooldown,
        out string error)
    {
        hp = null;
        shield = null;
        speed = null;
        damage = null;
        cooldown = null;

        if (!MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(hpInput, "HP must be a number.", out hp, out error))
        {
            return false;
        }

        if (!MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(shieldInput, "Shield must be a number.", out shield, out error))
        {
            return false;
        }

        if (!MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(speedInput, "Speed must be a number.", out speed, out error))
        {
            return false;
        }

        if (!MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(damageInput, "Damage multiplier must be a number.", out damage, out error))
        {
            return false;
        }

        return MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(cooldownInput, "Cooldown multiplier must be a number.", out cooldown, out error);
    }

    internal static bool TryParseSelectedUnitIntValues(
        string veterancyInput,
        string ownerFactionInput,
        out int? veterancy,
        out int? ownerFaction,
        out string error)
    {
        veterancy = null;
        ownerFaction = null;
        if (!MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt(veterancyInput, "Veterancy must be an integer.", out veterancy, out error))
        {
            return false;
        }

        return MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt(ownerFactionInput, "Owner faction must be an integer.", out ownerFaction, out error);
    }
}
