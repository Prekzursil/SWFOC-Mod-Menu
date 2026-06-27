using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

// ============================================================================
// Tab — Tactical Units (filterable list)
//
// Thin INPC wrapper over Core.V2Vm.TacticalUnitsFilterTabState. The Core
// state is Pure (no WPF deps) and unit-tested; this wrapper adds the
// ICommand surface + ObservableCollection that the WPF DataGrid binds to.
//
// Refresh reloads the full list via SWFOC_ListTacticalUnits; filter changes
// just re-project FilteredRows from the already-loaded set without a
// round-trip.
// ============================================================================

public sealed class TacticalUnitsFilterTabViewModel : ObservableBase
{
    private readonly TacticalUnitsFilterTabState _state;
    private readonly RecordingFeedbackSink _sink;

    private string _factionSlotFilterText = string.Empty;
    private string _textFilter = string.Empty;
    private bool _selectedOnlyFilter;
    private string _lastStatus = "(idle)";

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge(
        "SWFOC_ListTacticalUnits", "SWFOC_EnumerateUnits");

    public TacticalUnitsFilterTabViewModel(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _sink = new RecordingFeedbackSink();
        var dispatcher = new BridgeTacticalUnitsListDispatcher(bridge);
        _state = new TacticalUnitsFilterTabState(dispatcher, _sink);
        FilteredRows = new ObservableCollection<TacticalUnitRow>();
        RefreshCommand = new AsyncRelayCommand(
            executeAsync: RefreshAsyncCore,
            onError: ex => LastStatus = $"refresh failed: {ex.Message}");
        ExportCsvCommand = new RelayCommand(ExportCurrentRowsToCsv);
        Refresh = NewRefreshAction();
    }

