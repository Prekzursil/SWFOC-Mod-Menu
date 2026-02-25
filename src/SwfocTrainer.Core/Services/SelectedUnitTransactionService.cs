#pragma warning disable S4136
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class SelectedUnitTransactionService : ISelectedUnitTransactionService
{
    private const string SelectedHpSymbol = "selected_hp";
    private const string SelectedShieldSymbol = "selected_shield";
    private const string SelectedSpeedSymbol = "selected_speed";
    private const string SelectedDamageMultiplierSymbol = "selected_damage_multiplier";
    private const string SelectedCooldownMultiplierSymbol = "selected_cooldown_multiplier";
    private const string SelectedVeterancySymbol = "selected_veterancy";
    private const string SelectedOwnerFactionSymbol = "selected_owner_faction";
    private const string BundlePassResult = "bundle_pass";
    private const string BundleRollbackResult = "bundle_rollback";
    private const float FloatComparisonTolerance = 0.0001f;

    private readonly IRuntimeAdapter _runtime;
    private readonly TrainerOrchestrator _orchestrator;
    private readonly List<SelectedUnitTransactionRecord> _history = new();

    private static readonly IReadOnlyList<FieldBinding> Bindings = new[]
    {
        new FieldBinding(SelectedHpSymbol, "set_selected_hp", isFloat: true),
        new FieldBinding(SelectedShieldSymbol, "set_selected_shield", isFloat: true),
        new FieldBinding(SelectedSpeedSymbol, "set_selected_speed", isFloat: true),
        new FieldBinding(SelectedDamageMultiplierSymbol, "set_selected_damage_multiplier", isFloat: true),
        new FieldBinding(SelectedCooldownMultiplierSymbol, "set_selected_cooldown_multiplier", isFloat: true),
        new FieldBinding(SelectedVeterancySymbol, "set_selected_veterancy", isFloat: false),
        new FieldBinding(SelectedOwnerFactionSymbol, "set_selected_owner_faction", isFloat: false),
    };

    public SelectedUnitTransactionService(IRuntimeAdapter runtime, TrainerOrchestrator orchestrator)
    {
        _runtime = runtime;
        _orchestrator = orchestrator;
    }

    public SelectedUnitSnapshot? Baseline { get; private set; }

    public IReadOnlyList<SelectedUnitTransactionRecord> History => _history;

    public async Task<SelectedUnitSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        EnsureAttached();
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        Baseline ??= snapshot;
        return snapshot;
    }

    public async Task<SelectedUnitTransactionResult> ApplyAsync(
        string profileId,
        SelectedUnitDraft draft,
        RuntimeMode runtimeMode,
        CancellationToken cancellationToken)
    {
        var runtimeModeFailure = ValidateRuntimeMode(runtimeMode, "transaction");
        if (runtimeModeFailure is not null)
        {
            return runtimeModeFailure;
        }

        if (draft.IsEmpty)
        {
            return Failed("Selected-unit transaction has no field changes.", "empty_draft");
        }

        EnsureAttached();
        var before = await ReadSnapshotAsync(cancellationToken);
        Baseline ??= before;

        var transactionId = Guid.NewGuid().ToString("N");
        var plannedChanges = BuildChanges(before, draft);
        if (plannedChanges.Count == 0)
        {
            return Failed("Selected-unit transaction has no effective changes.", "no_effective_change");
        }

        var execution = await ExecuteChangesWithRollbackAsync(
            profileId,
            runtimeMode,
            plannedChanges,
            transactionId,
            "apply",
            cancellationToken);
        if (!execution.Succeeded)
        {
            return BuildApplyFailureResult(transactionId, execution);
        }

        return await BuildSuccessResultAsync(
            transactionId,
            before,
            plannedChanges,
            execution.Steps,
            operation: "apply",
            successMessage: $"Applied {plannedChanges.Count} selected-unit field update(s).",
            cancellationToken);
    }

    public async Task<SelectedUnitTransactionResult> RevertLastAsync(
        string profileId,
        RuntimeMode runtimeMode,
        CancellationToken cancellationToken)
    {
        if (_history.Count == 0)
        {
            return Failed("No selected-unit transaction history exists.", "history_empty");
        }

        var last = _history[^1];
        var revertId = $"revert-{last.TransactionId}";
        var result = await ApplySnapshotAsync(profileId, runtimeMode, last.Before, revertId, "revert_last", cancellationToken);
        if (!result.Succeeded)
        {
            return result;
        }

        _history.RemoveAt(_history.Count - 1);
        return result with { Message = $"Reverted transaction {last.TransactionId}." };
    }

    public Task<SelectedUnitTransactionResult> RestoreBaselineAsync(
        string profileId,
        RuntimeMode runtimeMode,
        CancellationToken cancellationToken)
    {
        if (Baseline is null)
        {
            return Task.FromResult(Failed("No baseline snapshot captured yet.", "baseline_missing"));
        }

        return ApplySnapshotAsync(
            profileId,
            runtimeMode,
            Baseline,
            $"baseline-{Guid.NewGuid():N}",
            "restore_baseline",
            cancellationToken);
    }

    public Task<SelectedUnitSnapshot> CaptureAsync()
    {
        return CaptureAsync(CancellationToken.None);
    }

    public Task<SelectedUnitTransactionResult> ApplyAsync(
        string profileId,
        SelectedUnitDraft draft,
        RuntimeMode runtimeMode)
    {
        return ApplyAsync(profileId, draft, runtimeMode, CancellationToken.None);
    }

    public Task<SelectedUnitTransactionResult> RevertLastAsync(
        string profileId,
        RuntimeMode runtimeMode)
    {
        return RevertLastAsync(profileId, runtimeMode, CancellationToken.None);
    }

    public Task<SelectedUnitTransactionResult> RestoreBaselineAsync(
        string profileId,
        RuntimeMode runtimeMode)
    {
        return RestoreBaselineAsync(profileId, runtimeMode, CancellationToken.None);
    }

    private async Task<SelectedUnitTransactionResult> ApplySnapshotAsync(
        string profileId,
        RuntimeMode runtimeMode,
        SelectedUnitSnapshot snapshot,
        string transactionId,
        string operation,
        CancellationToken cancellationToken)
    {
        var runtimeModeFailure = ValidateRuntimeMode(runtimeMode, "operation");
        if (runtimeModeFailure is not null)
        {
            return runtimeModeFailure;
        }

        EnsureAttached();
        var draft = BuildDraftFromSnapshot(snapshot);
        var before = await ReadSnapshotAsync(cancellationToken);
        var changes = BuildChanges(before, draft);
        if (changes.Count == 0)
        {
            return BuildNoPendingChangesResult(transactionId, operation);
        }

        var execution = await ExecuteChangesWithRollbackAsync(
            profileId,
            runtimeMode,
            changes,
            transactionId,
            operation,
            cancellationToken);
        if (!execution.Succeeded)
        {
            return BuildSnapshotFailureResult(transactionId, operation, execution);
        }

        return await BuildSuccessResultAsync(
            transactionId,
            before,
            changes,
            execution.Steps,
            operation,
            $"Selected-unit {operation} succeeded ({changes.Count} writes).",
            cancellationToken);
    }

    private static SelectedUnitTransactionResult? ValidateRuntimeMode(RuntimeMode runtimeMode, string operationLabel)
    {
        if (runtimeMode == RuntimeMode.Unknown)
        {
            return Failed($"Selected-unit {operationLabel} is blocked: runtime mode is unknown.", "mode_unknown_strict_gate");
        }

        if (runtimeMode != RuntimeMode.Tactical)
        {
            return Failed(
                $"Selected-unit {operationLabel} requires tactical mode, current mode is {runtimeMode}.",
                "mode_mismatch");
        }

        return null;
    }

    private async Task<ChangeExecutionOutcome> ExecuteChangesWithRollbackAsync(
        string profileId,
        RuntimeMode runtimeMode,
        IReadOnlyList<SelectedUnitChange> changes,
        string transactionId,
        string operation,
        CancellationToken cancellationToken)
    {
        var steps = new List<ActionExecutionResult>(changes.Count);
        var applied = new List<SelectedUnitChange>(changes.Count);
        foreach (var change in changes)
        {
            var context = BuildContext(transactionId, operation, change.ActionId, BundlePassResult);
            var result = await ExecuteChangeAsync(profileId, runtimeMode, change, context, cancellationToken);
            steps.Add(result);
            if (result.Succeeded)
            {
                applied.Add(change);
                continue;
            }

            var rollbackSteps = await RollbackAsync(profileId, runtimeMode, applied, transactionId, cancellationToken);
            return ChangeExecutionOutcome.CreateFailure(change, result, steps, rollbackSteps);
        }

        return ChangeExecutionOutcome.CreateSuccess(steps);
    }

    private static SelectedUnitTransactionResult BuildApplyFailureResult(string transactionId, ChangeExecutionOutcome execution)
    {
        var rollbackSucceeded = execution.RollbackSteps.All(x => x.Succeeded);
        var message = rollbackSucceeded
            ? $"Apply failed at '{execution.FailedChange!.ActionId}' and rollback succeeded. {execution.FailureResult!.Message}"
            : $"Apply failed at '{execution.FailedChange!.ActionId}' and rollback was partial. {execution.FailureResult!.Message}";
        return new SelectedUnitTransactionResult(
            false,
            message,
            transactionId,
            execution.Steps,
            RolledBack: true,
            RollbackSteps: execution.RollbackSteps);
    }

    private static SelectedUnitTransactionResult BuildSnapshotFailureResult(
        string transactionId,
        string operation,
        ChangeExecutionOutcome execution)
    {
        return new SelectedUnitTransactionResult(
            false,
            $"Selected-unit {operation} failed at '{execution.FailedChange!.ActionId}'. {execution.FailureResult!.Message}",
            transactionId,
            execution.Steps,
            RolledBack: execution.RollbackSteps.All(x => x.Succeeded),
            RollbackSteps: execution.RollbackSteps);
    }

    private static SelectedUnitTransactionResult BuildNoPendingChangesResult(string transactionId, string operation)
    {
        return new SelectedUnitTransactionResult(
            true,
            $"Selected-unit {operation} has no pending changes.",
            transactionId,
            Array.Empty<ActionExecutionResult>());
    }

    private async Task<SelectedUnitTransactionResult> BuildSuccessResultAsync(
        string transactionId,
        SelectedUnitSnapshot before,
        IReadOnlyList<SelectedUnitChange> changes,
        IReadOnlyList<ActionExecutionResult> steps,
        string operation,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var after = await ReadSnapshotAsync(cancellationToken);
        _history.Add(new SelectedUnitTransactionRecord(
            transactionId,
            DateTimeOffset.UtcNow,
            before,
            after,
            IsRollback: operation != "apply",
            operation,
            changes.Select(x => x.ActionId).ToArray()));

        return new SelectedUnitTransactionResult(true, successMessage, transactionId, steps);
    }

    private static SelectedUnitDraft BuildDraftFromSnapshot(SelectedUnitSnapshot snapshot)
    {
        return new SelectedUnitDraft(
            Hp: snapshot.Hp,
            Shield: snapshot.Shield,
            Speed: snapshot.Speed,
            DamageMultiplier: snapshot.DamageMultiplier,
            CooldownMultiplier: snapshot.CooldownMultiplier,
            Veterancy: snapshot.Veterancy,
            OwnerFaction: snapshot.OwnerFaction);
    }

    private async Task<IReadOnlyList<ActionExecutionResult>> RollbackAsync(
        string profileId,
        RuntimeMode runtimeMode,
        IReadOnlyList<SelectedUnitChange> applied,
        string transactionId,
        CancellationToken cancellationToken)
    {
        if (applied.Count == 0)
        {
            return Array.Empty<ActionExecutionResult>();
        }

        var rollbackResults = new List<ActionExecutionResult>(applied.Count);
        for (var i = applied.Count - 1; i >= 0; i--)
        {
            var step = applied[i];
            var rollback = step with { NewValue = step.OldValue };
            var context = BuildContext(
                transactionId,
                "rollback",
                rollback.ActionId,
                BundleRollbackResult);
            rollbackResults.Add(await ExecuteChangeAsync(profileId, runtimeMode, rollback, context, cancellationToken));
        }

        return rollbackResults;
    }

    private Task<ActionExecutionResult> ExecuteChangeAsync(
        string profileId,
        RuntimeMode runtimeMode,
        SelectedUnitChange change,
        IReadOnlyDictionary<string, object?> context,
        CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["symbol"] = change.Symbol
        };

        if (change.IsFloat)
        {
            payload["floatValue"] = Convert.ToSingle(change.NewValue);
        }
        else
        {
            payload["intValue"] = Convert.ToInt32(change.NewValue);
        }

        return _orchestrator.ExecuteAsync(profileId, change.ActionId, payload, runtimeMode, context, cancellationToken);
    }

    private async Task<SelectedUnitSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        var hp = await _runtime.ReadAsync<float>("selected_hp", cancellationToken);
        var shield = await _runtime.ReadAsync<float>("selected_shield", cancellationToken);
        var speed = await _runtime.ReadAsync<float>("selected_speed", cancellationToken);
        var damage = await _runtime.ReadAsync<float>("selected_damage_multiplier", cancellationToken);
        var cooldown = await _runtime.ReadAsync<float>("selected_cooldown_multiplier", cancellationToken);
        var veterancy = await _runtime.ReadAsync<int>("selected_veterancy", cancellationToken);
        var owner = await _runtime.ReadAsync<int>("selected_owner_faction", cancellationToken);
        return new SelectedUnitSnapshot(
            hp,
            shield,
            speed,
            damage,
            cooldown,
            veterancy,
            owner,
            DateTimeOffset.UtcNow);
    }

    private static List<SelectedUnitChange> BuildChanges(SelectedUnitSnapshot before, SelectedUnitDraft draft)
    {
        var changes = new List<SelectedUnitChange>(Bindings.Count);
        foreach (var binding in Bindings)
        {
            var change = BuildChangeForBinding(before, draft, binding);
            if (change is not null)
            {
                changes.Add(change);
            }
        }

        return changes;
    }

    private static SelectedUnitChange? BuildChangeForBinding(SelectedUnitSnapshot before, SelectedUnitDraft draft, FieldBinding binding)
    {
        return binding.Symbol switch
        {
            SelectedHpSymbol => BuildFloatChange(binding, before.Hp, draft.Hp),
            SelectedShieldSymbol => BuildFloatChange(binding, before.Shield, draft.Shield),
            SelectedSpeedSymbol => BuildFloatChange(binding, before.Speed, draft.Speed),
            SelectedDamageMultiplierSymbol => BuildFloatChange(binding, before.DamageMultiplier, draft.DamageMultiplier),
            SelectedCooldownMultiplierSymbol => BuildFloatChange(binding, before.CooldownMultiplier, draft.CooldownMultiplier),
            SelectedVeterancySymbol => BuildIntChange(binding, before.Veterancy, draft.Veterancy),
            SelectedOwnerFactionSymbol => BuildIntChange(binding, before.OwnerFaction, draft.OwnerFaction),
            _ => null
        };
    }

    private static SelectedUnitChange? BuildFloatChange(FieldBinding binding, float currentValue, float? draftValue)
    {
        if (draftValue is null || ApproximatelyEqual(currentValue, draftValue.Value))
        {
            return null;
        }

        return new SelectedUnitChange(binding.Symbol, binding.ActionId, currentValue, draftValue.Value, IsFloat: true);
    }

    private static SelectedUnitChange? BuildIntChange(FieldBinding binding, int currentValue, int? draftValue)
    {
        if (draftValue is null || currentValue == draftValue.Value)
        {
            return null;
        }

        return new SelectedUnitChange(binding.Symbol, binding.ActionId, currentValue, draftValue.Value, IsFloat: false);
    }

    private void EnsureAttached()
    {
        if (!_runtime.IsAttached)
        {
            throw new InvalidOperationException("Runtime is not attached.");
        }
    }

    private static IReadOnlyDictionary<string, object?> BuildContext(
        string transactionId,
        string operation,
        string actionId,
        string bundleGateResult)
    {
        return new Dictionary<string, object?>
        {
            ["transactionId"] = transactionId,
            ["transactionOperation"] = operation,
            ["transactionActionId"] = actionId,
            ["bundleGateResult"] = bundleGateResult
        };
    }

    private static SelectedUnitTransactionResult Failed(string message, string reasonCode)
    {
        return new SelectedUnitTransactionResult(
            false,
            message,
            null,
            new[]
            {
                new ActionExecutionResult(
                    false,
                    message,
                    AddressSource.None,
                    new Dictionary<string, object?>
                    {
                        ["failureReasonCode"] = reasonCode
                    })
            });
    }

    private static bool ApproximatelyEqual(float left, float right)
    {
        return MathF.Abs(left - right) < FloatComparisonTolerance;
    }

    private sealed record FieldBinding(string Symbol, string ActionId, bool isFloat)
    {
        public bool IsFloat { get; } = isFloat;
    }

    private sealed record SelectedUnitChange(
        string Symbol,
        string ActionId,
        object OldValue,
        object NewValue,
        bool IsFloat);

    private sealed record ChangeExecutionOutcome(
        bool Succeeded,
        IReadOnlyList<ActionExecutionResult> Steps,
        IReadOnlyList<ActionExecutionResult> RollbackSteps,
        SelectedUnitChange? FailedChange,
        ActionExecutionResult? FailureResult)
    {
        public static ChangeExecutionOutcome CreateSuccess(IReadOnlyList<ActionExecutionResult> steps)
            => new(
                Succeeded: true,
                Steps: steps,
                RollbackSteps: Array.Empty<ActionExecutionResult>(),
                FailedChange: null,
                FailureResult: null);

        public static ChangeExecutionOutcome CreateFailure(
            SelectedUnitChange failedChange,
            ActionExecutionResult failureResult,
            IReadOnlyList<ActionExecutionResult> steps,
            IReadOnlyList<ActionExecutionResult> rollbackSteps)
            => new(
                Succeeded: false,
                Steps: steps,
                RollbackSteps: rollbackSteps,
                FailedChange: failedChange,
                FailureResult: failureResult);
    }
}
