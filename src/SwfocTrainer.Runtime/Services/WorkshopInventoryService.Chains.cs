using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed partial class WorkshopInventoryService
{
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
