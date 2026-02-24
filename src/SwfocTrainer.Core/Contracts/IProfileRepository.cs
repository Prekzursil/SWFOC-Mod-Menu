using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IProfileRepository
{
    Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken);

    Task<ProfileManifest> LoadManifestAsync()
    {
        return LoadManifestAsync(CancellationToken.None);
    }

    Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken);

    Task<TrainerProfile> LoadProfileAsync(string profileId)
    {
        return LoadProfileAsync(profileId, CancellationToken.None);
    }

    Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken);

    Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId)
    {
        return ResolveInheritedProfileAsync(profileId, CancellationToken.None);
    }

    Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken);

    Task ValidateProfileAsync(TrainerProfile profile)
    {
        return ValidateProfileAsync(profile, CancellationToken.None);
    }

    Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListAvailableProfilesAsync()
    {
        return ListAvailableProfilesAsync(CancellationToken.None);
    }
}
