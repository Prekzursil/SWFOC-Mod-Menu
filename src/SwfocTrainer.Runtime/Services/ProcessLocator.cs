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
        ILaunchContextResolver? launchContextResolver,
        IProfileRepository? profileRepository)
    {
        _launchContextResolver = launchContextResolver ?? new LaunchContextResolver();
        _profileRepository = profileRepository;
    }

    public ProcessLocator()
        : this(null, null)
    {
    }

    public ProcessLocator(ILaunchContextResolver launchContextResolver)
        : this(launchContextResolver, null)
    {
    }

    public ProcessLocator(IProfileRepository profileRepository)
        : this(null, profileRepository)
    {
    }

    public async Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken)
    {
        var list = new List<ProcessMetadata>();
        var wmiByPid = GetWmiProcessInfoByPid();
        var profiles = await LoadProfilesForLaunchContextAsync(cancellationToken);

        foreach (var process in Process.GetProcesses())
        {
            var metadata = TryBuildProcessMetadata(process, wmiByPid, profiles);
            if (metadata is null)
            {
                continue;
            }

            list.Add(metadata);
        }

        return list;
    }

    public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync()
    {
        return FindSupportedProcessesAsync(CancellationToken.None);
    }

    public async Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken)
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

    public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target)
    {
        return FindBestMatchAsync(target, CancellationToken.None);
    }

    private static ProcessDetection GetProcessDetection(string processName, string? processPath, string? commandLine)
    {
        if (TryDetectDirectTarget(processName, processPath, commandLine, out var directDetection))
        {
            return directDetection;
        }

        var starWarsGDetection = TryDetectStarWarsG(processName, processPath, commandLine);
        if (starWarsGDetection is not null)
        {
            return starWarsGDetection;
        }

        // Heuristic for modded FoC launches where the process name/path can be atypical
        // but command line still carries mod launch markers.
        if (ContainsToken(commandLine, "steammod=") || ContainsToken(commandLine, "modpath="))
        {
            return new ProcessDetection(ExeTarget.Swfoc, IsStarWarsG: false, DetectedVia: "cmdline_mod_markers");
        }

        return new ProcessDetection(ExeTarget.Unknown, IsStarWarsG: false, DetectedVia: "unknown");
    }

    private ProcessMetadata? TryBuildProcessMetadata(
        Process process,
        IReadOnlyDictionary<int, WmiProcessInfo> wmiByPid,
        IReadOnlyList<TrainerProfile> profiles)
    {
        var probe = CaptureProcessProbe(process, wmiByPid);
        var detection = GetProcessDetection(process.ProcessName, probe.Path, probe.CommandLine);
        if (detection.ExeTarget == ExeTarget.Unknown)
        {
            return null;
        }

        var mode = InferMode(probe.CommandLine);
        var steamModIds = ExtractSteamModIds(probe.CommandLine);
        var hostRole = DetermineHostRole(detection);
        var metadata = BuildBaseMetadata(detection, probe.CommandLine, probe.MainModuleSize, hostRole, steamModIds, ExtractModPath(probe.CommandLine));
        var metadataContext = new ProcessMetadataContext(
            Process: process,
            Probe: probe,
            Detection: detection,
            Mode: mode,
            Metadata: metadata,
            LaunchContext: null,
            HostRole: hostRole,
            WorkshopMatchCount: steamModIds.Length);
        var provisional = BuildProcessMetadata(metadataContext);
        var launchContext = _launchContextResolver.Resolve(provisional, profiles);
        ApplyLaunchContextMetadata(metadata, launchContext);

        return BuildProcessMetadata(metadataContext with { LaunchContext = launchContext });
    }

    private static ProcessProbe CaptureProcessProbe(Process process, IReadOnlyDictionary<int, WmiProcessInfo> wmiByPid)
    {
        var path = string.Empty;
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

        var commandLine = TryReadCommandLine(process.Id, wmiByPid, ref path);
        return new ProcessProbe(path, commandLine, mainModuleSize);
    }

    private static string? TryReadCommandLine(
        int processId,
        IReadOnlyDictionary<int, WmiProcessInfo> wmiByPid,
        ref string path)
    {
        if (wmiByPid.TryGetValue(processId, out var wmi))
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                path = wmi.ExecutablePath ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(wmi.CommandLine))
            {
                return wmi.CommandLine;
            }
        }

        // Last-resort per-process WMI query only when bulk query didn't provide data.
#if WINDOWS
        return TryGetCommandLine(processId);
#else
        return null;
