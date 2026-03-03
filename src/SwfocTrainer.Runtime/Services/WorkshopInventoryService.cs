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
    private const string DetailsApiUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex InstalledIdRegex = new(@"""(?<id>\d{4,})""\s*\{", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex SteamModRegex = new(@"STEAMMOD\s*=\s*(?<id>\d{4,})", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    private static readonly string[] DefaultManifestPaths =
    [
        @"D:\SteamLibrary\steamapps\workshop\appworkshop_32470.acf",
        @"C:\Program Files (x86)\Steam\steamapps\workshop\appworkshop_32470.acf"
    ];

    private static readonly string[] DefaultWorkshopRoots =
    [
        @"D:\SteamLibrary\steamapps\workshop\content\32470",
        @"C:\Program Files (x86)\Steam\steamapps\workshop\content\32470"
    ];

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
        var appId = string.IsNullOrWhiteSpace(request.AppId) ? "32470" : request.AppId.Trim();
        var diagnostics = new List<string>();
        var installedIds = ReadInstalledWorkshopIds(request, appId, diagnostics);

        if (installedIds.Count == 0)
        {
            diagnostics.Add("No installed workshop IDs were discovered from manifest/content roots.");
            return WorkshopInventoryGraph.Empty(appId) with { Diagnostics = diagnostics };
        }

        var items = installedIds
            .Select(id => new WorkshopInventoryItem(
                WorkshopId: id,
                Title: $"Workshop Item {id}",
                ItemType: WorkshopItemType.Unknown,
                ParentWorkshopIds: Array.Empty<string>(),
                Tags: Array.Empty<string>(),
                ClassificationReason: "metadata_missing"))
            .ToDictionary(x => x.WorkshopId, StringComparer.OrdinalIgnoreCase);

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

    private static HashSet<string> ReadInstalledWorkshopIds(
        WorkshopInventoryRequest request,
        string appId,
        ICollection<string> diagnostics)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifestCandidates = BuildManifestCandidates(request, appId);
        string? resolvedManifestPath = null;

        foreach (var candidate in manifestCandidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            resolvedManifestPath = candidate;
            var text = File.ReadAllText(candidate);
            foreach (Match match in InstalledIdRegex.Matches(text))
            {
                var id = match.Groups["id"].Value;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }

            break;
        }

        if (resolvedManifestPath is not null)
        {
            diagnostics.Add($"manifest={resolvedManifestPath}");
        }
        else
        {
            diagnostics.Add("manifest_missing");
        }

        // Fall back to scanning workshop content roots when manifest is unavailable/incomplete.
        foreach (var root in BuildWorkshopContentCandidates(request))
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

        return ids;
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

        foreach (var defaultPath in DefaultManifestPaths)
        {
            if (defaultPath.Contains("32470", StringComparison.Ordinal))
            {
                yield return defaultPath.Replace("32470", appId, StringComparison.Ordinal);
            }
            else
            {
                yield return defaultPath;
            }
        }
    }

    private static IEnumerable<string> BuildWorkshopContentCandidates(WorkshopInventoryRequest request)
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

        foreach (var root in DefaultWorkshopRoots)
        {
            yield return root;
        }
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
            var slice = allIds.Skip(offset).Take(batchSize).ToArray();
            if (slice.Length == 0)
            {
                continue;
            }

            using var body = new FormUrlEncodedContent(BuildPostForm(slice));
            using var message = new HttpRequestMessage(HttpMethod.Post, DetailsApiUrl)
            {
                Content = body
            };
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"details_fetch_failed batch_start={offset} message={ex.Message}");
                _logger.LogWarning(ex, "Workshop details fetch failed for batch starting {Offset}", offset);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                diagnostics.Add($"details_fetch_http_{(int)response.StatusCode} batch_start={offset}");
                continue;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            JsonDocument payload;
            try
            {
                payload = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"details_parse_failed batch_start={offset} message={ex.Message}");
                continue;
            }

            using (payload)
            {
                if (!payload.RootElement.TryGetProperty("response", out var responseNode) ||
                    !responseNode.TryGetProperty("publishedfiledetails", out var detailsNode) ||
                    detailsNode.ValueKind != JsonValueKind.Array)
                {
                    diagnostics.Add($"details_missing_payload batch_start={offset}");
                    continue;
                }

                foreach (var detail in detailsNode.EnumerateArray())
                {
                    if (!TryMapItem(detail, out var mapped))
                    {
                        continue;
                    }

                    items[mapped.WorkshopId] = mapped;
                }
            }
        }
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
        if (!detail.TryGetProperty("publishedfileid", out var idNode))
        {
            return false;
        }

        var workshopId = idNode.GetString();
        if (string.IsNullOrWhiteSpace(workshopId))
        {
            return false;
        }

        var title = detail.TryGetProperty("title", out var titleNode)
            ? titleNode.GetString() ?? $"Workshop Item {workshopId}"
            : $"Workshop Item {workshopId}";
        var description = detail.TryGetProperty("file_description", out var descNode)
            ? descNode.GetString()
            : detail.TryGetProperty("description", out var descriptionNode)
                ? descriptionNode.GetString()
                : string.Empty;

        var tags = ParseTags(detail);
        var parentIds = ParseParentDependencies(detail, description, workshopId);
        var (itemType, reason) = ClassifyWorkshopItem(title, description, tags, parentIds);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["timeUpdated"] = detail.TryGetProperty("time_updated", out var updatedNode)
                ? updatedNode.ToString()
                : string.Empty,
            ["subscriptions"] = detail.TryGetProperty("subscriptions", out var subscriptionsNode)
                ? subscriptionsNode.ToString()
                : string.Empty
        };

        item = new WorkshopInventoryItem(
            WorkshopId: workshopId,
            Title: title,
            ItemType: itemType,
            ParentWorkshopIds: parentIds,
            Tags: tags,
            Description: description,
            ClassificationReason: reason,
            Metadata: metadata);
        return true;
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

        if (detail.TryGetProperty("children", out var childrenNode) && childrenNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in childrenNode.EnumerateArray())
            {
                if (entry.TryGetProperty("publishedfileid", out var childIdNode))
                {
                    var childId = childIdNode.GetString();
                    if (!string.IsNullOrWhiteSpace(childId) && !string.Equals(childId, selfId, StringComparison.OrdinalIgnoreCase))
                    {
                        parents.Add(childId);
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            foreach (Match match in SteamModRegex.Matches(description))
            {
                var id = match.Groups["id"].Value;
                if (!string.IsNullOrWhiteSpace(id) && !string.Equals(id, selfId, StringComparison.OrdinalIgnoreCase))
                {
                    parents.Add(id);
                }
            }
        }

        return parents.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
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
            if (item.ParentWorkshopIds.Count == 0)
            {
                AddChain(
                    orderedIds: new[] { item.WorkshopId },
                    reason: item.ClassificationReason ?? "independent_mod",
                    missingParentIds: Array.Empty<string>());
                continue;
            }

            var missingParentIds = item.ParentWorkshopIds
                .Where(id => !string.IsNullOrWhiteSpace(id) && !map.ContainsKey(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var resolvedParentIds = item.ParentWorkshopIds
                .Where(id => !string.IsNullOrWhiteSpace(id) && map.ContainsKey(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (resolvedParentIds.Length == 0)
            {
                AddChain(
                    orderedIds: new[] { item.WorkshopId },
                    reason: missingParentIds.Length > 0
                        ? "parent_dependency_missing"
                        : item.ClassificationReason ?? "parent_dependency",
                    missingParentIds: missingParentIds);
                continue;
            }

            foreach (var parentId in resolvedParentIds)
            {
                var ordered = BuildParentFirstChain(parentId, item.WorkshopId, map);
                AddChain(
                    orderedIds: ordered,
                    reason: missingParentIds.Length > 0
                        ? "parent_dependency_partial_missing"
                        : item.ClassificationReason ?? "parent_dependency",
                    missingParentIds: missingParentIds);
            }
        }

        return chains;

        void AddChain(IReadOnlyList<string> orderedIds, string reason, IReadOnlyList<string> missingParentIds)
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
        if (string.IsNullOrWhiteSpace(currentId) || !map.ContainsKey(currentId))
        {
            return;
        }

        if (!visited.Add(currentId))
        {
            return;
        }

        var parent = map[currentId];
        if (parent.ParentWorkshopIds.Count > 0)
        {
            foreach (var ancestor in parent.ParentWorkshopIds)
            {
                BuildParentStack(ancestor, map, visited, ordered);
            }
        }

        ordered.Add(currentId);
    }
}
