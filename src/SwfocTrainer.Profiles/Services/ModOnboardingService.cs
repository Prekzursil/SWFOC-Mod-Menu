#pragma warning disable S4136
using System.Globalization;
using System.Text.RegularExpressions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;

namespace SwfocTrainer.Profiles.Services;

public sealed class ModOnboardingService : IModOnboardingService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex SteamModRegex = new(@"STEAMMOD=(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex ModPathRegex = new("MODPATH=(?<path>\"[^\"]+\"|\\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
    private static readonly HashSet<string> ReservedPathTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "starwarsg",
        "steammod",
        "modpath",
        "language",
        "english",
        "steamapps",
        "steamlibrary",
        "common",
        "gamedata",
        "corruption",
        "swfoc",
        "sweaw",
        "exe",
        "mods",
        "mod"
    };

    private readonly IProfileRepository _profiles;
    private readonly ProfileRepositoryOptions _options;

    public ModOnboardingService(IProfileRepository profiles, ProfileRepositoryOptions options)
    {
        _profiles = profiles;
        _options = options;
    }

    public async Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest request, CancellationToken cancellationToken)
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

        var baseProfile = await _profiles.ResolveInheritedProfileAsync(request.BaseProfileId, cancellationToken);
        var profileId = NormalizeProfileId(request.DraftProfileId);
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

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["profileAliases"] = string.Join(',', aliases),
            ["localPathHints"] = string.Join(',', pathHints)
        };

        if (workshopIds.Count > 0)
        {
            metadata["requiredWorkshopIds"] = string.Join(',', workshopIds);
        }

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            metadata["onboardingNotes"] = request.Notes.Trim();
        }

        var draftProfile = new TrainerProfile(
            Id: profileId,
            DisplayName: request.DisplayName.Trim(),
            Inherits: baseProfile.Id,
            ExeTarget: baseProfile.ExeTarget,
            SteamWorkshopId: workshopIds.Count > 0 ? workshopIds[0] : baseProfile.SteamWorkshopId,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["customModDraft"] = true,
                ["allow_fog_patch_fallback"] = false,
                ["allow_unit_cap_patch_fallback"] = false,
                ["requires_calibration_before_mutation"] = true
            },
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: baseProfile.SaveSchemaId,
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata);

        var outputPath = ResolveDraftPath(profileId, request.NamespaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var json = JsonProfileSerializer.Serialize(draftProfile);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);

        return new ModOnboardingResult(
            Succeeded: true,
            ProfileId: profileId,
            OutputPath: outputPath,
            InferredWorkshopIds: workshopIds,
            InferredPathHints: pathHints,
            InferredAliases: aliases,
            Warnings: warnings);
    }

    public async Task<ModOnboardingBatchResult> ScaffoldDraftProfilesFromSeedsAsync(
        ModOnboardingSeedBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Seeds is null || request.Seeds.Count == 0)
        {
            throw new InvalidDataException("At least one generated profile seed is required.");
        }

        var usedProfileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ModOnboardingBatchItemResult>(request.Seeds.Count);
        for (var index = 0; index < request.Seeds.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seed = request.Seeds[index];
            var item = await ScaffoldDraftFromSeedAsync(seed, index, request.TargetNamespaceRoot, usedProfileIds, cancellationToken);
            results.Add(item);
        }

        var succeededCount = results.Count(x => x.Succeeded);
        var failedCount = results.Count - succeededCount;
        return new ModOnboardingBatchResult(
            Succeeded: failedCount == 0,
            Attempted: results.Count,
            SucceededCount: succeededCount,
            FailedCount: failedCount,
            Results: results);
    }

    private async Task<ModOnboardingBatchItemResult> ScaffoldDraftFromSeedAsync(  // NOSONAR
        GeneratedProfileSeed seed,
        int index,
        string? namespaceRoot,
        ISet<string> usedProfileIds,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var resolvedSeedDraftProfileId = ResolveSeedDraftProfileId(seed);
        var resolvedDisplayName = ResolveSeedDisplayName(seed);
        var resolvedSourceRunId = ResolveSeedSourceRunId(seed);
        var seedProfileId = resolvedSeedDraftProfileId
            ?? seed.DraftProfileId
            ?? seed.WorkshopId
            ?? seed.Title
            ?? string.Empty;
        string? profileId = null;
        string? outputPath = null;
        IReadOnlyList<string> workshopIds = Array.Empty<string>();
        IReadOnlyList<string> pathHints = Array.Empty<string>();
        IReadOnlyList<string> aliases = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(resolvedSeedDraftProfileId))
        {
            errors.Add("DraftProfileId is required.");
        }

        if (string.IsNullOrWhiteSpace(resolvedDisplayName))
        {
            errors.Add("DisplayName is required.");
        }

        if (string.IsNullOrWhiteSpace(resolvedSourceRunId))
        {
            errors.Add("SourceRunId is required.");
        }

        if (!double.IsFinite(seed.Confidence))
        {
            errors.Add("Confidence must be finite.");
        }

        var baseProfileId = ResolveBaseProfileId(seed);
        if (string.IsNullOrWhiteSpace(baseProfileId))
        {
            errors.Add("BaseProfileId or ParentProfile is required.");
        }

        if (errors.Count == 0)
        {
            try
            {
                var displayName = resolvedDisplayName!.Trim();
                profileId = NormalizeProfileId(resolvedSeedDraftProfileId!);
                if (!usedProfileIds.Add(profileId))
                {
                    errors.Add($"Duplicate normalized DraftProfileId '{profileId}' in batch request.");
                }
                else
                {
                    var baseProfile = await _profiles.ResolveInheritedProfileAsync(baseProfileId!, cancellationToken);
                    var launchSamples = seed.LaunchSamples ?? Array.Empty<ModLaunchSample>();
                    workshopIds = MergeWorkshopIds(seed.WorkshopId, seed.RequiredWorkshopIds, InferWorkshopIds(launchSamples));
                    pathHints = MergePathHints(seed.LocalPathHints, InferPathHints(launchSamples));
                    aliases = InferAliases(profileId, displayName, seed.ProfileAliases);
                    var requiredCapabilities = MergeRequiredCapabilities(baseProfile.RequiredCapabilities, seed.RequiredCapabilities);

                    if (workshopIds.Count == 0)
                    {
                        warnings.Add("No STEAMMOD markers were inferred from launch samples.");
                    }

                    if (pathHints.Count == 0)
                    {
                        warnings.Add("No local path hints were inferred from launch samples.");
                    }

                    var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["origin"] = "auto_discovery",
                        ["sourceRunId"] = resolvedSourceRunId!.Trim(),
                        ["confidence"] = seed.Confidence.ToString("0.00", CultureInfo.InvariantCulture),
                        ["parentProfile"] = ResolveParentProfile(seed, baseProfile.Id)
                    };

                    if (workshopIds.Count > 0)
                    {
                        metadata["requiredWorkshopIds"] = string.Join(',', workshopIds);
                    }

                    if (aliases.Count > 0)
                    {
                        metadata["profileAliases"] = string.Join(',', aliases);
                    }

                    if (pathHints.Count > 0)
                    {
                        metadata["localPathHints"] = string.Join(',', pathHints);
                    }

                    if (!string.IsNullOrWhiteSpace(seed.Notes))
                    {
                        metadata["onboardingNotes"] = seed.Notes.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(seed.Title))
                    {
                        metadata["seedTitle"] = seed.Title.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(seed.WorkshopId))
                    {
                        metadata["workshopId"] = seed.WorkshopId.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(seed.CandidateBaseProfile))
                    {
                        metadata["candidateBaseProfile"] = seed.CandidateBaseProfile.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(seed.RiskLevel))
                    {
                        metadata["riskLevel"] = NormalizeRiskLevel(seed.RiskLevel);
                    }

                    if (seed.ParentDependencies is { Count: > 0 })
                    {
                        metadata["parentDependencies"] = string.Join(',', seed.ParentDependencies.Where(x => !string.IsNullOrWhiteSpace(x)));
                    }

                    if (seed.LaunchHints is { Count: > 0 })
                    {
                        metadata["launchHints"] = string.Join(',', seed.LaunchHints.Where(x => !string.IsNullOrWhiteSpace(x)));
                    }

                    if (seed.AnchorHints is { Count: > 0 })
                    {
                        metadata["anchorHints"] = string.Join(',', seed.AnchorHints.Where(x => !string.IsNullOrWhiteSpace(x)));
                    }

                    if (requiredCapabilities.Count > 0)
                    {
                        metadata["requiredCapabilities"] = string.Join(',', requiredCapabilities);
                    }

                    var draftProfile = new TrainerProfile(
                        Id: profileId,
                        DisplayName: displayName,
                        Inherits: baseProfile.Id,
                        ExeTarget: baseProfile.ExeTarget,
                        SteamWorkshopId: workshopIds.Count > 0 ? workshopIds[0] : baseProfile.SteamWorkshopId,
                        SignatureSets: Array.Empty<SignatureSet>(),
                        FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
                        Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
                        FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["customModDraft"] = true,
                            ["auto_discovery"] = true,
                            ["allow_fog_patch_fallback"] = false,
                            ["allow_unit_cap_patch_fallback"] = false,
                            ["requires_calibration_before_mutation"] = true
                        },
                        CatalogSources: Array.Empty<CatalogSource>(),
                        SaveSchemaId: baseProfile.SaveSchemaId,
                        HelperModHooks: Array.Empty<HelperHookSpec>(),
                        Metadata: metadata,
                        RequiredCapabilities: requiredCapabilities);

                    outputPath = ResolveDraftPath(profileId, namespaceRoot);
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                    var json = JsonProfileSerializer.Serialize(draftProfile);
                    await File.WriteAllTextAsync(outputPath, json, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }

        return new ModOnboardingBatchItemResult(
            Index: index,
            SeedProfileId: seedProfileId,
            Succeeded: errors.Count == 0,
            ProfileId: profileId,
            OutputPath: outputPath,
            InferredWorkshopIds: workshopIds,
            InferredPathHints: pathHints,
            InferredAliases: aliases,
            Warnings: warnings,
            Errors: errors);
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

    private static string? ResolveSeedDraftProfileId(GeneratedProfileSeed seed)
    {
        if (!string.IsNullOrWhiteSpace(seed.DraftProfileId))
        {
            return seed.DraftProfileId;
        }

        if (!string.IsNullOrWhiteSpace(seed.WorkshopId))
        {
            return $"workshop_{seed.WorkshopId}";
        }

        if (!string.IsNullOrWhiteSpace(seed.Title))
        {
            return seed.Title;
        }

        return null;
    }

    private static string? ResolveSeedDisplayName(GeneratedProfileSeed seed)
    {
        if (!string.IsNullOrWhiteSpace(seed.DisplayName))
        {
            return seed.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(seed.Title))
        {
            return seed.Title;
        }

        if (!string.IsNullOrWhiteSpace(seed.WorkshopId))
        {
            return $"Workshop Mod {seed.WorkshopId}";
        }

        return null;
    }

    private static string? ResolveSeedSourceRunId(GeneratedProfileSeed seed)
    {
        return string.IsNullOrWhiteSpace(seed.SourceRunId)
            ? null
            : seed.SourceRunId.Trim();
    }

    private static string? ResolveBaseProfileId(GeneratedProfileSeed seed)
    {
        if (!string.IsNullOrWhiteSpace(seed.BaseProfileId))
        {
            return seed.BaseProfileId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(seed.CandidateBaseProfile))
        {
            return seed.CandidateBaseProfile.Trim();
        }

        if (!string.IsNullOrWhiteSpace(seed.ParentProfile))
        {
            return seed.ParentProfile.Trim();
        }

        return null;
    }

    private static string ResolveParentProfile(GeneratedProfileSeed seed, string fallbackProfileId)
    {
        if (!string.IsNullOrWhiteSpace(seed.ParentProfile))
        {
            return seed.ParentProfile.Trim();
        }

        if (!string.IsNullOrWhiteSpace(seed.CandidateBaseProfile))
        {
            return seed.CandidateBaseProfile.Trim();
        }

        return fallbackProfileId;
    }

    private static IReadOnlyList<string> MergeWorkshopIds(
        string? workshopId,
        IReadOnlyList<string>? declared,
        IReadOnlyList<string> inferred)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(workshopId))
        {
            merged.Add(workshopId.Trim());
        }

        if (declared is not null)
        {
            foreach (var id in declared.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                merged.Add(id.Trim());
            }
        }

        foreach (var id in inferred.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            merged.Add(id.Trim());
        }

        return merged.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> MergePathHints(
        IReadOnlyList<string>? declared,
        IReadOnlyList<string> inferred)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (declared is not null)
        {
            foreach (var hint in declared.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var normalized = new string(hint.Trim().ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray()).Trim('_');
                if (IsPathHintCandidate(normalized))
                {
                    merged.Add(normalized);
                }
            }
        }

        foreach (var hint in inferred.Where(x => !string.IsNullOrWhiteSpace(x)))  // NOSONAR
        {
            if (IsPathHintCandidate(hint))
            {
                merged.Add(hint);
            }
        }

        return merged.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
    }

    private static IReadOnlyList<string> MergeRequiredCapabilities(
        IReadOnlyList<string>? inheritedCapabilities,
        IReadOnlyList<string>? discoveredCapabilities)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (inheritedCapabilities is not null)
        {
            foreach (var capability in inheritedCapabilities.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                merged.Add(capability.Trim());
            }
        }

        if (discoveredCapabilities is not null)
        {
            foreach (var capability in discoveredCapabilities.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                merged.Add(capability.Trim());
            }
        }

        return merged.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string NormalizeRiskLevel(string? riskLevel)
    {
        var normalized = string.IsNullOrWhiteSpace(riskLevel)
            ? "medium"
            : riskLevel.Trim().ToLowerInvariant();
        return normalized is "low" or "medium" or "high"
            ? normalized
            : "medium";
    }

    private static IReadOnlyList<string> InferWorkshopIds(IReadOnlyList<ModLaunchSample> samples)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in samples)  // NOSONAR
        {
            if (string.IsNullOrWhiteSpace(sample.CommandLine))
            {
                continue;
            }

            foreach (Match match in SteamModRegex.Matches(sample.CommandLine))
            {
                var id = match.Groups["id"].Value;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> InferPathHints(IReadOnlyList<ModLaunchSample> samples)  // NOSONAR
    {
        var hints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sample in samples)
        {
            var inputs = new List<string>();
            if (!string.IsNullOrWhiteSpace(sample.ProcessPath))
            {
                inputs.Add(sample.ProcessPath);
            }

            if (!string.IsNullOrWhiteSpace(sample.CommandLine))
            {
                inputs.Add(sample.CommandLine);

                foreach (Match match in ModPathRegex.Matches(sample.CommandLine))
                {
                    var raw = match.Groups["path"].Value.Trim();
                    if (raw.StartsWith('"') && raw.EndsWith('"') && raw.Length > 1)
                    {
                        raw = raw[1..^1];
                    }

                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        inputs.Add(raw);
                    }
                }
            }

            foreach (var input in inputs)
            {
                foreach (var token in TokenizeHintInput(input))
                {
                    if (!IsPathHintCandidate(token))
                    {
                        continue;
                    }

                    hints.Add(token);
                }
            }
        }

        return hints.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
    }

    private static bool IsPathHintCandidate(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 3)
        {
            return false;
        }

        if (ReservedPathTokens.Contains(token))
        {
            return false;
        }

        return true;
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

    private static IReadOnlyList<string> InferAliases(string profileId, string displayName, IReadOnlyList<string>? userAliases)  // NOSONAR
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            profileId
        };

        var displayAlias = new string(displayName
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray())
            .Trim('_');

        if (!string.IsNullOrWhiteSpace(displayAlias))
        {
            aliases.Add(displayAlias);
        }

        if (userAliases is not null)
        {
            foreach (var alias in userAliases)
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                var normalized = new string(alias
                    .Trim()
                    .ToLowerInvariant()
                    .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
                    .ToArray())
                    .Trim('_');

                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    aliases.Add(normalized);
                }
            }
        }

        return aliases.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest request)
    {
        return ScaffoldDraftProfileAsync(request, CancellationToken.None);
    }

    public Task<ModOnboardingBatchResult> ScaffoldDraftProfilesFromSeedsAsync(ModOnboardingSeedBatchRequest request)
    {
        return ScaffoldDraftProfilesFromSeedsAsync(request, CancellationToken.None);
    }
}
