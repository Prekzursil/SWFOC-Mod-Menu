using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// V2 Tab — filterable Tactical Units list. Task #107 — consumes
/// SWFOC_ListTacticalUnits and exposes filtered subsets the DataGrid
/// (#103) can bind ItemsSource to.
///
/// Filters compose: (faction-slot if set) AND (text-substring if set)
/// AND (selected-only if set). Each filter is independent.
/// </summary>
public sealed class TacticalUnitsFilterTabState
{
    private readonly ITacticalUnitsListDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;
    private readonly TacticalUnitSelection _selection = new();

    public TacticalUnitsFilterTabState(ITacticalUnitsListDispatcher dispatcher,
                                        IUxFeedbackSink feedback)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        _dispatcher = dispatcher;
        _feedback = feedback;
    }

    public TacticalUnitSelection Selection => _selection;
    public int? FactionSlotFilter { get; set; }
    public string TextFilter { get; set; } = string.Empty;
    public bool SelectedOnlyFilter { get; set; }

    public async Task<UxFeedback> RefreshAsync(CancellationToken ct = default)
    {
        var rows = await _dispatcher.ListTacticalUnitsAsync(ct);
        _selection.LoadRows(rows);
        return Emit(UxFeedback.Info("list_tactical",
            $"loaded {_selection.Rows.Count} units", "list_tactical"));
    }

    public IReadOnlyList<TacticalUnitRow> FilteredRows()
    {
        IEnumerable<TacticalUnitRow> q = _selection.Rows;
        if (FactionSlotFilter is int slot)
        {
            q = q.Where(r => r.OwnerSlot == slot);
        }
        if (!string.IsNullOrWhiteSpace(TextFilter))
        {
            var query = TextFilter.Trim();
            q = q.Where(r => r.ObjAddrHex.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        if (SelectedOnlyFilter)
        {
            q = q.Where(r => r.IsSelected);
        }
        return q.ToList();
    }

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

public interface ITacticalUnitsListDispatcher
{
    Task<IReadOnlyList<TacticalUnitRow>> ListTacticalUnitsAsync(CancellationToken ct);
}
