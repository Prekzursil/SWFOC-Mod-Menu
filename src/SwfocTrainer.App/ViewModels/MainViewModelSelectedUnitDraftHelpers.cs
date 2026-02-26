namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelSelectedUnitDraftHelpers
{
    internal sealed record SelectedUnitFloatInputs(
        string HpInput,
        string ShieldInput,
        string SpeedInput,
        string DamageInput,
        string CooldownInput);

    internal sealed record SelectedUnitFloatValues(
        float? Hp,
        float? Shield,
        float? Speed,
        float? Damage,
        float? Cooldown);

    internal static bool TryParseSelectedUnitFloatValues(
        SelectedUnitFloatInputs inputs,
        out SelectedUnitFloatValues values,
        out string error)
    {
        var hp = default(float?);
        var shield = default(float?);
        var speed = default(float?);
        var damage = default(float?);
        var cooldown = default(float?);

        if (!MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(inputs.HpInput, "HP must be a number.", out hp, out error))
        {
            values = new SelectedUnitFloatValues(null, null, null, null, null);
            return false;
        }

        if (!MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(inputs.ShieldInput, "Shield must be a number.", out shield, out error))
        {
            values = new SelectedUnitFloatValues(null, null, null, null, null);
            return false;
        }

        if (!MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(inputs.SpeedInput, "Speed must be a number.", out speed, out error))
        {
            values = new SelectedUnitFloatValues(null, null, null, null, null);
            return false;
        }

        if (!MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(inputs.DamageInput, "Damage multiplier must be a number.", out damage, out error))
        {
            values = new SelectedUnitFloatValues(null, null, null, null, null);
            return false;
        }

        if (!MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(inputs.CooldownInput, "Cooldown multiplier must be a number.", out cooldown, out error))
        {
            values = new SelectedUnitFloatValues(null, null, null, null, null);
            return false;
        }

        values = new SelectedUnitFloatValues(hp, shield, speed, damage, cooldown);
        return true;
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
