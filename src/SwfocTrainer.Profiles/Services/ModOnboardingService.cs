using System.Text.RegularExpressions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;

namespace SwfocTrainer.Profiles.Services;

public sealed class ModOnboardingService : IModOnboardingService
{
    private static readonly Regex SteamModRegex = new(@"STEAMMOD=(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ModPathRegex = new("MODPATH=(?<path>\"[^\"]+\"|\\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
                ["customModDraft"] = true
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
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in samples)
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

    private static IReadOnlyList<string> InferPathHints(IReadOnlyList<ModLaunchSample> samples)
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
                    if (token.Length < 3)
                    {
                        continue;
                    }

                    if (token is "steamapps" or "workshop" or "content" or "corruption" or "swfoc" or "starwarsg" or "language" or "english")
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
}
