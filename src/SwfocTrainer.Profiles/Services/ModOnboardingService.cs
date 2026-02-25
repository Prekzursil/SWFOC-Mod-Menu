using System.Text.RegularExpressions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;

namespace SwfocTrainer.Profiles.Services;

public sealed class ModOnboardingService : IModOnboardingService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex SteamModRegex = new(
        @"STEAMMOD=(?<id>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex ModPathRegex = new(
        "MODPATH=(?<path>\"[^\"]+\"|\\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private readonly IProfileRepository _profiles;
    private readonly ProfileRepositoryOptions _options;

    public ModOnboardingService(IProfileRepository profiles, ProfileRepositoryOptions options)
    {
        _profiles = profiles;
        _options = options;
    }

    public async Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest request, CancellationToken cancellationToken)
    {
        ValidateDraftProfileRequest(request);

        var baseProfile = await _profiles.ResolveInheritedProfileAsync(request.BaseProfileId, cancellationToken);
        var profileId = NormalizeProfileId(request.DraftProfileId);
        var draftSignals = BuildDraftSignals(request, profileId);
        var metadata = BuildDraftMetadata(
            draftSignals.WorkshopIds,
            draftSignals.PathHints,
            draftSignals.Aliases,
            request.Notes,
            request.AdditionalMetadata);
        var requiredCapabilities = NormalizeRequiredCapabilities(request.RequiredCapabilities);

        var draftProfile = new TrainerProfile(
            Id: profileId,
            DisplayName: request.DisplayName.Trim(),
            Inherits: baseProfile.Id,
            ExeTarget: baseProfile.ExeTarget,
            SteamWorkshopId: draftSignals.WorkshopIds.Count > 0 ? draftSignals.WorkshopIds[0] : baseProfile.SteamWorkshopId,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["customModDraft"] = true
            },
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: baseProfile.SaveSchemaId,
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata,
            RequiredCapabilities: requiredCapabilities);

        var outputPath = ResolveDraftPath(profileId, request.NamespaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var json = JsonProfileSerializer.Serialize(draftProfile);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);

        return new ModOnboardingResult(
            Succeeded: true,
            ProfileId: profileId,
            OutputPath: outputPath,
            InferredWorkshopIds: draftSignals.WorkshopIds,
            InferredPathHints: draftSignals.PathHints,
            InferredAliases: draftSignals.Aliases,
            Warnings: draftSignals.Warnings);
    }

    public Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest request)
    {
        return ScaffoldDraftProfileAsync(request, CancellationToken.None);
    }

    public async Task<ModOnboardingBatchResult> ScaffoldDraftProfilesFromSeedsAsync(
        ModOnboardingSeedBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Seeds is null || request.Seeds.Count == 0)
        {
            throw new InvalidDataException("At least one generated profile seed is required.");
        }

        var itemResults = new List<ModOnboardingBatchItemResult>(request.Seeds.Count);

        foreach (var seed in request.Seeds)
        {
            var warnings = new List<string>();
            try
            {
                var onboardingRequest = CreateSeedOnboardingRequest(seed, request, warnings);
                var result = await ScaffoldDraftProfileAsync(onboardingRequest, cancellationToken);
                warnings.AddRange(result.Warnings);
                itemResults.Add(new ModOnboardingBatchItemResult(
                    WorkshopId: seed.WorkshopId,
                    ProfileId: result.ProfileId,
                    OutputPath: result.OutputPath,
                    Succeeded: true,
                    Warnings: warnings));
            }
            catch (Exception ex)
            {
                itemResults.Add(new ModOnboardingBatchItemResult(
                    WorkshopId: seed.WorkshopId,
                    ProfileId: string.Empty,
                    OutputPath: string.Empty,
                    Succeeded: false,
                    Warnings: warnings,
                    Error: ex.Message));
            }
        }

        var generated = itemResults.Count(item => item.Succeeded);
        var failed = itemResults.Count - generated;
        return new ModOnboardingBatchResult(
            Succeeded: failed == 0,
            Total: itemResults.Count,
            Generated: generated,
            Failed: failed,
            Items: itemResults);
    }

    private static ModOnboardingRequest CreateSeedOnboardingRequest(
        GeneratedProfileSeed seed,
        ModOnboardingSeedBatchRequest request,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(seed.WorkshopId))
        {
            throw new InvalidDataException("Seed workshop id is required.");
        }

        if (string.IsNullOrWhiteSpace(seed.Title))
        {
            throw new InvalidDataException($"Seed '{seed.WorkshopId}' title is required.");
        }

        var baseProfileId = string.IsNullOrWhiteSpace(seed.CandidateBaseProfile)
            ? request.FallbackBaseProfileId
            : seed.CandidateBaseProfile.Trim();

        if (string.IsNullOrWhiteSpace(baseProfileId))
        {
            throw new InvalidDataException($"Seed '{seed.WorkshopId}' does not specify a base profile.");
        }

        var launchSamples = BuildSeedLaunchSamples(seed, warnings);
        var profileAliases = BuildSeedAliases(seed);
        var additionalMetadata = BuildSeedMetadata(seed, baseProfileId);

        return new ModOnboardingRequest(
            DraftProfileId: BuildSeedDraftProfileId(seed),
            DisplayName: seed.Title.Trim(),
            BaseProfileId: baseProfileId,
            LaunchSamples: launchSamples,
            ProfileAliases: profileAliases,
            NamespaceRoot: request.NamespaceRoot,
            Notes: $"generated from workshop discovery seed {seed.SourceRunId}",
            RequiredCapabilities: seed.RequiredCapabilities,
            AdditionalMetadata: additionalMetadata);
    }

    private static IReadOnlyList<ModLaunchSample> BuildSeedLaunchSamples(GeneratedProfileSeed seed, List<string> warnings)
    {
        var steamModIds = seed.LaunchHints.SteamModIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!steamModIds.Contains(seed.WorkshopId, StringComparer.OrdinalIgnoreCase))
        {
            steamModIds.Insert(0, seed.WorkshopId.Trim());
        }

        var modPathHints = seed.LaunchHints.ModPathHints
            .Where(hint => !string.IsNullOrWhiteSpace(hint))
            .Select(hint => hint.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (modPathHints.Length == 0)
        {
            warnings.Add($"Seed '{seed.WorkshopId}' had no modPathHints; relying on STEAMMOD launch markers only.");
        }

        var steamModSegment = string.Join(' ', steamModIds.Select(id => $"STEAMMOD={id}"));
        var modPathSegment = modPathHints.Length > 0
            ? $" MODPATH=Mods\\\\{modPathHints[0]}"
            : string.Empty;
        var commandLine = $"StarWarsG.exe {steamModSegment}{modPathSegment}".Trim();
        return [new ModLaunchSample("StarWarsG.exe", null, commandLine)];
    }

    private static IReadOnlyList<string> BuildSeedAliases(GeneratedProfileSeed seed)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BuildSeedDraftProfileId(seed),
            seed.WorkshopId.Trim()
        };

        var titleAlias = NormalizeAlias(seed.Title);
        if (!string.IsNullOrWhiteSpace(titleAlias))
        {
            aliases.Add(titleAlias!);
        }

        foreach (var modPathHint in seed.LaunchHints.ModPathHints)
        {
            var normalized = NormalizeAlias(modPathHint);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                aliases.Add(normalized!);
            }
        }

        return aliases.OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyDictionary<string, string> BuildSeedMetadata(GeneratedProfileSeed seed, string baseProfileId)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["origin"] = "auto_discovery",
            ["sourceRunId"] = seed.SourceRunId,
            ["confidence"] = seed.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            ["riskLevel"] = seed.RiskLevel,
            ["parentProfile"] = baseProfileId,
            ["parentDependencies"] = string.Join(',', seed.ParentDependencies)
        };

        if (seed.AnchorHints is not null && seed.AnchorHints.Count > 0)
        {
            metadata["anchorHintFeatures"] = string.Join(
                ',',
                seed.AnchorHints
                    .Where(kv => kv.Value.Count > 0)
                    .Select(kv => kv.Key)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        return metadata;
    }

    private static string BuildSeedDraftProfileId(GeneratedProfileSeed seed)
    {
        var titleToken = new string(seed.Title
                .Trim()
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
                .ToArray())
            .Trim('_');

        if (string.IsNullOrWhiteSpace(titleToken))
        {
            titleToken = "mod";
        }

        return $"custom_{titleToken}_{seed.WorkshopId}_swfoc";
    }

    private string ResolveDraftPath(string profileId, string? namespaceRoot)
    {
        var defaultNamespaceRoot = Directory.GetParent(_options.ProfilesRootPath)?.FullName ?? _options.ProfilesRootPath;
        var normalizedNamespace = NormalizeNamespace(namespaceRoot);
        var customProfilesDir = Path.Combine(defaultNamespaceRoot, normalizedNamespace, "profiles");
        return Path.Combine(customProfilesDir, $"{profileId}.json");
    }

    private static string NormalizeNamespace(string? namespaceRoot)
    {
        if (string.IsNullOrWhiteSpace(namespaceRoot))
        {
            return "custom";
        }

        var sanitized = new string(namespaceRoot
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray())
            .Trim('_');

        return string.IsNullOrWhiteSpace(sanitized) ? "custom" : sanitized;
    }

    private static string NormalizeProfileId(string profileId)
    {
        var normalized = new string(profileId
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray())
            .Trim('_');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidDataException("Draft profile id normalized to empty value.");
        }

        if (!normalized.StartsWith("custom_", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"custom_{normalized}";
        }

        return normalized;
    }

    private static IReadOnlyList<string> InferWorkshopIds(IReadOnlyList<ModLaunchSample> samples)
    {
        return samples
            .Where(sample => !string.IsNullOrWhiteSpace(sample.CommandLine))
            .SelectMany(sample => SteamModRegex.Matches(sample.CommandLine!)
                .Select(match => match.Groups["id"].Value))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> InferPathHints(IReadOnlyList<ModLaunchSample> samples)
    {
        var hints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sample in samples)
        {
            foreach (var input in CollectSampleHintInputs(sample))
            {
                foreach (var token in TokenizeHintInput(input))
                {
                    if (token.Length < 3)
                    {
                        continue;
                    }

                    if (IsNoiseHintToken(token))
                    {
                        continue;
                    }

                    hints.Add(token);
                }
            }
        }

        return hints.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
    }

    private static IEnumerable<string> TokenizeHintInput(string input)
    {
        var cleaned = input
            .Replace('\\', ' ')
            .Replace('/', ' ')
            .Replace('=', ' ')
            .Replace('"', ' ')
            .Replace('(', ' ')
            .Replace(')', ' ')
            .Replace('-', ' ')
            .Replace('.', ' ');

        foreach (var token in cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = new string(token
                .ToLowerInvariant()
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '_')
                .ToArray())
                .Trim('_');

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static IReadOnlyList<string> InferAliases(string profileId, string displayName, IReadOnlyList<string>? userAliases)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            profileId
        };

        var displayAlias = NormalizeAlias(displayName);
        if (displayAlias is not null)
        {
            aliases.Add(displayAlias);
        }

        if (userAliases is not null)
        {
            foreach (var normalized in userAliases
                         .Select(NormalizeAlias)
                         .Where(static value => !string.IsNullOrWhiteSpace(value))
                         .Select(static value => value!))
            {
                aliases.Add(normalized);
            }
        }

        return aliases.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> CollectSampleHintInputs(ModLaunchSample sample)
    {
        if (!string.IsNullOrWhiteSpace(sample.ProcessPath))
        {
            yield return sample.ProcessPath;
        }

        if (string.IsNullOrWhiteSpace(sample.CommandLine))
        {
            yield break;
        }

        yield return sample.CommandLine;
        foreach (var path in ExtractModPathValues(sample.CommandLine))
        {
            yield return path;
        }
    }

    private static IEnumerable<string> ExtractModPathValues(string commandLine)
    {
        foreach (var match in ModPathRegex.Matches(commandLine).Select(match => match.Groups["path"].Value.Trim()))
        {
            if (string.IsNullOrWhiteSpace(match))
            {
                continue;
            }

            if (match.StartsWith('"') && match.EndsWith('"') && match.Length > 1)
            {
                yield return match[1..^1];
                continue;
            }

            yield return match;
        }
    }

    private static bool IsNoiseHintToken(string token)
    {
        return token is "steamapps" or "workshop" or "content" or "corruption" or "swfoc" or "starwarsg" or "language" or "english";
    }

    private static string? NormalizeAlias(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray())
            .Trim('_');

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void ValidateDraftProfileRequest(ModOnboardingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DraftProfileId))
        {
            throw new InvalidDataException("DraftProfileId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new InvalidDataException("DisplayName is required.");
        }

        if (request.LaunchSamples is null || request.LaunchSamples.Count == 0)
        {
            throw new InvalidDataException("At least one launch sample is required.");
        }
    }

    private static DraftSignals BuildDraftSignals(ModOnboardingRequest request, string profileId)
    {
        var workshopIds = InferWorkshopIds(request.LaunchSamples);
        var pathHints = InferPathHints(request.LaunchSamples);
        var aliases = InferAliases(profileId, request.DisplayName, request.ProfileAliases);
        var warnings = new List<string>();
        if (workshopIds.Count == 0)
        {
            warnings.Add("No STEAMMOD markers were inferred from launch samples.");
        }

        if (pathHints.Count == 0)
        {
            warnings.Add("No local path hints were inferred from launch samples.");
        }

        return new DraftSignals(workshopIds, pathHints, aliases, warnings);
    }

    private static Dictionary<string, string> BuildDraftMetadata(
        IReadOnlyList<string> workshopIds,
        IReadOnlyList<string> pathHints,
        IReadOnlyList<string> aliases,
        string? notes,
        IReadOnlyDictionary<string, string>? additionalMetadata)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["profileAliases"] = string.Join(',', aliases),
            ["localPathHints"] = string.Join(',', pathHints)
        };

        if (workshopIds.Count > 0)
        {
            metadata["requiredWorkshopIds"] = string.Join(',', workshopIds);
        }

        if (!string.IsNullOrWhiteSpace(notes))
        {
            metadata["onboardingNotes"] = notes.Trim();
        }

        if (additionalMetadata is null)
        {
            return metadata;
        }

        foreach (var kv in additionalMetadata)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value))
            {
                continue;
            }

            metadata[kv.Key.Trim()] = kv.Value.Trim();
        }

        return metadata;
    }

    private static string[]? NormalizeRequiredCapabilities(IReadOnlyList<string>? requiredCapabilities)
    {
        return requiredCapabilities?
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Select(capability => capability.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record DraftSignals(
        IReadOnlyList<string> WorkshopIds,
        IReadOnlyList<string> PathHints,
        IReadOnlyList<string> Aliases,
        IReadOnlyList<string> Warnings);
}
