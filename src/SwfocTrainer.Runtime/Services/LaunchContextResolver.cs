using System.Text.RegularExpressions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class LaunchContextResolver : ILaunchContextResolver
{
    private static readonly Regex SteamModRegex = new(
        @"steammod\s*=\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ModPathRegex = new(
        @"modpath\s*=\s*(?:""(?<quoted>[^""]+)""|(?<unquoted>[^\s]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public LaunchContext Resolve(ProcessMetadata process, IReadOnlyList<TrainerProfile> profiles)
    {
        var commandLineAvailable = !string.IsNullOrWhiteSpace(process.CommandLine);
        var steamModIds = ExtractSteamModIds(process).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var modPathRaw = ExtractModPath(process.CommandLine);
        var modPathNormalized = NormalizeToken(modPathRaw);
        var detectedVia = ReadMetadata(process, "detectedVia") ?? "unknown";
        var source = ResolveLaunchContextSource(process);

        var launchKind = DetermineLaunchKind(process, steamModIds, modPathNormalized);
        var recommendation = RecommendProfile(process, steamModIds, modPathNormalized, profiles);

        return new LaunchContext(
            launchKind,
            commandLineAvailable,
            steamModIds,
            modPathRaw,
            modPathNormalized,
            detectedVia,
            recommendation,
            source);
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
        var forcedProfileId = ReadMetadata(process, "forcedProfileId");
        if (ResolveLaunchContextSource(process).Equals("forced", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(forcedProfileId))
        {
            return new ProfileRecommendation(forcedProfileId, "forced_profile_id", 1.0d);
        }

        var byWorkshop = RecommendByWorkshop(steamModIds, profiles);
        if (byWorkshop is not null)
        {
            return byWorkshop;
        }

        var byModPath = RecommendByModPath(modPathNormalized, profiles);
        if (byModPath is not null)
        {
            return byModPath;
        }

        if (process.ExeTarget == ExeTarget.Sweaw)
        {
            return new ProfileRecommendation("base_sweaw", "exe_target_sweaw", 0.80d);
        }

        if (process.ExeTarget == ExeTarget.Swfoc || IsStarWarsGProcess(process))
        {
            return new ProfileRecommendation("base_swfoc", "foc_safe_starwarsg_fallback", IsStarWarsGProcess(process) ? 0.55d : 0.65d);
        }

        return new ProfileRecommendation(null, "unknown", 0.20d);
    }

    private static string ResolveLaunchContextSource(ProcessMetadata process)
    {
        var raw = ReadMetadata(process, "launchContextSource");
        return string.Equals(raw, "forced", StringComparison.OrdinalIgnoreCase)
            ? "forced"
            : "detected";
    }

    private static ProfileRecommendation? RecommendByWorkshop(
        IReadOnlyList<string> steamModIds,
        IReadOnlyList<TrainerProfile> profiles)
    {
        if (steamModIds.Count == 0 || profiles.Count == 0)
        {
            return null;
        }

        var ids = steamModIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        TrainerProfile? bestProfile = null;
        var bestScore = 0;
        var bestRequiredCount = 0;

        foreach (var profile in profiles)
        {
            var score = ScoreWorkshopMatch(profile, ids);
            if (score <= 0)
            {
                continue;
            }

            var requiredCount = BuildRequiredWorkshopIds(profile).Count;
            if (bestProfile is null ||
                score > bestScore ||
                score == bestScore && requiredCount > bestRequiredCount ||
                score == bestScore && requiredCount == bestRequiredCount && IsPreferredProfile(profile, bestProfile))
            {
                bestProfile = profile;
                bestScore = score;
                bestRequiredCount = requiredCount;
            }
        }

        if (bestProfile is null)
        {
            return null;
        }

        var confidence = bestScore >= 1000 ? 1.0d : 0.97d;
        return new ProfileRecommendation(bestProfile.Id, BuildReasonCode(bestProfile.Id, "steam"), confidence);
    }

    private static int ScoreWorkshopMatch(TrainerProfile profile, IReadOnlySet<string> steamModIds)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(profile.SteamWorkshopId))
        {
            if (steamModIds.Contains(profile.SteamWorkshopId))
            {
                score = Math.Max(score, 1000);
            }
        }

        var requiredIds = BuildRequiredWorkshopIds(profile);
        if (requiredIds.Count == 0)
        {
            return score;
        }

        var overlapCount = requiredIds.Count(steamModIds.Contains);
        if (overlapCount == requiredIds.Count)
        {
            score = Math.Max(score, 900 + requiredIds.Count);
            return score;
        }

        if (overlapCount > 0)
        {
            score = Math.Max(score, 700 + overlapCount);
        }

        return score;
    }

    private static ProfileRecommendation? RecommendByModPath(
        string? modPathNormalized,
        IReadOnlyList<TrainerProfile> profiles)
    {
        if (string.IsNullOrWhiteSpace(modPathNormalized) || profiles.Count == 0)
        {
            return null;
        }

        TrainerProfile? bestProfile = null;
        var bestScore = 0;
        foreach (var profile in profiles)
        {
            var score = 0;
            foreach (var hint in BuildHints(profile))
            {
                if (modPathNormalized.Contains(hint, StringComparison.OrdinalIgnoreCase))
                {
                    score = Math.Max(score, hint.Length);
                }
            }

            if (score == 0)
            {
                continue;
            }

            if (bestProfile is null || score > bestScore || score == bestScore && IsPreferredProfile(profile, bestProfile))
            {
                bestProfile = profile;
                bestScore = score;
            }
        }

        if (bestProfile is null)
        {
            return null;
        }

        return new ProfileRecommendation(bestProfile.Id, BuildReasonCode(bestProfile.Id, "modpath"), 0.95d);
    }

    private static bool IsPreferredProfile(TrainerProfile candidate, TrainerProfile current)
    {
        var candidatePriority = ResolveProfilePriority(candidate.Id);
        var currentPriority = ResolveProfilePriority(current.Id);
        if (candidatePriority != currentPriority)
        {
            return candidatePriority < currentPriority;
        }

        return string.Compare(candidate.Id, current.Id, StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static int ResolveProfilePriority(string profileId)
    {
        var normalized = profileId.ToLowerInvariant();
        if (normalized.Contains("roe_", StringComparison.Ordinal))
        {
            return 0;
        }

        if (normalized.Contains("aotr_", StringComparison.Ordinal))
        {
            return 1;
        }

        return 2;
    }

    private static string BuildReasonCode(string profileId, string source)
    {
        var normalizedId = profileId.ToLowerInvariant();
        if (string.Equals(source, "steam", StringComparison.OrdinalIgnoreCase))
        {
            if (normalizedId.Contains("roe_", StringComparison.Ordinal))
            {
                return "steammod_exact_roe";
            }

            if (normalizedId.Contains("aotr_", StringComparison.Ordinal))
            {
                return "steammod_exact_aotr";
            }

            return "steammod_exact_profile";
        }

        if (string.Equals(source, "modpath", StringComparison.OrdinalIgnoreCase))
        {
            if (normalizedId.Contains("roe_", StringComparison.Ordinal))
            {
                return "modpath_hint_roe";
            }

            if (normalizedId.Contains("aotr_", StringComparison.Ordinal))
            {
                return "modpath_hint_aotr";
            }

            return "modpath_hint_profile";
        }

        return "unknown";
    }

    private static IReadOnlyList<string> BuildRequiredWorkshopIds(TrainerProfile profile)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(profile.SteamWorkshopId))
        {
            required.Add(profile.SteamWorkshopId);
        }

        if (profile.Metadata is null)
        {
            return required.ToArray();
        }

        foreach (var key in new[] { "requiredWorkshopIds", "requiredWorkshopId" })
        {
            if (!profile.Metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var id in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    required.Add(id);
                }
            }
        }

        return required.ToArray();
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

            foreach (var part in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var normalized = NormalizeToken(part);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    hints.Add(normalized);
                }
            }
        }

        return hints.Where(x => !string.IsNullOrWhiteSpace(x));
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
            foreach (Match match in SteamModRegex.Matches(process.CommandLine))
            {
                if (match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                {
                    ids.Add(match.Groups[1].Value);
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
}
