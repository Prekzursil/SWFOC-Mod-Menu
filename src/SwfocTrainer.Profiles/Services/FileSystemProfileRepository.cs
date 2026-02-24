using System.Text.Json;
using System.Text.Json.Serialization;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Validation;

namespace SwfocTrainer.Profiles.Services;

public sealed class FileSystemProfileRepository : IProfileRepository
{
    private readonly ProfileRepositoryOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    public FileSystemProfileRepository(ProfileRepositoryOptions options)
    {
        _options = options;
    }

    public async Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(_options.ProfilesRootPath, _options.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Profile manifest not found: {manifestPath}");
        }

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonSerializer.Deserialize<ProfileManifest>(json, _jsonOptions);
        return manifest ?? throw new InvalidDataException("Failed to deserialize manifest.json");
    }

    public async Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
    {
        var manifest = await LoadManifestAsync(cancellationToken);
        return manifest.Profiles.Select(x => x.Id).ToArray();
    }

    public async Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        var profilePath = Path.Combine(_options.ProfilesRootPath, "profiles", $"{profileId}.json");
        if (!File.Exists(profilePath))
        {
            throw new FileNotFoundException($"Profile file not found: {profilePath}");
        }

        var json = await File.ReadAllTextAsync(profilePath, cancellationToken);
        var profile = JsonSerializer.Deserialize<TrainerProfile>(json, _jsonOptions)
            ?? throw new InvalidDataException($"Failed to deserialize profile '{profileId}'");

        await ValidateProfileAsync(profile, cancellationToken);
        return profile;
    }

    public async Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return await ResolveInternalAsync(profileId, seen, cancellationToken);
    }

    private async Task<TrainerProfile> ResolveInternalAsync(string profileId, HashSet<string> seen, CancellationToken cancellationToken)
    {
        if (!seen.Add(profileId))
        {
            throw new InvalidDataException($"Circular profile inheritance detected at '{profileId}'");
        }

        var current = await LoadProfileAsync(profileId, cancellationToken);
        if (string.IsNullOrWhiteSpace(current.Inherits))
        {
            return current;
        }

        var parent = await ResolveInternalAsync(current.Inherits, seen, cancellationToken);
        return Merge(parent, current);
    }

    public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
    {
        ProfileValidator.Validate(profile);
        return Task.CompletedTask;
    }

    public Task<ProfileManifest> LoadManifestAsync()
    {
        return LoadManifestAsync(CancellationToken.None);
    }

    public Task<IReadOnlyList<string>> ListAvailableProfilesAsync()
    {
        return ListAvailableProfilesAsync(CancellationToken.None);
    }

    public Task<TrainerProfile> LoadProfileAsync(string profileId)
    {
        return LoadProfileAsync(profileId, CancellationToken.None);
    }

    public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId)
    {
        return ResolveInheritedProfileAsync(profileId, CancellationToken.None);
    }

    public Task ValidateProfileAsync(TrainerProfile profile)
    {
        return ValidateProfileAsync(profile, CancellationToken.None);
    }

    private static TrainerProfile Merge(TrainerProfile parent, TrainerProfile child)
    {
        var mergedActions = new Dictionary<string, ActionSpec>(parent.Actions, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in child.Actions)
        {
            mergedActions[kv.Key] = kv.Value;
        }

        var mergedFlags = new Dictionary<string, bool>(parent.FeatureFlags, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in child.FeatureFlags)
        {
            mergedFlags[kv.Key] = kv.Value;
        }

        var mergedOffsets = new Dictionary<string, long>(parent.FallbackOffsets, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in child.FallbackOffsets)
        {
            mergedOffsets[kv.Key] = kv.Value;
        }

        var mergedCatalog = parent.CatalogSources.Concat(child.CatalogSources).ToArray();
        var mergedHooks = parent.HelperModHooks.Concat(child.HelperModHooks).ToArray();
        var mergedSignatures = parent.SignatureSets.Concat(child.SignatureSets).ToArray();
        var mergedRequiredCapabilities = (parent.RequiredCapabilities ?? Array.Empty<string>())
            .Concat(child.RequiredCapabilities ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var mergedExperimentalFeatures = (parent.ExperimentalFeatures ?? Array.Empty<string>())
            .Concat(child.ExperimentalFeatures ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (parent.Metadata is not null)
        {
            foreach (var kv in parent.Metadata)
            {
                metadata[kv.Key] = kv.Value;
            }
        }

        if (child.Metadata is not null)
        {
            foreach (var kv in child.Metadata)
            {
                metadata[kv.Key] = kv.Value;
            }
        }

        return new TrainerProfile(
            Id: child.Id,
            DisplayName: child.DisplayName,
            Inherits: child.Inherits,
            ExeTarget: child.ExeTarget == ExeTarget.Unknown ? parent.ExeTarget : child.ExeTarget,
            SteamWorkshopId: child.SteamWorkshopId ?? parent.SteamWorkshopId,
            SignatureSets: mergedSignatures,
            FallbackOffsets: mergedOffsets,
            Actions: mergedActions,
            FeatureFlags: mergedFlags,
            CatalogSources: mergedCatalog,
            SaveSchemaId: string.IsNullOrWhiteSpace(child.SaveSchemaId) ? parent.SaveSchemaId : child.SaveSchemaId,
            HelperModHooks: mergedHooks,
            Metadata: metadata,
            BackendPreference: string.IsNullOrWhiteSpace(child.BackendPreference) ? parent.BackendPreference : child.BackendPreference,
            RequiredCapabilities: mergedRequiredCapabilities,
            HostPreference: string.IsNullOrWhiteSpace(child.HostPreference) ? parent.HostPreference : child.HostPreference,
            ExperimentalFeatures: mergedExperimentalFeatures);
    }
}
