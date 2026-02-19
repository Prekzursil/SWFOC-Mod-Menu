using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
#if WINDOWS
using System.Management;
#endif

namespace SwfocTrainer.Runtime.Services;

public sealed class ProcessLocator : IProcessLocator
{
    private readonly ILaunchContextResolver _launchContextResolver;
    private readonly IProfileRepository? _profileRepository;

    private IReadOnlyList<TrainerProfile>? _cachedProfiles;
    private DateTimeOffset _cachedProfilesLoadedAtUtc = DateTimeOffset.MinValue;
    private static readonly TimeSpan ProfileCacheTtl = TimeSpan.FromSeconds(5);

    public ProcessLocator(
        ILaunchContextResolver? launchContextResolver = null,
        IProfileRepository? profileRepository = null)
    {
        _launchContextResolver = launchContextResolver ?? new LaunchContextResolver();
        _profileRepository = profileRepository;
    }

    public async Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<ProcessMetadata>();
        var wmiByPid = GetWmiProcessInfoByPid();
        var profiles = await LoadProfilesForLaunchContextAsync(cancellationToken);

        foreach (var process in Process.GetProcesses())
        {
            string path;
            var mainModuleSize = 0;
            try
            {
                var mainModule = process.MainModule;
                path = mainModule?.FileName ?? string.Empty;
                mainModuleSize = mainModule?.ModuleMemorySize ?? 0;
            }
            catch
            {
                path = string.Empty;
            }

            string? commandLine = null;
            if (wmiByPid.TryGetValue(process.Id, out var wmi))
            {
                commandLine = wmi.CommandLine;
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = wmi.ExecutablePath ?? string.Empty;
                }
            }

            // Last-resort per-process WMI query only when bulk query didn't provide data.
            commandLine ??= TryGetCommandLine(process.Id);

            var detection = GetProcessDetection(process.ProcessName, path, commandLine);
            if (detection.ExeTarget == ExeTarget.Unknown)
            {
                continue;
            }

