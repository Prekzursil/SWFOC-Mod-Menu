#pragma warning disable S4136
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class SelectedUnitTransactionService : ISelectedUnitTransactionService
{
    private readonly IRuntimeAdapter _runtime;
    private readonly TrainerOrchestrator _orchestrator;
    private readonly List<SelectedUnitTransactionRecord> _history = new();

    private static readonly IReadOnlyList<FieldBinding> Bindings = new[]
    {
        new FieldBinding("selected_hp", "set_selected_hp", isFloat: true),
        new FieldBinding("selected_shield", "set_selected_shield", isFloat: true),
        new FieldBinding("selected_speed", "set_selected_speed", isFloat: true),
        new FieldBinding("selected_damage_multiplier", "set_selected_damage_multiplier", isFloat: true),
        new FieldBinding("selected_cooldown_multiplier", "set_selected_cooldown_multiplier", isFloat: true),
        new FieldBinding("selected_veterancy", "set_selected_veterancy", isFloat: false),
        new FieldBinding("selected_owner_faction", "set_selected_owner_faction", isFloat: false),
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
        if (runtimeMode == RuntimeMode.Unknown)
        {
            return Failed("Selected-unit transaction is blocked: runtime mode is unknown.", "mode_unknown_strict_gate");
        }

        if (runtimeMode != RuntimeMode.Tactical)
        {
            return Failed(
                $"Selected-unit transaction requires tactical mode, current mode is {runtimeMode}.",
                "mode_mismatch");
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

        var steps = new List<ActionExecutionResult>(plannedChanges.Count);
        var applied = new List<SelectedUnitChange>(plannedChanges.Count);

        foreach (var change in plannedChanges)
        {
            var context = BuildContext(
                transactionId,
                "apply",
                change.ActionId,
                "bundle_pass");
            var result = await ExecuteChangeAsync(profileId, runtimeMode, change, context, cancellationToken);
            steps.Add(result);
            if (!result.Succeeded)
            {
                var rollbackSteps = await RollbackAsync(profileId, runtimeMode, applied, transactionId, cancellationToken);
                var rollbackSucceeded = rollbackSteps.All(x => x.Succeeded);
                var message = rollbackSucceeded
                    ? $"Apply failed at '{change.ActionId}' and rollback succeeded. {result.Message}"
                    : $"Apply failed at '{change.ActionId}' and rollback was partial. {result.Message}";
                return new SelectedUnitTransactionResult(
                    false,
                    message,
                    transactionId,
                    steps,
                    RolledBack: true,
                    RollbackSteps: rollbackSteps);
            }

            applied.Add(change);
        }

        var after = await ReadSnapshotAsync(cancellationToken);
        _history.Add(new SelectedUnitTransactionRecord(
            transactionId,
            DateTimeOffset.UtcNow,
            before,
            after,
            IsRollback: false,
            "apply",
            plannedChanges.Select(x => x.ActionId).ToArray()));

        return new SelectedUnitTransactionResult(
            true,
            $"Applied {plannedChanges.Count} selected-unit field update(s).",
            transactionId,
            steps);
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
        if (runtimeMode == RuntimeMode.Unknown)
        {
            return Failed("Selected-unit operation is blocked: runtime mode is unknown.", "mode_unknown_strict_gate");
        }

        if (runtimeMode != RuntimeMode.Tactical)
        {
            return Failed(
                $"Selected-unit operation requires tactical mode, current mode is {runtimeMode}.",
                "mode_mismatch");
        }

        EnsureAttached();
        var draft = new SelectedUnitDraft(
            Hp: snapshot.Hp,
            Shield: snapshot.Shield,
            Speed: snapshot.Speed,
            DamageMultiplier: snapshot.DamageMultiplier,
            CooldownMultiplier: snapshot.CooldownMultiplier,
            Veterancy: snapshot.Veterancy,
            OwnerFaction: snapshot.OwnerFaction);

        var before = await ReadSnapshotAsync(cancellationToken);
        var changes = BuildChanges(before, draft);
        if (changes.Count == 0)
        {
            return new SelectedUnitTransactionResult(
                true,
                $"Selected-unit {operation} has no pending changes.",
                transactionId,
                Array.Empty<ActionExecutionResult>());
        }

        var steps = new List<ActionExecutionResult>(changes.Count);
        var applied = new List<SelectedUnitChange>(changes.Count);
        foreach (var change in changes)
        {
            var context = BuildContext(
                transactionId,
                operation,
                change.ActionId,
                "bundle_pass");
            var result = await ExecuteChangeAsync(profileId, runtimeMode, change, context, cancellationToken);
            steps.Add(result);
            if (!result.Succeeded)
            {
                var rollbackSteps = await RollbackAsync(profileId, runtimeMode, applied, transactionId, cancellationToken);
                return new SelectedUnitTransactionResult(
                    false,
                    $"Selected-unit {operation} failed at '{change.ActionId}'. {result.Message}",
                    transactionId,
                    steps,
                    RolledBack: rollbackSteps.All(x => x.Succeeded),
                    RollbackSteps: rollbackSteps);
            }

            applied.Add(change);
        }

        var after = await ReadSnapshotAsync(cancellationToken);
        _history.Add(new SelectedUnitTransactionRecord(
            transactionId,
            DateTimeOffset.UtcNow,
            before,
            after,
            IsRollback: operation != "apply",
            operation,
            changes.Select(x => x.ActionId).ToArray()));

        return new SelectedUnitTransactionResult(
            true,
            $"Selected-unit {operation} succeeded ({changes.Count} writes).",
            transactionId,
            steps);
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
                "bundle_rollback");
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
            switch (binding.Symbol)
            {
                case "selected_hp" when draft.Hp is not null:
                    if (!ApproximatelyEqual(before.Hp, draft.Hp.Value))
                    {
                        changes.Add(new SelectedUnitChange(binding.Symbol, binding.ActionId, before.Hp, draft.Hp.Value, IsFloat: true));
                    }
                    break;
                case "selected_shield" when draft.Shield is not null:
                    if (!ApproximatelyEqual(before.Shield, draft.Shield.Value))
                    {
                        changes.Add(new SelectedUnitChange(binding.Symbol, binding.ActionId, before.Shield, draft.Shield.Value, IsFloat: true));
                    }
                    break;
                case "selected_speed" when draft.Speed is not null:
                    if (!ApproximatelyEqual(before.Speed, draft.Speed.Value))
                    {
                        changes.Add(new SelectedUnitChange(binding.Symbol, binding.ActionId, before.Speed, draft.Speed.Value, IsFloat: true));
                    }
                    break;
                case "selected_damage_multiplier" when draft.DamageMultiplier is not null:
                    if (!ApproximatelyEqual(before.DamageMultiplier, draft.DamageMultiplier.Value))
                    {
                        changes.Add(new SelectedUnitChange(binding.Symbol, binding.ActionId, before.DamageMultiplier, draft.DamageMultiplier.Value, IsFloat: true));
                    }
                    break;
                case "selected_cooldown_multiplier" when draft.CooldownMultiplier is not null:
                    if (!ApproximatelyEqual(before.CooldownMultiplier, draft.CooldownMultiplier.Value))
                    {
                        changes.Add(new SelectedUnitChange(binding.Symbol, binding.ActionId, before.CooldownMultiplier, draft.CooldownMultiplier.Value, IsFloat: true));
                    }
                    break;
                case "selected_veterancy" when draft.Veterancy is not null:
                    if (before.Veterancy != draft.Veterancy.Value)
                    {
                        changes.Add(new SelectedUnitChange(binding.Symbol, binding.ActionId, before.Veterancy, draft.Veterancy.Value, IsFloat: false));
                    }
                    break;
                case "selected_owner_faction" when draft.OwnerFaction is not null:
                    if (before.OwnerFaction != draft.OwnerFaction.Value)
                    {
                        changes.Add(new SelectedUnitChange(binding.Symbol, binding.ActionId, before.OwnerFaction, draft.OwnerFaction.Value, IsFloat: false));
                    }
                    break;
            }
        }

        return changes;
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
        return MathF.Abs(left - right) < 0.0001f;
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
}
