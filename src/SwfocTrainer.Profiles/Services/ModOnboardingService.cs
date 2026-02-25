#pragma warning disable S4136
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

    public Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest request)
    {
        return ScaffoldDraftProfileAsync(request, CancellationToken.None);
    }
}
