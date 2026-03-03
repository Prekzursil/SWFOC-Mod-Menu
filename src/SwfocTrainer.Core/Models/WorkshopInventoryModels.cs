namespace SwfocTrainer.Core.Models;

public enum WorkshopItemType
{
    Unknown = 0,
    Mod,
    Submod
}

public sealed record WorkshopInventoryRequest(
    string AppId = "32470",
    string? ManifestPath = null,
    string? WorkshopContentRootPath = null,
    bool FetchRemoteMetadata = true,
    int MetadataBatchSize = 100);

public sealed record WorkshopInventoryItem(
    string WorkshopId,
    string Title,
    WorkshopItemType ItemType,
    IReadOnlyList<string> ParentWorkshopIds,
    IReadOnlyList<string> Tags,
    string? Description = null,
    string? ClassificationReason = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record WorkshopInventoryChain(
    string ChainId,
    IReadOnlyList<string> OrderedWorkshopIds,
    string ClassificationReason,
    bool ParentFirst = true,
    IReadOnlyList<string>? MissingParentIds = null);

public sealed record WorkshopInventoryGraph(
    string AppId,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<WorkshopInventoryItem> Items,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<WorkshopInventoryChain>? Chains = null)
{
    public static WorkshopInventoryGraph Empty() => Empty("32470");

    public static WorkshopInventoryGraph Empty(string appId) =>
        new(
            AppId: appId,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Items: Array.Empty<WorkshopInventoryItem>(),
            Diagnostics: Array.Empty<string>(),
            Chains: Array.Empty<WorkshopInventoryChain>());
}
