using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelCreditsHelpers
{
    internal static bool TryParseCreditsValue(string creditsValue, out int value, out string errorStatus)
    {
        if (int.TryParse(creditsValue, out value) && value >= 0)
        {
            errorStatus = string.Empty;
            return true;
        }

        value = 0;
        errorStatus = "✗ Invalid credits value. Enter a positive whole number.";
        return false;
    }

    internal static string ResolveCreditsStateTag(ActionExecutionResult result, bool creditsFreeze)
    {
        var stateTag = MainViewModelDiagnostics.ReadDiagnosticString(result.Diagnostics, "creditsStateTag");
        if (!string.IsNullOrWhiteSpace(stateTag))
        {
            return stateTag;
        }

        return creditsFreeze ? "HOOK_LOCK" : "HOOK_ONESHOT";
    }

    internal static CreditsStatusResult BuildCreditsSuccessStatus(
        bool creditsFreeze,
        int value,
        string stateTag,
        string diagnosticsSuffix)
    {
        if (creditsFreeze)
        {
            if (!stateTag.Equals("HOOK_LOCK", StringComparison.OrdinalIgnoreCase))
            {
                return CreditsStatusResult.Failure(
                    $"✗ Credits: unexpected state '{stateTag}' for lock mode.{diagnosticsSuffix}");
            }

            return CreditsStatusResult.Success(
                shouldFreeze: true,
                statusMessage: $"✓ [HOOK_LOCK] Credits locked to {value:N0} (float+int hook active){diagnosticsSuffix}");
        }

        if (!stateTag.Equals("HOOK_ONESHOT", StringComparison.OrdinalIgnoreCase))
        {
            return CreditsStatusResult.Failure(
                $"✗ Credits: unexpected state '{stateTag}' for one-shot mode.{diagnosticsSuffix}");
        }

        return CreditsStatusResult.Success(
            shouldFreeze: false,
            statusMessage: $"✓ [HOOK_ONESHOT] Credits set to {value:N0} (float+int sync){diagnosticsSuffix}");
    }
}

internal readonly record struct CreditsStatusResult(bool IsValid, bool ShouldFreeze, string StatusMessage)
{
    internal static CreditsStatusResult Success(bool shouldFreeze, string statusMessage)
    {
        return new CreditsStatusResult(true, shouldFreeze, statusMessage);
    }

    internal static CreditsStatusResult Failure(string statusMessage)
    {
        return new CreditsStatusResult(false, false, statusMessage);
    }
}
