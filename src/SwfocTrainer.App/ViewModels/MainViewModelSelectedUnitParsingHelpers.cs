namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelSelectedUnitParsingHelpers
{
    internal static bool TryParseSelectedUnitFloat(
        string input,
        string errorMessage,
        out float? value,
        out string error)
    {
        if (TryParseOptionalFloat(input, out value))
        {
            error = string.Empty;
            return true;
        }

        error = errorMessage;
        return false;
    }

    internal static bool TryParseSelectedUnitInt(
        string input,
        string errorMessage,
        out int? value,
        out string error)
    {
        if (TryParseOptionalInt(input, out value))
        {
            error = string.Empty;
            return true;
        }

        error = errorMessage;
        return false;
    }

    private static bool TryParseOptionalFloat(string input, out float? value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = null;
            return true;
        }

        if (float.TryParse(input, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryParseOptionalInt(string input, out int? value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = null;
            return true;
        }

        if (int.TryParse(input, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }
}