    /// <summary>
    /// Internal ctor so unit tests can swap in a recording dispatcher
    /// without spinning up a real V2BridgeAdapter (which needs a pipe).
    /// </summary>
    internal TacticalUnitsFilterTabViewModel(
        ITacticalUnitsListDispatcher dispatcher,
        RecordingFeedbackSink sink)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
        _state = new TacticalUnitsFilterTabState(dispatcher, _sink);
        FilteredRows = new ObservableCollection<TacticalUnitRow>();
        RefreshCommand = new AsyncRelayCommand(
            executeAsync: RefreshAsyncCore,
            onError: ex => LastStatus = $"refresh failed: {ex.Message}");
        ExportCsvCommand = new RelayCommand(ExportCurrentRowsToCsv);
        Refresh = NewRefreshAction();
    }

    // 2026-04-27 (iter 59): per-button capability metadata. Refresh routes
    // through SWFOC_ListTacticalUnits — engine walker, catalogued LIVE.
    private static SwfocTrainer.Core.Diagnostics.CapabilityAwareAction NewRefreshAction() =>
        new("Refresh tactical units", "SWFOC_ListTacticalUnits");

    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction Refresh { get; }
    public IReadOnlyList<SwfocTrainer.Core.Diagnostics.CapabilityAwareAction> AllActions =>
        new[] { Refresh };

    public ObservableCollection<TacticalUnitRow> FilteredRows { get; }

    public ICommand RefreshCommand { get; }

    /// <summary>
    /// 2026-04-27: dump the currently-projected (filtered) rows as CSV
    /// to the system clipboard. Same data as the DataGrid + a 1-line
    /// header so the operator can paste straight into Excel / Google
    /// Sheets without dragging a 5000-row table to a file.
    /// </summary>
    public ICommand ExportCsvCommand { get; }

    private void ExportCurrentRowsToCsv()
    {
        var sb = new System.Text.StringBuilder(8 * 1024);
        sb.AppendLine("obj_addr_hex,obj_addr_decimal,owner_slot,hull,invuln_flag,prevent_death,is_local,is_selected");
        foreach (var r in FilteredRows)
        {
            sb.Append("0x").Append(r.ObjAddr.ToString("X")).Append(',');
            sb.Append(r.ObjAddr).Append(',');
            sb.Append(r.OwnerSlot).Append(',');
            sb.Append(r.Hull.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.InvulnFlag).Append(',');
            sb.Append(r.PreventDeath).Append(',');
            sb.Append(r.IsLocal ? 1 : 0).Append(',');
            sb.Append(r.IsSelected ? 1 : 0);
            sb.AppendLine();
        }
        try
        {
            System.Windows.Clipboard.SetText(sb.ToString());
            LastStatus = $"Exported {FilteredRows.Count} row(s) to clipboard as CSV.";
        }
        catch (Exception ex)
        {
            LastStatus = $"CSV export failed: {ex.Message}";
        }
    }

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    /// <summary>
    /// 2026-04-27: row the operator currently right-clicked on. Bound by
    /// the DataGrid's SelectedItem so the per-row context menu commands
    /// know which row to act on. Null when nothing is selected.
    /// </summary>
    private TacticalUnitRow? _selectedRow;
    public TacticalUnitRow? SelectedRow
    {
        get => _selectedRow;
        set => SetField(ref _selectedRow, value);
    }

    /// <summary>2026-04-27: copy the selected row's obj_addr in hex.</summary>
    public RelayCommand CopySelectedAddrHexCommand =>
        _copySelectedAddrHexCommand ??= new RelayCommand(
            () => CopyToClipboardSafely(_selectedRow is { } r ? $"0x{r.ObjAddr:X}" : string.Empty),
            () => _selectedRow is not null);
    private RelayCommand? _copySelectedAddrHexCommand;

    /// <summary>2026-04-27: copy the selected row's obj_addr in decimal.</summary>
    public RelayCommand CopySelectedAddrDecimalCommand =>
        _copySelectedAddrDecimalCommand ??= new RelayCommand(
            () => CopyToClipboardSafely(_selectedRow is { } r ? r.ObjAddr.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty),
            () => _selectedRow is not null);
    private RelayCommand? _copySelectedAddrDecimalCommand;

    /// <summary>2026-04-27: copy the selected row as a single CSV line.</summary>
    public RelayCommand CopySelectedRowCsvCommand =>
        _copySelectedRowCsvCommand ??= new RelayCommand(
            () => CopyToClipboardSafely(_selectedRow is { } r
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "0x{0:X},{0},{1},{2:0.000},{3},{4},{5},{6}",
                    r.ObjAddr, r.OwnerSlot, r.Hull, r.InvulnFlag, r.PreventDeath,
                    r.IsLocal ? 1 : 0, r.IsSelected ? 1 : 0)
                : string.Empty),
            () => _selectedRow is not null);
    private RelayCommand? _copySelectedRowCsvCommand;

    private void CopyToClipboardSafely(string blob)
    {
        if (string.IsNullOrEmpty(blob))
        {
            LastStatus = "Nothing selected to copy.";
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(blob);
            LastStatus = $"Copied: {blob}";
        }
        catch (Exception ex)
        {
            LastStatus = $"Clipboard copy failed: {ex.Message}";
        }
    }

    public string FactionSlotFilterText
    {
        get => _factionSlotFilterText;
        set
        {
            if (SetField(ref _factionSlotFilterText, value ?? string.Empty))
            {
                _state.FactionSlotFilter = ParseSlotFilter(value);
                Reproject();
            }
        }
    }

    public string TextFilter
    {
        get => _textFilter;
        set
        {
            if (SetField(ref _textFilter, value ?? string.Empty))
            {
                _state.TextFilter = _textFilter;
                Reproject();
            }
        }
    }

    public bool SelectedOnlyFilter
    {
        get => _selectedOnlyFilter;
        set
        {
            if (SetField(ref _selectedOnlyFilter, value))
            {
                _state.SelectedOnlyFilter = value;
                Reproject();
            }
        }
    }

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public int TotalRowCount => _state.Selection.Rows.Count;

    public int FilteredRowCount => FilteredRows.Count;

    private async Task RefreshAsyncCore()
    {
        var fb = await _state.RefreshAsync().ConfigureAwait(true);
        Reproject();
        LastStatus = $"{fb.Severity}: {fb.Title} — {fb.Message}";
        OnPropertyChanged(nameof(TotalRowCount));
    }

    private void Reproject()
    {
        var rows = _state.FilteredRows();
        FilteredRows.Clear();
        foreach (var row in rows) FilteredRows.Add(row);
        OnPropertyChanged(nameof(FilteredRowCount));
    }

    private static int? ParseSlotFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value.Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var slot) ? slot : null;
    }
}
