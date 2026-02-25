using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Profiles.Validation;

public static class ProfileValidator
{
    private static readonly HashSet<string> AllowedBackendPreferences = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto",
        "extender",
        "helper",
        "memory"
    };

    private static readonly HashSet<string> AllowedHostPreferences = new(StringComparer.OrdinalIgnoreCase)
    {
        "starwarsg_preferred",
        "any"
    };

    public static void Validate(TrainerProfile profile)
    {
        ValidateRequiredFields(profile);
        ValidateOptionalPreference(
            profile.Id,
            profile.BackendPreference,
            AllowedBackendPreferences,
            "backendPreference must be one of: auto|extender|helper|memory.");
        ValidateOptionalPreference(
            profile.Id,
            profile.HostPreference,
            AllowedHostPreferences,
            "hostPreference must be one of: starwarsg_preferred|any.");
    }

    private static void ValidateRequiredFields(TrainerProfile profile)
    {
        EnsureCondition(!string.IsNullOrWhiteSpace(profile.Id), "Profile id cannot be empty.");
        EnsureCondition(
            !string.IsNullOrWhiteSpace(profile.DisplayName),
            $"Profile '{profile.Id}' displayName cannot be empty.");
        EnsureCondition(
            profile.ExeTarget != ExeTarget.Unknown,
            $"Profile '{profile.Id}' must define a valid exeTarget.");
        EnsureCondition(
            profile.SignatureSets.Count > 0 || !string.IsNullOrWhiteSpace(profile.Inherits),
            $"Profile '{profile.Id}' must contain at least one signature set (or inherit from a profile that does).");
        EnsureCondition(
            !string.IsNullOrWhiteSpace(profile.SaveSchemaId),
            $"Profile '{profile.Id}' requires saveSchemaId.");
    }

    private static void ValidateOptionalPreference(
        string profileId,
        string? rawValue,
        IReadOnlySet<string> allowedValues,
        string validationSuffix)
    {
        var normalized = (rawValue ?? string.Empty).Trim();
        if (normalized.Length == 0 || allowedValues.Contains(normalized))
        {
            return;
        }

        throw new InvalidDataException($"Profile '{profileId}' {validationSuffix}");
    }

    private static void EnsureCondition(bool isValid, string errorMessage)
    {
        if (!isValid)
        {
            throw new InvalidDataException(errorMessage);
        }
    }
}
