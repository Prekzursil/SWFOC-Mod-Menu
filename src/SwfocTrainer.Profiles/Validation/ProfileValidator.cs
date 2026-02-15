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
    }
}
