using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// One field edit pending bulk-apply.
/// </summary>
public sealed record StatEdit(string FieldName, float Value);

/// <summary>
/// P7 / Task #171 — Bulk Unit Stat Editor. Composes the Inspector
/// (#148) + SetUnitField generic setter (#157) + multi-selection
/// model (#103). The operator stages a list of (field, value) edits,
/// then applies them to every selected unit in one click; the VM
/// reports per-unit success/failure plus an aggregate summary.
/// </summary>
public sealed class UnitStatEditorState
{
    private readonly IUnitStatEditDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;
    private readonly TacticalUnitSelection _selection;
    private readonly List<StatEdit> _pendingEdits = new();

    public UnitStatEditorState(
        IUnitStatEditDispatcher dispatcher,
        IUxFeedbackSink feedback,
        TacticalUnitSelection selection)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(selection);
        _dispatcher = dispatcher;
        _feedback = feedback;
        _selection = selection;
    }

    public IReadOnlyList<StatEdit> PendingEdits => _pendingEdits;

    public UxFeedback StageEdit(string field, float value)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return Emit(UxFeedback.Error("stat_edit.stage", "field name required", "stat_edit"));
        }
        // De-dup: if the same field is already staged, the latest value wins.
        var idx = _pendingEdits.FindIndex(e =>
            string.Equals(e.FieldName, field, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            _pendingEdits[idx] = new StatEdit(field, value);
            return Emit(UxFeedback.Info("stat_edit.stage",
                $"updated staged '{field}' = {value}", "stat_edit"));
        }
        _pendingEdits.Add(new StatEdit(field, value));
        return Emit(UxFeedback.Success("stat_edit.stage",
            $"staged '{field}' = {value}", "stat_edit"));
    }

    public UxFeedback ClearStaged()
    {
        var n = _pendingEdits.Count;
        _pendingEdits.Clear();
        return Emit(UxFeedback.Info("stat_edit.clear",
            $"cleared {n} staged edits", "stat_edit"));
    }

    public async Task<UxFeedback> ApplyAllAsync(CancellationToken ct = default)
    {
        if (_pendingEdits.Count == 0)
        {
            return Emit(UxFeedback.Warning("stat_edit.apply",
                "no edits staged", "stat_edit"));
        }
        var summary = _selection.ClassifyBulk();
        if (summary.WritableSelected == 0)
        {
            return Emit(UxFeedback.Error("stat_edit.apply",
                summary.EnemySelectedSkipped > 0
                    ? $"every selection ({summary.EnemySelectedSkipped}) is enemy — READ-ONLY blocks bulk apply"
                    : "no units selected — pick at least one local unit",
                "stat_edit"));
        }

        var totalApplied = 0;
        var totalFailed = 0;
        var failedDetails = new List<string>();
        foreach (var row in _selection.WritableSelectedRows)
        {
            foreach (var edit in _pendingEdits)
            {
                var ok = await _dispatcher.SetUnitFieldAsync(
                    row.ObjAddr, edit.FieldName, edit.Value, ct);
                if (ok) totalApplied++;
                else
                {
                    totalFailed++;
                    failedDetails.Add($"0x{row.ObjAddr:X}.{edit.FieldName}");
                }
            }
        }

        var totalAttempted = totalApplied + totalFailed;
        if (totalFailed == 0)
        {
            return Emit(UxFeedback.Success("stat_edit.apply",
                $"applied {totalApplied} edits across {summary.WritableSelected} units",
                "stat_edit"));
        }
        if (totalApplied == 0)
        {
            return Emit(UxFeedback.Error("stat_edit.apply",
                $"every edit ({totalFailed}) failed — bridge may be detached or fields invalid",
                "stat_edit"));
        }
        return Emit(UxFeedback.Warning("stat_edit.apply",
            $"partial: {totalApplied}/{totalAttempted} succeeded — failed: " +
            $"{string.Join(", ", failedDetails.Take(5))}" +
            (failedDetails.Count > 5 ? $" (+{failedDetails.Count - 5} more)" : ""),
            "stat_edit"));
    }

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

public interface IUnitStatEditDispatcher
{
    Task<bool> SetUnitFieldAsync(long objAddr, string field, float value, CancellationToken ct);
}
