using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Profiles.Validation;

public static class ProfileValidator
{
    public static void Validate(TrainerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            throw new InvalidDataException("Profile id cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            throw new InvalidDataException($"Profile '{profile.Id}' displayName cannot be empty.");
        }

        if (profile.ExeTarget == ExeTarget.Unknown)
        {
            throw new InvalidDataException($"Profile '{profile.Id}' must define a valid exeTarget.");
        }

        if (profile.SignatureSets.Count == 0 && string.IsNullOrWhiteSpace(profile.Inherits))
        {
            throw new InvalidDataException($"Profile '{profile.Id}' must contain at least one signature set (or inherit from a profile that does).");
        }

        if (string.IsNullOrWhiteSpace(profile.SaveSchemaId))
        {
            throw new InvalidDataException($"Profile '{profile.Id}' requires saveSchemaId.");
        }

        var backendPreference = (profile.BackendPreference ?? string.Empty).Trim();
        if (backendPreference.Length > 0 &&
            !backendPreference.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
            !backendPreference.Equals("extender", StringComparison.OrdinalIgnoreCase) &&
            !backendPreference.Equals("helper", StringComparison.OrdinalIgnoreCase) &&
            !backendPreference.Equals("memory", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Profile '{profile.Id}' backendPreference must be one of: auto|extender|helper|memory.");
        }

        var hostPreference = (profile.HostPreference ?? string.Empty).Trim();
        if (hostPreference.Length > 0 &&
            !hostPreference.Equals("starwarsg_preferred", StringComparison.OrdinalIgnoreCase) &&
            !hostPreference.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Profile '{profile.Id}' hostPreference must be one of: starwarsg_preferred|any.");
        }
    }
}