            var mode = InferMode(commandLine);
            var steamModIds = ExtractSteamModIds(commandLine);
            var modPathRaw = ExtractModPath(commandLine);
            var hostRole = DetermineHostRole(detection);
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["targetHint"] = detection.ExeTarget.ToString(),
                ["hasModPath"] = (!string.IsNullOrWhiteSpace(modPathRaw)).ToString(),
                ["hasSteamMod"] = (steamModIds.Length > 0).ToString(),
                ["detectedVia"] = detection.DetectedVia,
                ["commandLineAvailable"] = (!string.IsNullOrWhiteSpace(commandLine)).ToString(),
                ["isStarWarsG"] = detection.IsStarWarsG.ToString(),
                ["steamModIdsDetected"] = steamModIds.Length == 0 ? string.Empty : string.Join(",", steamModIds),
                ["hostRole"] = hostRole.ToString().ToLowerInvariant(),
                ["mainModuleSize"] = mainModuleSize.ToString(),
                ["workshopMatchCount"] = steamModIds.Length.ToString(),
                ["selectionScore"] = "0.00"
            };

            var provisional = new ProcessMetadata(
                process.Id,
                process.ProcessName,
                path,
                commandLine,
                detection.ExeTarget,
                mode,
                metadata,
                HostRole: hostRole,
                MainModuleSize: mainModuleSize,
                WorkshopMatchCount: steamModIds.Length,
                SelectionScore: 0d);
            var launchContext = _launchContextResolver.Resolve(provisional, profiles);
            metadata["launchKind"] = launchContext.LaunchKind.ToString();
            metadata["modPathRaw"] = launchContext.ModPathRaw ?? string.Empty;
            metadata["modPathNormalized"] = launchContext.ModPathNormalized ?? string.Empty;
            metadata["profileRecommendation"] = launchContext.Recommendation.ProfileId ?? string.Empty;
            metadata["recommendationReason"] = launchContext.Recommendation.ReasonCode;
            metadata["recommendationConfidence"] = launchContext.Recommendation.Confidence.ToString("0.00");
            metadata["hasModPath"] = (!string.IsNullOrWhiteSpace(launchContext.ModPathNormalized)).ToString();
            metadata["hasSteamMod"] = (launchContext.SteamModIds.Count > 0).ToString();
            metadata["steamModIdsDetected"] = launchContext.SteamModIds.Count == 0
                ? string.Empty
                : string.Join(",", launchContext.SteamModIds);

            list.Add(new ProcessMetadata(
                process.Id,
                process.ProcessName,
                path,
                commandLine,
                detection.ExeTarget,
                mode,
                metadata,
                launchContext,
                hostRole,
                mainModuleSize,
                steamModIds.Length,
                0d));
        }

        return list;
    }

    public async Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken = default)
    {
        var all = await FindSupportedProcessesAsync(cancellationToken);
        var direct = all.FirstOrDefault(x => x.ExeTarget == target);
        if (direct is not null)
        {
            return direct;
        }

        // FoC/EaW launches can both show as StarWarsG.exe with ambiguous target hints.
        if (target is ExeTarget.Swfoc or ExeTarget.Sweaw)
        {
            return all.FirstOrDefault(x =>
                x.Metadata is not null &&
                x.Metadata.TryGetValue("isStarWarsG", out var raw) &&
                bool.TryParse(raw, out var isStarWarsG) &&
                isStarWarsG);
        }

        return null;
    }

    private static ProcessDetection GetProcessDetection(string processName, string? processPath, string? commandLine)
    {
        if (IsProcessName(processName, "sweaw") || ContainsToken(processPath, "sweaw.exe") || ContainsToken(commandLine, "sweaw.exe"))
        {
            return new ProcessDetection(ExeTarget.Sweaw, IsStarWarsG: false, DetectedVia: "name_or_path_sweaw");
        }

        if (IsProcessName(processName, "swfoc") || ContainsToken(processPath, "swfoc.exe") || ContainsToken(commandLine, "swfoc.exe"))
        {
            return new ProcessDetection(ExeTarget.Swfoc, IsStarWarsG: false, DetectedVia: "name_or_path_swfoc");
        }

        // Steam x64 "Gold Pack" typically launches StarWarsG.exe instead of sweaw/swfoc exes.
        // Infer family from folder/launch args. Prefer FoC-safe defaults because most trainer use
        // in this project targets FoC/modded FoC and command line is sometimes unavailable.
        // - corruption folder or mod args => FoC runtime
        // - ambiguous/no args => FoC-safe fallback
        if (IsProcessName(processName, "starwarsg") || ContainsToken(processPath, "starwarsg.exe") || ContainsToken(commandLine, "starwarsg.exe"))
        {
            if (ContainsToken(commandLine, "sweaw.exe") && !ContainsToken(commandLine, "steammod=") && !ContainsToken(commandLine, "modpath="))
            {
                return new ProcessDetection(ExeTarget.Sweaw, IsStarWarsG: true, DetectedVia: "starwarsg_cmdline_sweaw_hint");
            }

            if (ContainsToken(commandLine, "swfoc.exe") ||
                ContainsToken(commandLine, "steammod=") ||
                ContainsToken(commandLine, "modpath=") ||
                ContainsToken(commandLine, "corruption"))
            {
                return new ProcessDetection(ExeTarget.Swfoc, IsStarWarsG: true, DetectedVia: "starwarsg_cmdline_foc_hint");
            }

            if (ContainsToken(processPath, @"\corruption\") || ContainsToken(processPath, "/corruption/"))
            {
                return new ProcessDetection(ExeTarget.Swfoc, IsStarWarsG: true, DetectedVia: "starwarsg_path_corruption");
            }

            if (ContainsToken(processPath, @"\gamedata\") || ContainsToken(processPath, "/gamedata/"))
            {
                // Ambiguous for StarWarsG; keep FoC-safe fallback to avoid false negatives for
                // FoC+mod sessions where args are inaccessible.
                return new ProcessDetection(ExeTarget.Swfoc, IsStarWarsG: true, DetectedVia: "starwarsg_path_gamedata_foc_safe");
            }

            return new ProcessDetection(ExeTarget.Swfoc, IsStarWarsG: true, DetectedVia: "starwarsg_default_foc_safe");
        }

        // Heuristic for modded FoC launches where the process name/path can be atypical
        // but command line still carries mod launch markers.
        if (ContainsToken(commandLine, "steammod=") || ContainsToken(commandLine, "modpath="))
        {
            return new ProcessDetection(ExeTarget.Swfoc, IsStarWarsG: false, DetectedVia: "cmdline_mod_markers");
        }

        return new ProcessDetection(ExeTarget.Unknown, IsStarWarsG: false, DetectedVia: "unknown");
    }

    private static RuntimeMode InferMode(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return RuntimeMode.Unknown;
        }

        if (commandLine.Contains("skirmish", StringComparison.OrdinalIgnoreCase) ||
            commandLine.Contains("tactical", StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeMode.Tactical;
        }

        if (commandLine.Contains("campaign", StringComparison.OrdinalIgnoreCase) ||
            commandLine.Contains("galactic", StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeMode.Galactic;
        }

        return RuntimeMode.Unknown;
    }

    private static string? TryGetCommandLine(int processId)
    {
#if WINDOWS
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
            foreach (ManagementObject obj in searcher.Get())
            {
                return obj["CommandLine"]?.ToString();
            }
        }
        catch
        {
            // ignored, command line can be unavailable if permissions are insufficient.
        }
#endif
        return null;
    }

    private static bool ContainsToken(string? value, string token)
    {
        return value?.Contains(token, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsProcessName(string? processName, string expectedWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
        return normalized.Equals(expectedWithoutExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ExtractSteamModIds(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(commandLine, @"steammod\s*=\s*(\d+)", RegexOptions.IgnoreCase))
        {
            if (match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                ids.Add(match.Groups[1].Value);
            }
        }

        // Also infer IDs from mod paths containing workshop content folder segments.
        foreach (Match match in Regex.Matches(commandLine, @"[\\/]+32470[\\/]+(\d+)", RegexOptions.IgnoreCase))
        {
            if (match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                ids.Add(match.Groups[1].Value);
            }
        }

        return ids.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private static ProcessHostRole DetermineHostRole(ProcessDetection detection)
    {
        if (detection.IsStarWarsG)
        {
            return ProcessHostRole.GameHost;
        }

        return detection.ExeTarget is ExeTarget.Sweaw or ExeTarget.Swfoc
            ? ProcessHostRole.Launcher
            : ProcessHostRole.Unknown;
    }

    private static string? ExtractModPath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var match = Regex.Match(
            commandLine,
            @"modpath\s*=\s*(?:""(?<quoted>[^""]+)""|(?<unquoted>[^\s]+))",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["quoted"].Success
            ? match.Groups["quoted"].Value
            : match.Groups["unquoted"].Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
    }

    private async Task<IReadOnlyList<TrainerProfile>> LoadProfilesForLaunchContextAsync(CancellationToken cancellationToken)
    {
        if (_profileRepository is null)
        {
            return Array.Empty<TrainerProfile>();
        }

        var now = DateTimeOffset.UtcNow;
        if (_cachedProfiles is not null && (now - _cachedProfilesLoadedAtUtc) < ProfileCacheTtl)
        {
            return _cachedProfiles;
        }

        try
        {
            var ids = await _profileRepository.ListAvailableProfilesAsync(cancellationToken);
            var profiles = new List<TrainerProfile>(ids.Count);
            foreach (var id in ids)
            {
                profiles.Add(await _profileRepository.ResolveInheritedProfileAsync(id, cancellationToken));
            }

            _cachedProfiles = profiles;
            _cachedProfilesLoadedAtUtc = now;
            return profiles;
        }
        catch
        {
            return Array.Empty<TrainerProfile>();
        }
    }

    private static IReadOnlyDictionary<int, WmiProcessInfo> GetWmiProcessInfoByPid()
    {
#if WINDOWS
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine, ExecutablePath FROM Win32_Process");
            var map = new Dictionary<int, WmiProcessInfo>();
            foreach (ManagementObject obj in searcher.Get())
            {
                var pid = TryGetProcessId(obj);
                if (pid is null)
                {
                    continue;
                }

                map[pid.Value] = new WmiProcessInfo(
                    obj["CommandLine"]?.ToString(),
                    obj["ExecutablePath"]?.ToString());
            }

            return map;
        }
        catch
        {
            // WMI can fail in hardened environments. Caller will fall back gracefully.
            return new Dictionary<int, WmiProcessInfo>();
        }
#else
        return new Dictionary<int, WmiProcessInfo>();
#endif
    }

#if WINDOWS
    private static int? TryGetProcessId(ManagementObject obj)
    {
        try
        {
            var raw = obj["ProcessId"];
            return raw switch
            {
                uint u => (int)u,
                int i => i,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
#endif

    private sealed record WmiProcessInfo(string? CommandLine, string? ExecutablePath);
    private sealed record ProcessDetection(ExeTarget ExeTarget, bool IsStarWarsG, string DetectedVia);
}
