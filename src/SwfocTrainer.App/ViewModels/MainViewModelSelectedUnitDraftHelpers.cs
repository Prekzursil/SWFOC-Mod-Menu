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
        ArgumentNullException.ThrowIfNull(inputs);

        var empty = new SelectedUnitFloatValues(null, null, null, null, null);
        var fields = new (string Input, string ErrorMessage)[]
        {
            (inputs.HpInput, "HP must be a number."),
            (inputs.ShieldInput, "Shield must be a number."),
            (inputs.SpeedInput, "Speed must be a number."),
            (inputs.DamageInput, "Damage multiplier must be a number."),
            (inputs.CooldownInput, "Cooldown multiplier must be a number."),
        };

        var parsed = new float?[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            if (!MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(
                    fields[i].Input, fields[i].ErrorMessage, out parsed[i], out error))
            {
                values = empty;
                return false;
            }
        }

        error = string.Empty;
        values = new SelectedUnitFloatValues(parsed[0], parsed[1], parsed[2], parsed[3], parsed[4]);
        return true;
    }

    internal static bool TryParseSelectedUnitIntValues(
        string veterancyInput,
        string ownerFactionInput,
        out int? veterancy,
        out int? ownerFaction,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(veterancyInput);
        ArgumentNullException.ThrowIfNull(ownerFactionInput);
        veterancy = null;
        ownerFaction = null;
        if (!MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt(veterancyInput, "Veterancy must be an integer.", out veterancy, out error))
        {
            return false;
        }

        return MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt(ownerFactionInput, "Owner faction must be an integer.", out ownerFaction, out error);
    }
}