#endif
    }

    private static Dictionary<string, string> BuildBaseMetadata(
        ProcessDetection detection,
        string? commandLine,
        int mainModuleSize,
        ProcessHostRole hostRole,
        IReadOnlyCollection<string> steamModIds,
        string? modPathRaw)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["targetHint"] = detection.ExeTarget.ToString(),
            ["hasModPath"] = (!string.IsNullOrWhiteSpace(modPathRaw)).ToString(),
            ["hasSteamMod"] = (steamModIds.Count > 0).ToString(),
            ["detectedVia"] = detection.DetectedVia,
            ["commandLineAvailable"] = (!string.IsNullOrWhiteSpace(commandLine)).ToString(),
            ["isStarWarsG"] = detection.IsStarWarsG.ToString(),
            ["steamModIdsDetected"] = steamModIds.Count == 0 ? string.Empty : string.Join(",", steamModIds),
            ["hostRole"] = hostRole.ToString().ToLowerInvariant(),
            ["mainModuleSize"] = mainModuleSize.ToString(),
            ["workshopMatchCount"] = steamModIds.Count.ToString(),
            ["selectionScore"] = "0.00"
        };
    }

    private static ProcessMetadata BuildProcessMetadata(ProcessMetadataContext context)
    {
        return new ProcessMetadata(
            context.Process.Id,
            context.Process.ProcessName,
            context.Probe.Path,
            context.Probe.CommandLine,
            context.Detection.ExeTarget,
            context.Mode,
            context.Metadata,
            context.LaunchContext,
            context.HostRole,
            context.Probe.MainModuleSize,
            context.WorkshopMatchCount,
            0d);
    }

    private static void ApplyLaunchContextMetadata(IDictionary<string, string> metadata, LaunchContext launchContext)
    {
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
    }

    private static bool TryDetectDirectTarget(
        string processName,
        string? processPath,
        string? commandLine,
        out ProcessDetection detection)
    {
        if (IsProcessName(processName, "sweaw") || ContainsToken(processPath, "sweaw.exe") || ContainsToken(commandLine, "sweaw.exe"))
        {
            detection = new ProcessDetection(ExeTarget.Sweaw, IsStarWarsG: false, DetectedVia: "name_or_path_sweaw");
            return true;
        }

        if (IsProcessName(processName, "swfoc") || ContainsToken(processPath, "swfoc.exe") || ContainsToken(commandLine, "swfoc.exe"))
        {
            detection = new ProcessDetection(ExeTarget.Swfoc, IsStarWarsG: false, DetectedVia: "name_or_path_swfoc");
            return true;
        }

        detection = new ProcessDetection(ExeTarget.Unknown, IsStarWarsG: false, DetectedVia: "unknown");
        return false;
    }

    private static ProcessDetection? TryDetectStarWarsG(string processName, string? processPath, string? commandLine)
    {
        if (!IsStarWarsGProcess(processName, processPath, commandLine))
        {
            return null;
        }

        var commandLineDetection = TryDetectStarWarsGFromCommandLine(commandLine);
        if (commandLineDetection is not null)
        {
            return commandLineDetection;
        }

        if (ContainsToken(processPath, "\\corruption\\") || ContainsToken(processPath, "/corruption/"))
        {
            return new ProcessDetection(ExeTarget.Swfoc, IsStarWarsG: true, DetectedVia: "starwarsg_path_corruption");
        }

        if (ContainsToken(processPath, "\\gamedata\\") || ContainsToken(processPath, "/gamedata/"))
        {
            // Ambiguous for StarWarsG; keep FoC-safe fallback to avoid false negatives for
            // FoC+mod sessions where args are inaccessible.
            return new ProcessDetection(ExeTarget.Swfoc, IsStarWarsG: true, DetectedVia: "starwarsg_path_gamedata_foc_safe");
        }

        return new ProcessDetection(ExeTarget.Swfoc, IsStarWarsG: true, DetectedVia: "starwarsg_default_foc_safe");
    }

    private static bool IsStarWarsGProcess(string processName, string? processPath, string? commandLine)
    {
        return IsProcessName(processName, "starwarsg")
               || ContainsToken(processPath, "starwarsg.exe")
               || ContainsToken(commandLine, "starwarsg.exe");
    }

    private static ProcessDetection? TryDetectStarWarsGFromCommandLine(string? commandLine)
    {
        if (ContainsToken(commandLine, "sweaw.exe") &&
            !ContainsToken(commandLine, "steammod=") &&
            !ContainsToken(commandLine, "modpath="))
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

        return null;
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

#if WINDOWS
    private static string? TryGetCommandLine(int processId)
    {
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

        return null;
    }
#endif

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
        AddCapturedGroupValues(ids, Regex.Matches(commandLine, @"steammod\s*=\s*(\d+)", RegexOptions.IgnoreCase), groupIndex: 1);

        // Also infer IDs from mod paths containing workshop content folder segments.
        AddCapturedGroupValues(ids, Regex.Matches(commandLine, @"[\\/]+32470[\\/]+(\d+)", RegexOptions.IgnoreCase), groupIndex: 1);

        return ids.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private static void AddCapturedGroupValues(ISet<string> target, MatchCollection matches, int groupIndex)
    {
        target.UnionWith(matches
            .Cast<Match>()
            .Select(match => match.Groups)
            .Where(groups => groups.Count > groupIndex && !string.IsNullOrWhiteSpace(groups[groupIndex].Value))
            .Select(groups => groups[groupIndex].Value));
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
    private sealed record ProcessProbe(string Path, string? CommandLine, int MainModuleSize);
    private sealed record ProcessDetection(ExeTarget ExeTarget, bool IsStarWarsG, string DetectedVia);
    private sealed record ProcessMetadataContext(
        Process Process,
        ProcessProbe Probe,
        ProcessDetection Detection,
        RuntimeMode Mode,
        IReadOnlyDictionary<string, string> Metadata,
        LaunchContext? LaunchContext,
        ProcessHostRole HostRole,
        int WorkshopMatchCount);
}
