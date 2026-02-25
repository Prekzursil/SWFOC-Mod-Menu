using System.Text.RegularExpressions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class LaunchContextResolver : ILaunchContextResolver
{
    private const string RoeProfileId = "roe_3447786229_swfoc";
    private const string AotrProfileId = "aotr_1397421866_swfoc";
    private const string RoeWorkshopId = "3447786229";
    private const string AotrWorkshopId = "1397421866";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex SteamModRegex = new(
        @"steammod\s*=\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex ModPathRegex = new(
        @"modpath\s*=\s*(?:""(?<quoted>[^""]+)""|(?<unquoted>[^\s]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    public LaunchContext Resolve(ProcessMetadata process, IReadOnlyList<TrainerProfile> profiles)
    {
        var commandLineAvailable = !string.IsNullOrWhiteSpace(process.CommandLine);
        var steamModIds = ExtractSteamModIds(process).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var modPathRaw = ExtractModPath(process.CommandLine);
        var modPathNormalized = NormalizeToken(modPathRaw);
        var detectedVia = ReadMetadata(process, "detectedVia") ?? "unknown";

        var launchKind = DetermineLaunchKind(process, steamModIds, modPathNormalized);
        var recommendation = RecommendProfile(process, steamModIds, modPathNormalized, profiles);

        return new LaunchContext(
            launchKind,
            commandLineAvailable,
            steamModIds,
            modPathRaw,
            modPathNormalized,
            detectedVia,
            recommendation);
    }

    private static LaunchKind DetermineLaunchKind(
        ProcessMetadata process,
        IReadOnlyList<string> steamModIds,
        string? modPathNormalized)
    {
        var hasSteamMods = steamModIds.Count > 0;
        var hasModPath = !string.IsNullOrWhiteSpace(modPathNormalized);
        if (hasSteamMods && hasModPath)
        {
            return LaunchKind.Mixed;
        }

        if (hasSteamMods)
        {
            return LaunchKind.Workshop;
        }

        if (hasModPath)
        {
            return LaunchKind.LocalModPath;
        }

        if (process.ExeTarget is ExeTarget.Sweaw or ExeTarget.Swfoc)
        {
            return LaunchKind.BaseGame;
        }

        return LaunchKind.Unknown;
    }

    private static ProfileRecommendation RecommendProfile(
        ProcessMetadata process,
        IReadOnlyList<string> steamModIds,
        string? modPathNormalized,
        IReadOnlyList<TrainerProfile> profiles)
    {
        var workshopRecommendation = TryRecommendProfileFromWorkshopIds(steamModIds, profiles) ??
                                     TryRecommendLegacyWorkshopProfile(steamModIds);
        if (workshopRecommendation is not null)
        {
            return workshopRecommendation;
        }

        var modPathRecommendation = string.IsNullOrWhiteSpace(modPathNormalized)
            ? null
            : TryRecommendProfileFromModPathHints(modPathNormalized, profiles) ??
              TryRecommendLegacyModPathProfile(modPathNormalized, profiles);
        if (modPathRecommendation is not null)
        {
            return modPathRecommendation;
        }

        return RecommendFallbackBaseProfile(process);
    }

    private static ProfileRecommendation? TryRecommendLegacyWorkshopProfile(IReadOnlyList<string> steamModIds)
    {
        if (steamModIds.Any(id => id.Equals(RoeWorkshopId, StringComparison.OrdinalIgnoreCase)))
        {
            return new ProfileRecommendation(RoeProfileId, "steammod_exact_roe", 1.0d);
        }

        return steamModIds.Any(id => id.Equals(AotrWorkshopId, StringComparison.OrdinalIgnoreCase))
            ? new ProfileRecommendation(AotrProfileId, "steammod_exact_aotr", 1.0d)
            : null;
    }

    private static ProfileRecommendation? TryRecommendLegacyModPathProfile(
        string? modPathNormalized,
        IReadOnlyList<TrainerProfile> profiles)
    {
        if (string.IsNullOrWhiteSpace(modPathNormalized))
        {
            return null;
        }

        if (MatchesProfileHints(RoeProfileId, modPathNormalized, profiles) ||
            LooksLikeRoePath(modPathNormalized))
        {
            return new ProfileRecommendation(RoeProfileId, "modpath_hint_roe", 0.95d);
        }

        return MatchesProfileHints(AotrProfileId, modPathNormalized, profiles) ||
               LooksLikeAotrPath(modPathNormalized)
            ? new ProfileRecommendation(AotrProfileId, "modpath_hint_aotr", 0.95d)
            : null;
    }

    private static ProfileRecommendation RecommendFallbackBaseProfile(ProcessMetadata process)
    {
        if (process.ExeTarget == ExeTarget.Sweaw)
        {
            return new ProfileRecommendation("base_sweaw", "exe_target_sweaw", 0.80d);
        }

        if (process.ExeTarget == ExeTarget.Swfoc || IsStarWarsGProcess(process))
        {
            var confidence = IsStarWarsGProcess(process) ? 0.55d : 0.65d;
            return new ProfileRecommendation("base_swfoc", "foc_safe_starwarsg_fallback", confidence);
        }

        return new ProfileRecommendation(null, "unknown", 0.20d);
    }

    private static ProfileRecommendation? TryRecommendProfileFromWorkshopIds(
        IReadOnlyList<string> steamModIds,
        IReadOnlyList<TrainerProfile> profiles)
    {
        if (steamModIds.Count == 0 || profiles.Count == 0)
        {
            return null;
        }

        var modIds = new HashSet<string>(steamModIds, StringComparer.OrdinalIgnoreCase);
        var candidates = profiles
            .Where(profile => !IsLegacyBaseProfile(profile.Id))
            .Select(profile => BuildWorkshopMatch(profile, modIds))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.MetadataConfidence)
            .ThenBy(match => match.Profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        var selected = candidates[0];

        return new ProfileRecommendation(
            selected.Profile.Id,
            selected.IsExactMatch ? "steammod_profile_exact" : "steammod_profile_dependency_match",
            selected.IsExactMatch ? 0.98d : Math.Min(0.94d, 0.80d + (selected.MatchedWorkshopIds * 0.03d)));
    }

    private static WorkshopProfileMatch BuildWorkshopMatch(TrainerProfile profile, IReadOnlySet<string> modIds)
    {
        var hasExact = !string.IsNullOrWhiteSpace(profile.SteamWorkshopId) &&
                       modIds.Contains(profile.SteamWorkshopId);
        var requiredWorkshopIds = ReadMetadataCsv(profile.Metadata, "requiredWorkshopIds")
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
        var requiredSubsetMatched = requiredWorkshopIds.Length > 0 && requiredWorkshopIds.All(modIds.Contains);
        var matchedWorkshopIds = requiredWorkshopIds.Count(modIds.Contains);

        var score = 0;
        if (hasExact)
        {
            score += 300;
        }

        if (requiredSubsetMatched)
        {
            score += 200 + matchedWorkshopIds;
        }

        if (IsAutoDiscoveryProfile(profile))
        {
            score += 10;
        }

        return new WorkshopProfileMatch(
            profile,
            score,
            hasExact,
            matchedWorkshopIds,
            ReadMetadataConfidence(profile.Metadata));
    }

    private static ProfileRecommendation? TryRecommendProfileFromModPathHints(
        string modPathNormalized,
        IReadOnlyList<TrainerProfile> profiles)
    {
        if (profiles.Count == 0)
        {
            return null;
        }

        var candidates = profiles
            .Where(profile => !IsLegacyBaseProfile(profile.Id))
            .Select(profile =>
            {
                var matchedHints = BuildHints(profile)
                    .Where(hint => modPathNormalized.Contains(hint, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return (Profile: profile, MatchedHints: matchedHints);
            })
            .Where(candidate => candidate.MatchedHints.Length > 0)
            .Select(candidate => new ModPathProfileMatch(
                candidate.Profile,
                candidate.MatchedHints.Max(hint => hint.Length),
                candidate.MatchedHints.Length,
                ReadMetadataConfidence(candidate.Profile.Metadata),
                IsAutoDiscoveryProfile(candidate.Profile)))
            .OrderByDescending(candidate => candidate.LongestHintLength)
            .ThenByDescending(candidate => candidate.HintCount)
            .ThenByDescending(candidate => candidate.IsAutoDiscovery)
            .ThenByDescending(candidate => candidate.MetadataConfidence)
            .ThenBy(candidate => candidate.Profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        var selected = candidates[0];
        var confidence = selected.IsAutoDiscovery ? 0.90d : 0.88d;
        return new ProfileRecommendation(
            selected.Profile.Id,
            selected.IsAutoDiscovery ? "modpath_profile_auto_discovery_hint" : "modpath_profile_hint",
            confidence);
    }

    private static bool IsLegacyBaseProfile(string profileId)
    {
        return profileId.Equals(RoeProfileId, StringComparison.OrdinalIgnoreCase) ||
               profileId.Equals(AotrProfileId, StringComparison.OrdinalIgnoreCase) ||
               profileId.Equals("universal_auto", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesProfileHints(string profileId, string modPathNormalized, IReadOnlyList<TrainerProfile> profiles)
    {
        var profile = profiles.FirstOrDefault(x => x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return false;
        }

        return BuildHints(profile)
            .Any(hint => modPathNormalized.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> BuildHints(TrainerProfile profile)
    {
        var hints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        hints.Add(NormalizeToken(profile.Id) ?? profile.Id.ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(profile.SteamWorkshopId))
        {
            hints.Add(profile.SteamWorkshopId);
        }

        if (profile.Metadata is null)
        {
            return hints.Where(x => !string.IsNullOrWhiteSpace(x));
        }

        foreach (var key in new[] { "localPathHints", "profileAliases" })
        {
            if (!profile.Metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var normalized in raw
                         .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                         .Select(NormalizeToken)
                         .Where(static value => !string.IsNullOrWhiteSpace(value))
                         .Select(static value => value!))
            {
                hints.Add(normalized);
            }
        }

        return hints.Where(x => !string.IsNullOrWhiteSpace(x));
    }

    private static bool IsAutoDiscoveryProfile(TrainerProfile profile)
    {
        return profile.Metadata is not null &&
               profile.Metadata.TryGetValue("origin", out var origin) &&
               string.Equals(origin, "auto_discovery", StringComparison.OrdinalIgnoreCase);
    }

    private static double ReadMetadataConfidence(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null ||
            !metadata.TryGetValue("confidence", out var raw) ||
            string.IsNullOrWhiteSpace(raw) ||
            !double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var confidence))
        {
            return 0.0d;
        }

        return confidence;
    }

    private static IReadOnlyList<string> ReadMetadataCsv(IReadOnlyDictionary<string, string>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool LooksLikeRoePath(string normalizedPath)
    {
        return normalizedPath.Contains("3447786229", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("roe", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("order-66", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("order 66", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeAotrPath(string normalizedPath)
    {
        return normalizedPath.Contains("1397421866", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("aotr", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("awakening-of-the-rebellion", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("awakening of the rebellion", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractModPath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var match = ModPathRegex.Match(commandLine);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["quoted"].Success
            ? match.Groups["quoted"].Value
            : match.Groups["unquoted"].Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Trim('"');
    }

    private static IReadOnlyList<string> ExtractSteamModIds(ProcessMetadata process)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(process.CommandLine))
        {
            foreach (var groups in SteamModRegex.Matches(process.CommandLine).Select(match => match.Groups))
            {
                if (groups.Count > 1 && !string.IsNullOrWhiteSpace(groups[1].Value))
                {
                    ids.Add(groups[1].Value);
                }
            }
        }

        var existingIds = ReadMetadata(process, "steamModIdsDetected");
        if (!string.IsNullOrWhiteSpace(existingIds))
        {
            foreach (var id in existingIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                ids.Add(id);
            }
        }

        return ids.ToArray();
    }

    private static bool IsStarWarsGProcess(ProcessMetadata process)
    {
        if (process.ProcessName.Equals("StarWarsG", StringComparison.OrdinalIgnoreCase) ||
            process.ProcessName.Equals("StarWarsG.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var metadataValue = ReadMetadata(process, "isStarWarsG");
        if (!string.IsNullOrWhiteSpace(metadataValue) &&
            bool.TryParse(metadataValue, out var isStarWarsG))
        {
            return isStarWarsG;
        }

        return process.ProcessPath.Contains("StarWarsG.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadMetadata(ProcessMetadata process, string key)
    {
        if (process.Metadata is null)
        {
            return null;
        }

        return process.Metadata.TryGetValue(key, out var value) ? value : null;
    }

    private static string? NormalizeToken(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var value = input.Trim().Trim('"').Replace('\\', '/');
        while (value.Contains("//", StringComparison.Ordinal))
        {
            value = value.Replace("//", "/", StringComparison.Ordinal);
        }

        return value.ToLowerInvariant();
    }

    private sealed record WorkshopProfileMatch(
        TrainerProfile Profile,
        int Score,
        bool IsExactMatch,
        int MatchedWorkshopIds,
        double MetadataConfidence);

    private sealed record ModPathProfileMatch(
        TrainerProfile Profile,
        int LongestHintLength,
        int HintCount,
        double MetadataConfidence,
        bool IsAutoDiscovery);
}
