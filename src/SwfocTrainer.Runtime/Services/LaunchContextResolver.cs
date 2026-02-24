using System.Text.RegularExpressions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class LaunchContextResolver : ILaunchContextResolver
{
    private const string RoeWorkshopId = "3447786229";
    private const string AotrWorkshopId = "1397421866";

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
        if (steamModIds.Any(id => id.Equals(RoeWorkshopId, StringComparison.OrdinalIgnoreCase)))
        {
            return new ProfileRecommendation("roe_3447786229_swfoc", "steammod_exact_roe", 1.0d);
        }

        if (steamModIds.Any(id => id.Equals(AotrWorkshopId, StringComparison.OrdinalIgnoreCase)))
        {
            return new ProfileRecommendation("aotr_1397421866_swfoc", "steammod_exact_aotr", 1.0d);
        }

        if (!string.IsNullOrWhiteSpace(modPathNormalized))
        {
            if (MatchesProfileHints("roe_3447786229_swfoc", modPathNormalized, profiles) ||
                LooksLikeRoePath(modPathNormalized))
            {
                return new ProfileRecommendation("roe_3447786229_swfoc", "modpath_hint_roe", 0.95d);
            }

            if (MatchesProfileHints("aotr_1397421866_swfoc", modPathNormalized, profiles) ||
                LooksLikeAotrPath(modPathNormalized))
            {
                return new ProfileRecommendation("aotr_1397421866_swfoc", "modpath_hint_aotr", 0.95d);
            }
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

    private static bool MatchesProfileHints(string profileId, string modPathNormalized, IReadOnlyList<TrainerProfile> profiles)
    {
        var profile = profiles.FirstOrDefault(x => x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return false;
        }

        foreach (var hint in BuildHints(profile))
        {
            if (modPathNormalized.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
                         .Select(NormalizeToken))
            {
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    hints.Add(normalized);
                }
            }
        }

        return hints.Where(x => !string.IsNullOrWhiteSpace(x));
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
}
