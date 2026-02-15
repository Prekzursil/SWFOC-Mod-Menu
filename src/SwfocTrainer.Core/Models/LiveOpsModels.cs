using System.Text.Json.Serialization;

namespace SwfocTrainer.Core.Models;

/// <summary>
/// Reliability state and diagnostics for a single profile action.
/// </summary>
public sealed record ActionReliabilityInfo(
    string ActionId,
    ActionReliabilityState State,
    string ReasonCode,
    double Confidence,
    string? Detail = null);

/// <summary>
/// Captured selected-unit runtime values.
/// </summary>
public sealed record SelectedUnitSnapshot(
    float Hp,
    float Shield,
    float Speed,
    float DamageMultiplier,
    float CooldownMultiplier,
    int Veterancy,
    int OwnerFaction,
    DateTimeOffset CapturedAt);

/// <summary>
/// Optional selected-unit value edits to be applied as a transaction.
/// </summary>
public sealed record SelectedUnitDraft(
    float? Hp = null,
    float? Shield = null,
    float? Speed = null,
    float? DamageMultiplier = null,
    float? CooldownMultiplier = null,
    int? Veterancy = null,
    int? OwnerFaction = null)
{
    [JsonIgnore]
    public bool IsEmpty =>
        Hp is null &&
        Shield is null &&
        Speed is null &&
        DamageMultiplier is null &&
        CooldownMultiplier is null &&
        Veterancy is null &&
        OwnerFaction is null;
}

/// <summary>
/// History entry for a selected-unit transaction operation.
/// </summary>
public sealed record SelectedUnitTransactionRecord(
    string TransactionId,
    DateTimeOffset Timestamp,
    SelectedUnitSnapshot Before,
    SelectedUnitSnapshot After,
    bool IsRollback,
    string Message,
    IReadOnlyList<string> AppliedActions);

/// <summary>
/// Result of a selected-unit transaction apply/revert/baseline restore operation.
/// </summary>
public sealed record SelectedUnitTransactionResult(
    bool Succeeded,
    string Message,
    string? TransactionId,
    IReadOnlyList<ActionExecutionResult> Steps,
    bool RolledBack = false,
    IReadOnlyList<ActionExecutionResult>? RollbackSteps = null);

/// <summary>
/// Profile-scoped spawn preset definition.
/// </summary>
public sealed record SpawnPreset(
    string Id,
    string Name,
    string UnitId,
    string Faction,
    string EntryMarker,
    int DefaultQuantity = 1,
    int DefaultDelayMs = 125,
    string? Description = null);

/// <summary>
/// Single executable spawn request in a batch plan.
/// </summary>
public sealed record SpawnBatchItem(
    int Sequence,
    string UnitId,
    string Faction,
    string EntryMarker,
    int DelayMs);

/// <summary>
/// Expanded spawn plan ready for runtime execution.
/// </summary>
public sealed record SpawnBatchPlan(
    string ProfileId,
    string PresetId,
    bool StopOnFailure,
    IReadOnlyList<SpawnBatchItem> Items);

/// <summary>
/// Per-item execution outcome for spawn batches.
/// </summary>
public sealed record SpawnBatchItemResult(
    int Sequence,
    string UnitId,
    bool Succeeded,
    string Message,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);

/// <summary>
/// Aggregate execution summary for spawn batches.
/// </summary>
public sealed record SpawnBatchExecutionResult(
    bool Succeeded,
    string Message,
    int Attempted,
    int SucceededCount,
    int FailedCount,
    bool StoppedEarly,
    IReadOnlyList<SpawnBatchItemResult> Results);

/// <summary>
/// Runtime configuration for Live Ops preset loading.
/// </summary>
public sealed class LiveOpsOptions
{
    /// <summary>
    /// Root directory containing profile-scoped Live Ops preset files.
    /// </summary>
    public string PresetRootPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "profiles", "default", "presets");
}
