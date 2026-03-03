using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class WorkshopInventoryService : IWorkshopInventoryService
{
    private const string DefaultAppId = "32470";
    private const string SteamApiHost = "api.steampowered.com";
    private const string PublishedFileDetailsApiPath = "ISteamRemoteStorage/GetPublishedFileDetails/v1/";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex InstalledIdRegex = new(@"""(?<id>\d{4,})""\s*\{", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex SteamModRegex = new(@"STEAMMOD\s*=\s*(?<id>\d{4,})", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    private readonly ILogger<WorkshopInventoryService> _logger;
    private readonly HttpClient _httpClient;

    public WorkshopInventoryService(ILogger<WorkshopInventoryService> logger)
        : this(logger, CreateHttpClient())
    {
    }

    internal WorkshopInventoryService(ILogger<WorkshopInventoryService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<WorkshopInventoryGraph> DiscoverInstalledAsync(
        WorkshopInventoryRequest request,
        CancellationToken cancellationToken)
    {
        var appId = string.IsNullOrWhiteSpace(request.AppId) ? DefaultAppId : request.AppId.Trim();
        var diagnostics = new List<string>();
        var installedIds = ReadInstalledWorkshopIds(request, appId, diagnostics);

        if (installedIds.Count == 0)
        {
            diagnostics.Add("No installed workshop IDs were discovered from manifest/content roots.");
            return WorkshopInventoryGraph.Empty(appId) with { Diagnostics = diagnostics };
        }

        var items = BuildSkeletonItems(installedIds);

        if (request.FetchRemoteMetadata)
        {
            await EnrichWithPublishedFileDetailsAsync(request, items, diagnostics, cancellationToken);
        }

        var finalizedItems = items.Values
            .OrderBy(x => x.WorkshopId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var chains = ResolveChains(finalizedItems);
        diagnostics.Add($"resolved_chains={chains.Count}");

        return new WorkshopInventoryGraph(
            AppId: appId,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Items: finalizedItems,
            Diagnostics: diagnostics,
            Chains: chains);
    }

    private static Dictionary<string, WorkshopInventoryItem> BuildSkeletonItems(IEnumerable<string> installedIds)
    {
        return installedIds
            .Select(id => new WorkshopInventoryItem(
                WorkshopId: id,
                Title: $"Workshop Item {id}",
                ItemType: WorkshopItemType.Unknown,
                ParentWorkshopIds: Array.Empty<string>(),
                Tags: Array.Empty<string>(),
                ClassificationReason: "metadata_missing"))
            .ToDictionary(x => x.WorkshopId, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ReadInstalledWorkshopIds(
        WorkshopInventoryRequest request,
        string appId,
        ICollection<string> diagnostics)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifestPath = ResolveExistingPath(BuildManifestCandidates(request, appId));
        AddManifestIds(manifestPath, ids, diagnostics);
        AddWorkshopRootIds(request, appId, ids);
        return ids;
    }

    private static string? ResolveExistingPath(IEnumerable<string> candidates)
    {
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void AddManifestIds(string? manifestPath, ISet<string> ids, ICollection<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            diagnostics.Add("manifest_missing");
            return;
        }

        diagnostics.Add($"manifest={manifestPath}");
        var text = File.ReadAllText(manifestPath);
        foreach (Match match in InstalledIdRegex.Matches(text))
        {
            var id = match.Groups["id"].Value;
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }
    }

    private static void AddWorkshopRootIds(WorkshopInventoryRequest request, string appId, ISet<string> ids)
    {
        foreach (var root in BuildWorkshopContentCandidates(request, appId))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var id = Path.GetFileName(directory);
                if (!string.IsNullOrWhiteSpace(id) && id.All(char.IsDigit))
                {
                    ids.Add(id);
                }
            }
        }
    }

    private static IEnumerable<string> BuildManifestCandidates(WorkshopInventoryRequest request, string appId)
    {
        if (!string.IsNullOrWhiteSpace(request.ManifestPath))
        {
            yield return request.ManifestPath.Trim();
            yield break;
        }

        var envManifest = Environment.GetEnvironmentVariable("SWFOC_WORKSHOP_MANIFEST_PATH");
        if (!string.IsNullOrWhiteSpace(envManifest))
        {
            yield return envManifest.Trim();
            yield break;
        }

        foreach (var steamRoot in EnumerateDefaultSteamRoots())
        {
            yield return Path.Combine(steamRoot, "steamapps", "workshop", $"appworkshop_{appId}.acf");
        }
    }

    private static IEnumerable<string> BuildWorkshopContentCandidates(WorkshopInventoryRequest request, string appId)
    {
        if (!string.IsNullOrWhiteSpace(request.WorkshopContentRootPath))
        {
            yield return request.WorkshopContentRootPath.Trim();
            yield break;
        }

        var envRoot = Environment.GetEnvironmentVariable("SWFOC_WORKSHOP_CONTENT_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
        {
            yield return envRoot.Trim();
            yield break;
        }

        foreach (var steamRoot in EnumerateDefaultSteamRoots())
        {
            yield return Path.Combine(steamRoot, "steamapps", "workshop", "content", appId);
        }
    }

    private static IEnumerable<string> EnumerateDefaultSteamRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                seen.Add(path.Trim());
            }
        }

        Add(Environment.GetEnvironmentVariable("STEAM_INSTALL_PATH"));

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            Add(Path.Combine(programFilesX86, "Steam"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            Add(Path.Combine(programFiles, "Steam"));
        }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed && d.IsReady))
        {
            Add(Path.Combine(drive.RootDirectory.FullName, "SteamLibrary"));
        }

        return seen;
    }

    private async Task EnrichWithPublishedFileDetailsAsync(
        WorkshopInventoryRequest request,
        IDictionary<string, WorkshopInventoryItem> items,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var allIds = items.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var batchSize = Math.Clamp(request.MetadataBatchSize, 1, 100);

        for (var offset = 0; offset < allIds.Length; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = allIds.Skip(offset).Take(batchSize).ToArray();
            if (batch.Length == 0)
            {
                continue;
            }

            var mappedItems = await FetchDetailsBatchAsync(batch, offset, diagnostics, cancellationToken);
            foreach (var mapped in mappedItems)
            {
                items[mapped.WorkshopId] = mapped;
            }
        }
    }

    private async Task<IReadOnlyList<WorkshopInventoryItem>> FetchDetailsBatchAsync(
        IReadOnlyList<string> workshopIds,
        int offset,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        using var message = CreateDetailsRequest(workshopIds);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"details_fetch_failed batch_start={offset} message={ex.Message}");
            _logger.LogWarning(ex, "Workshop details fetch failed for batch starting {Offset}", offset);
            return Array.Empty<WorkshopInventoryItem>();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                diagnostics.Add($"details_fetch_http_{(int)response.StatusCode} batch_start={offset}");
                return Array.Empty<WorkshopInventoryItem>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            try
            {
                using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                return ExtractMappedItems(payload, offset, diagnostics);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"details_parse_failed batch_start={offset} message={ex.Message}");
                return Array.Empty<WorkshopInventoryItem>();
            }
        }
    }

    private static HttpRequestMessage CreateDetailsRequest(IReadOnlyList<string> workshopIds)
    {
        var endpointUri = new UriBuilder(Uri.UriSchemeHttps, SteamApiHost)
        {
            Path = PublishedFileDetailsApiPath
        }.Uri;
        var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
        {
            Content = new FormUrlEncodedContent(BuildPostForm(workshopIds))
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static IReadOnlyList<WorkshopInventoryItem> ExtractMappedItems(
        JsonDocument payload,
        int offset,
        ICollection<string> diagnostics)
    {
        if (!TryGetDetailsArray(payload, out var detailsNode))
        {
            diagnostics.Add($"details_missing_payload batch_start={offset}");
            return Array.Empty<WorkshopInventoryItem>();
        }

        var mappedItems = new List<WorkshopInventoryItem>();
        foreach (var detail in detailsNode.EnumerateArray())
        {
            if (TryMapItem(detail, out var mapped))
            {
                mappedItems.Add(mapped);
            }
        }

        return mappedItems;
    }

    private static bool TryGetDetailsArray(JsonDocument payload, out JsonElement detailsNode)
    {
        detailsNode = default;
        if (!payload.RootElement.TryGetProperty("response", out var responseNode))
        {
            return false;
        }

        if (!responseNode.TryGetProperty("publishedfiledetails", out detailsNode))
        {
            return false;
        }

        return detailsNode.ValueKind == JsonValueKind.Array;
    }

    private static Dictionary<string, string> BuildPostForm(IReadOnlyList<string> workshopIds)
    {
        var form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["itemcount"] = workshopIds.Count.ToString()
        };

        for (var index = 0; index < workshopIds.Count; index++)
        {
            form[$"publishedfileids[{index}]"] = workshopIds[index];
        }

        return form;
    }

    private static bool TryMapItem(JsonElement detail, out WorkshopInventoryItem item)
    {
        item = default!;
        if (!TryGetWorkshopId(detail, out var workshopId))
        {
            return false;
        }

        var title = ResolveTitle(detail, workshopId);
        var description = ResolveDescription(detail);
        var tags = ParseTags(detail);
        var parentIds = ParseParentDependencies(detail, description, workshopId);
        var (itemType, reason) = ClassifyWorkshopItem(title, description, tags, parentIds);

        item = new WorkshopInventoryItem(
            WorkshopId: workshopId,
            Title: title,
            ItemType: itemType,
            ParentWorkshopIds: parentIds,
            Tags: tags,
            Description: description,
            ClassificationReason: reason,
            Metadata: BuildItemMetadata(detail));
        return true;
    }

    private static bool TryGetWorkshopId(JsonElement detail, out string workshopId)
    {
        workshopId = string.Empty;
        if (!detail.TryGetProperty("publishedfileid", out var idNode))
        {
            return false;
        }

        var value = idNode.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        workshopId = value;
        return true;
    }

    private static string ResolveTitle(JsonElement detail, string workshopId)
    {
        return detail.TryGetProperty("title", out var titleNode)
            ? titleNode.GetString() ?? $"Workshop Item {workshopId}"
            : $"Workshop Item {workshopId}";
    }

    private static string ResolveDescription(JsonElement detail)
    {
        if (detail.TryGetProperty("file_description", out var descNode))
        {
            return descNode.GetString() ?? string.Empty;
        }

        if (detail.TryGetProperty("description", out var descriptionNode))
        {
            return descriptionNode.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static Dictionary<string, string> BuildItemMetadata(JsonElement detail)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["timeUpdated"] = detail.TryGetProperty("time_updated", out var updatedNode)
                ? updatedNode.ToString()
                : string.Empty,
            ["subscriptions"] = detail.TryGetProperty("subscriptions", out var subscriptionsNode)
                ? subscriptionsNode.ToString()
                : string.Empty
        };
    }

    private static IReadOnlyList<string> ParseTags(JsonElement detail)
    {
        if (!detail.TryGetProperty("tags", out var tagsNode) || tagsNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tagsNode.EnumerateArray())
        {
            var value = tag.TryGetProperty("tag", out var tagName)
                ? tagName.GetString()
                : tag.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                tags.Add(value.Trim());
            }
        }

        return tags.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> ParseParentDependencies(JsonElement detail, string? description, string selfId)
    {
        var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddParentsFromChildren(detail, selfId, parents);
        AddParentsFromDescription(description, selfId, parents);
        return parents.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddParentsFromChildren(JsonElement detail, string selfId, ISet<string> parents)
    {
        if (!detail.TryGetProperty("children", out var childrenNode) || childrenNode.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in childrenNode.EnumerateArray())
        {
            if (!entry.TryGetProperty("publishedfileid", out var childIdNode))
            {
                continue;
            }

            var childId = childIdNode.GetString();
            if (!string.IsNullOrWhiteSpace(childId) && !string.Equals(childId, selfId, StringComparison.OrdinalIgnoreCase))
            {
                parents.Add(childId);
            }
        }
    }

    private static void AddParentsFromDescription(string? description, string selfId, ISet<string> parents)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        foreach (Match match in SteamModRegex.Matches(description))
        {
            var id = match.Groups["id"].Value;
            if (!string.IsNullOrWhiteSpace(id) && !string.Equals(id, selfId, StringComparison.OrdinalIgnoreCase))
            {
                parents.Add(id);
            }
        }
    }

    private static (WorkshopItemType ItemType, string Reason) ClassifyWorkshopItem(
        string title,
        string? description,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> parentIds)
    {
        if (parentIds.Count > 0)
        {
            return (WorkshopItemType.Submod, "parent_dependency");
        }

        if (tags.Any(static tag => tag.Contains("submod", StringComparison.OrdinalIgnoreCase)))
        {
            return (WorkshopItemType.Submod, "tag_submod_unknown_parent");
        }

        var normalized = $"{title}\n{description}";
        if (normalized.Contains("submod", StringComparison.OrdinalIgnoreCase))
        {
            return (WorkshopItemType.Submod, "keyword_submod_unknown_parent");
        }

        return (WorkshopItemType.Mod, "independent_mod");
    }

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private static IReadOnlyList<WorkshopInventoryChain> ResolveChains(IReadOnlyList<WorkshopInventoryItem> items)
    {
        if (items.Count == 0)
        {
            return Array.Empty<WorkshopInventoryChain>();
        }

        var map = items.ToDictionary(x => x.WorkshopId, StringComparer.OrdinalIgnoreCase);
        var chains = new List<WorkshopInventoryChain>(items.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items.OrderBy(x => x.WorkshopId, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var candidate in BuildChainCandidates(item, map))
            {
                AddChainIfUnique(candidate.OrderedIds, candidate.Reason, candidate.MissingParentIds, chains, seen);
            }
        }

        return chains;
    }

    private static IEnumerable<ChainCandidate> BuildChainCandidates(
        WorkshopInventoryItem item,
        IReadOnlyDictionary<string, WorkshopInventoryItem> map)
    {
        if (item.ParentWorkshopIds.Count == 0)
        {
            yield return CreateSingleItemChain(item.WorkshopId, item.ClassificationReason ?? "independent_mod");
            yield break;
        }

        var missingParentIds = GetMissingParentIds(item.ParentWorkshopIds, map);
        var resolvedParentIds = GetResolvedParentIds(item.ParentWorkshopIds, map);

        if (resolvedParentIds.Length == 0)
        {
            yield return new ChainCandidate(
                OrderedIds: new[] { item.WorkshopId },
                Reason: missingParentIds.Length > 0
                    ? "parent_dependency_missing"
                    : item.ClassificationReason ?? "parent_dependency",
                MissingParentIds: missingParentIds);
            yield break;
        }

        foreach (var parentId in resolvedParentIds)
        {
            yield return new ChainCandidate(
                OrderedIds: BuildParentFirstChain(parentId, item.WorkshopId, map),
                Reason: ResolveParentDependencyReason(item.ClassificationReason, missingParentIds),
                MissingParentIds: missingParentIds);
        }
    }

    private static ChainCandidate CreateSingleItemChain(string workshopId, string reason)
    {
        return new ChainCandidate(
            OrderedIds: new[] { workshopId },
            Reason: reason,
            MissingParentIds: Array.Empty<string>());
    }

    private static string[] GetMissingParentIds(
        IEnumerable<string> parentIds,
        IReadOnlyDictionary<string, WorkshopInventoryItem> map)
    {
        return parentIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && !map.ContainsKey(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] GetResolvedParentIds(
        IEnumerable<string> parentIds,
        IReadOnlyDictionary<string, WorkshopInventoryItem> map)
    {
        return parentIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && map.ContainsKey(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveParentDependencyReason(string? classificationReason, IReadOnlyCollection<string> missingParentIds)
    {
        if (missingParentIds.Count > 0)
        {
            return "parent_dependency_partial_missing";
        }

        return classificationReason ?? "parent_dependency";
    }

    private static void AddChainIfUnique(
        IReadOnlyList<string> orderedIds,
        string reason,
        IReadOnlyList<string> missingParentIds,
        ICollection<WorkshopInventoryChain> chains,
        ISet<string> seen)
    {
        if (orderedIds.Count == 0)
        {
            return;
        }

        var chainId = string.Join(">", orderedIds);
        if (!seen.Add(chainId))
        {
            return;
        }

        chains.Add(new WorkshopInventoryChain(
            ChainId: chainId,
            OrderedWorkshopIds: orderedIds,
            ClassificationReason: reason,
            ParentFirst: true,
            MissingParentIds: missingParentIds));
    }

    private static IReadOnlyList<string> BuildParentFirstChain(
        string rootParentId,
        string childId,
        IReadOnlyDictionary<string, WorkshopInventoryItem> map)
    {
        var ordered = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        BuildParentStack(rootParentId, map, visited, ordered);
        if (visited.Add(childId))
        {
            ordered.Add(childId);
        }

        return ordered;
    }

    private static void BuildParentStack(
        string currentId,
        IReadOnlyDictionary<string, WorkshopInventoryItem> map,
        ISet<string> visited,
        ICollection<string> ordered)
    {
        if (string.IsNullOrWhiteSpace(currentId) || !map.ContainsKey(currentId) || !visited.Add(currentId))
        {
            return;
        }

        var parent = map[currentId];
        foreach (var ancestor in parent.ParentWorkshopIds)
        {
            BuildParentStack(ancestor, map, visited, ordered);
        }

        ordered.Add(currentId);
    }

    private readonly record struct ChainCandidate(
        IReadOnlyList<string> OrderedIds,
        string Reason,
        IReadOnlyList<string> MissingParentIds);
}
