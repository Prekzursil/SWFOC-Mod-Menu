using System.Text.RegularExpressions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class ModDependencyValidator : IModDependencyValidator
{
    private const string WorkshopAppId = "32470";
    private const string PathTraversalToken = "..";
    private const string DependencyMetadataMarkerKey = "requiredMarkerFile";
    private const string DependencySoftFailSuffix = "Attach will continue, but dependency-sensitive actions are temporarily disabled.";
    private const string LibraryFolderPathPattern = "\"path\"\\s*\"([^\"]+)\"";
    private const string ModPathPattern = "modpath\\s*=\\s*(?:\"(?<quoted>[^\"]+)\"|(?<unquoted>[^\\s]+))";
    private static readonly IReadOnlyList<string> LinuxWorkshopCandidates =
    [
        "/mnt/c/Program Files (x86)/Steam/steamapps/workshop/content/32470",
        "/mnt/d/SteamLibrary/steamapps/workshop/content/32470",
        "/mnt/c/SteamLibrary/steamapps/workshop/content/32470"
    ];

    public DependencyValidationResult Validate(TrainerProfile profile, ProcessMetadata process)
    {
        var marker = ReadMetadata(profile, DependencyMetadataMarkerKey);
        var markerFailure = ValidateMarkerMetadata(marker);
        if (markerFailure is not null)
        {
            return markerFailure;
        }

        var requiredIds = CollectRequiredWorkshopIds(profile);
        if (requiredIds.Count == 0)
        {
            return BuildNoDependenciesDeclaredResult();
        }

        var dependencySensitiveActions = GetDependencySensitiveActions(profile);
        var workshopRoots = DiscoverWorkshopRoots();
        var issues = new List<string>();
        var unresolvedIds = ResolveWorkshopDependencies(requiredIds, workshopRoots, marker, issues);

        ResolveLocalDependencies(profile, process, marker, unresolvedIds, issues);

        return BuildDependencyValidationResult(unresolvedIds, workshopRoots, issues, dependencySensitiveActions);
    }

    private static DependencyValidationResult? ValidateMarkerMetadata(string? marker)
    {
        if (string.IsNullOrWhiteSpace(marker) || !marker.Contains(PathTraversalToken, StringComparison.Ordinal))
        {
            return null;
        }

        return new DependencyValidationResult(
            DependencyValidationStatus.HardFail,
            $"Invalid dependency marker path '{marker}' in profile metadata.",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static DependencyValidationResult BuildNoDependenciesDeclaredResult()
    {
        return new DependencyValidationResult(
            DependencyValidationStatus.Pass,
            "No workshop dependencies declared for this profile.",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static HashSet<string> ResolveWorkshopDependencies(
        IReadOnlyCollection<string> requiredIds,
        IReadOnlyList<string> workshopRoots,
        string? marker,
        ICollection<string> issues)
    {
        var unresolvedIds = new HashSet<string>(requiredIds, StringComparer.OrdinalIgnoreCase);
        foreach (var id in requiredIds)
        {
            var folder = FindWorkshopFolder(id, workshopRoots);
            if (folder is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(marker) && !HasMarker(folder, marker))
            {
                issues.Add($"{id}:missing marker '{marker}' in workshop folder");
                continue;
            }

            unresolvedIds.Remove(id);
        }

        return unresolvedIds;
    }

    private static void ResolveLocalDependencies(
        TrainerProfile profile,
        ProcessMetadata process,
        string? marker,
        ISet<string> unresolvedIds,
        ICollection<string> issues)
    {
        var localRoots = ResolveLocalDependencyRoots(profile, process);
        if (unresolvedIds.Count == 0 || localRoots.Count == 0)
        {
            return;
        }

        var availableRoots = localRoots.ToList();
        var resolvedByLocal = new List<string>();
        foreach (var unresolved in unresolvedIds.ToArray())
        {
            var root = availableRoots.FirstOrDefault(path => string.IsNullOrWhiteSpace(marker) || HasMarker(path, marker));
            if (root is null)
            {
                continue;
            }

            resolvedByLocal.Add($"{unresolved}->{root}");
            availableRoots.Remove(root);
            unresolvedIds.Remove(unresolved);
        }

        if (resolvedByLocal.Count > 0)
        {
            issues.Add($"resolved by local mod paths [{string.Join(", ", resolvedByLocal)}]");
        }
    }

    private static DependencyValidationResult BuildDependencyValidationResult(
        IReadOnlyCollection<string> unresolvedIds,
        IReadOnlyCollection<string> workshopRoots,
        IList<string> issues,
        HashSet<string> dependencySensitiveActions)
    {
        if (unresolvedIds.Count == 0)
        {
            return new DependencyValidationResult(
                DependencyValidationStatus.Pass,
                issues.Count == 0
                    ? "Workshop/local dependencies verified."
                    : $"Dependencies verified ({string.Join("; ", issues)}).",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        var unresolvedSegment = $"missing dependencies [{string.Join(", ", unresolvedIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}]";
        if (workshopRoots.Count == 0)
        {
            issues.Add("no workshop roots discovered");
        }

        issues.Insert(0, unresolvedSegment);
        var message = $"Dependency verification soft-failed: {string.Join("; ", issues)}. {DependencySoftFailSuffix}";
        return new DependencyValidationResult(
            DependencyValidationStatus.SoftFail,
            message,
            dependencySensitiveActions);
    }

    private static HashSet<string> CollectRequiredWorkshopIds(TrainerProfile profile)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(profile.SteamWorkshopId))
        {
            ids.Add(profile.SteamWorkshopId);
        }

        AddCsvMetadataValues(profile, ids, "requiredWorkshopIds");
        AddCsvMetadataValues(profile, ids, "requiredWorkshopId");
        return ids;
    }

    private static HashSet<string> GetDependencySensitiveActions(TrainerProfile profile)
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddCsvMetadataValues(profile, disabled, "dependencySensitiveActions");

        foreach (var action in profile.Actions.Values)
        {
            if (action.ExecutionKind == ExecutionKind.Helper)
            {
                disabled.Add(action.Id);
            }
        }

        return disabled;
    }

    private static IReadOnlyList<string> DiscoverWorkshopRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            var root = drive.RootDirectory.FullName;
            var steamCandidates = new[]
            {
                Path.Combine(root, "SteamLibrary"),
                Path.Combine(root, "Program Files (x86)", "Steam"),
                Path.Combine(root, "Program Files", "Steam")
            };

            foreach (var steamRoot in steamCandidates)
            {
                var workshopRoot = Path.Combine(steamRoot, "steamapps", "workshop", "content", WorkshopAppId);
                if (Directory.Exists(workshopRoot))
                {
                    roots.Add(workshopRoot);
                }

                var libraryFoldersPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
                AddWorkshopRootsFromLibraryFolders(libraryFoldersPath, roots);
            }
        }

        foreach (var linuxCandidate in LinuxWorkshopCandidates)
        {
            if (Directory.Exists(linuxCandidate))
            {
                roots.Add(linuxCandidate);
            }
        }

        return roots.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddWorkshopRootsFromLibraryFolders(string libraryFoldersPath, ISet<string> roots)
    {
        if (!File.Exists(libraryFoldersPath))
        {
            return;
        }

        string content;
        try
        {
            content = File.ReadAllText(libraryFoldersPath);
        }
        catch (Exception)
        {
            return;
        }

        foreach (Match match in Regex.Matches(content, LibraryFolderPathPattern, RegexOptions.IgnoreCase))
        {
            if (match.Groups.Count < 2)
            {
                continue;
            }

            var pathValue = match.Groups[1].Value
                .Replace("\\\\", "\\", StringComparison.Ordinal)
                .Trim();
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                continue;
            }

            var workshopRoot = Path.Combine(pathValue, "steamapps", "workshop", "content", WorkshopAppId);
            if (Directory.Exists(workshopRoot))
            {
                roots.Add(workshopRoot);
            }
        }
    }

    private static string? FindWorkshopFolder(string id, IReadOnlyList<string> workshopRoots)
    {
        foreach (var root in workshopRoots)
        {
            var candidate = Path.Combine(root, id);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool HasMarker(string root, string marker)
    {
        var safeMarker = marker.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var markerPath = Path.Combine(root, safeMarker);
        return File.Exists(markerPath);
    }

    private static IReadOnlyList<string> ResolveLocalDependencyRoots(TrainerProfile profile, ProcessMetadata process)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var launchContext = process.LaunchContext;
        var modPathRaw = launchContext?.ModPathRaw;
        if (string.IsNullOrWhiteSpace(modPathRaw))
        {
            modPathRaw = ExtractModPath(process.CommandLine);
        }

        if (string.IsNullOrWhiteSpace(modPathRaw))
        {
            return roots.ToArray();
        }

        foreach (var candidate in BuildPossibleModRoots(modPathRaw, process.ProcessPath))
        {
            if (Directory.Exists(candidate))
            {
                roots.Add(candidate);
            }
        }

        var parentHints = ParseCsvMetadata(profile, "localParentPathHints");
        if (parentHints.Count > 0 && roots.Count > 0)
        {
            foreach (var root in roots.ToArray())
            {
                var parent = Directory.GetParent(root)?.FullName;
                if (string.IsNullOrWhiteSpace(parent))
                {
                    continue;
                }

                foreach (var hint in parentHints)
                {
                    if (hint.Contains(PathTraversalToken, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var hintedPath = Path.Combine(parent, hint);
                    if (Directory.Exists(hintedPath))
                    {
                        roots.Add(hintedPath);
                    }
                }
            }
        }

        return roots.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> BuildPossibleModRoots(string modPathRaw, string processPath)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = modPathRaw.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return candidates.ToArray();
        }

        if (Path.IsPathRooted(normalized))
        {
            candidates.Add(Path.GetFullPath(normalized));
            return candidates.ToArray();
        }

        var processDir = string.IsNullOrWhiteSpace(processPath) ? null : Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(processDir))
        {
            return candidates.ToArray();
        }

        candidates.Add(Path.GetFullPath(Path.Combine(processDir, normalized)));

        var oneUp = Directory.GetParent(processDir)?.FullName;
        if (!string.IsNullOrWhiteSpace(oneUp))
        {
            candidates.Add(Path.GetFullPath(Path.Combine(oneUp, normalized)));
            candidates.Add(Path.GetFullPath(Path.Combine(oneUp, "corruption", normalized)));
        }

        return candidates.ToArray();
    }

    private static string? ExtractModPath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var match = Regex.Match(commandLine, ModPathPattern, RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["quoted"].Success
            ? match.Groups["quoted"].Value
            : match.Groups["unquoted"].Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
    }

    private static void AddCsvMetadataValues(TrainerProfile profile, ISet<string> target, string key)
    {
        if (profile.Metadata is null ||
            !profile.Metadata.TryGetValue(key, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        foreach (var value in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            target.Add(value);
        }
    }

    private static IReadOnlyList<string> ParseCsvMetadata(TrainerProfile profile, string key)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddCsvMetadataValues(profile, values, key);
        return values.ToArray();
    }

    private static string? ReadMetadata(TrainerProfile profile, string key)
    {
        if (profile.Metadata is null)
        {
            return null;
        }

        return profile.Metadata.TryGetValue(key, out var value) ? value : null;
    }
}
