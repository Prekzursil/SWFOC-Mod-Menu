using System.Globalization;

namespace SwfocTrainer.Core.Models;

/// <summary>
/// One row from <c>SWFOC_ListTacticalUnits</c> / <c>SWFOC_EnumerateUnits</c>.
/// Wire format (pipe-separated):
///   <c>count=N|obj_addr;owner_slot;hull;invuln_flag;prevent_death;is_local;is_selected|...</c>
///
/// This model replaces the scalar obj_addr/hull text fields the V2
/// editor was binding to — the DataGrid ItemsSource now gets a typed
/// list, and multi-select dispatch operates on <see cref="TacticalUnitSelection"/>.
/// Task #103.
/// </summary>
public sealed record TacticalUnitRow(
    long ObjAddr,
    int OwnerSlot,
    float Hull,
    byte InvulnFlag,
    byte PreventDeath,
    bool IsLocal,
    bool IsSelected)
{
    /// <summary>
    /// Render the obj_addr as the hex string the bridge expects when
    /// building Lua commands (<c>0x7FFC.....</c>). Small convenience so
    /// call sites don't reimplement the format.
    /// </summary>
    public string ObjAddrHex => $"0x{ObjAddr:X}";
}

/// <summary>
/// Parse the CSV wire format emitted by <c>SWFOC_ListTacticalUnits</c>
/// and its filtered variant. Tolerant of empty payloads and whitespace;
/// malformed rows are skipped with a recorded count in
/// <see cref="TacticalUnitListParseResult.MalformedRowCount"/> so the UI
/// can surface "M of N rows unparseable" without throwing.
/// </summary>
public sealed record TacticalUnitListParseResult(
    int DeclaredCount,
    int MalformedRowCount,
    IReadOnlyList<TacticalUnitRow> Rows);

public static class TacticalUnitListParser
{
    /// <summary>
    /// Parse the <c>count=N|row1|row2|...</c> wire format. Returns an
    /// empty result for null/empty input or for inputs that don't start
    /// with the <c>count=</c> prefix.
    /// </summary>
    public static TacticalUnitListParseResult Parse(string? wirePayload)
    {
        if (string.IsNullOrWhiteSpace(wirePayload))
        {
            return new TacticalUnitListParseResult(0, 0, Array.Empty<TacticalUnitRow>());
        }

        var pieces = wirePayload.Split('|');
        if (pieces.Length == 0 || !pieces[0].StartsWith("count=", StringComparison.Ordinal))
        {
            return new TacticalUnitListParseResult(0, 0, Array.Empty<TacticalUnitRow>());
        }

        var countStr = pieces[0].AsSpan("count=".Length);
        if (!int.TryParse(countStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var declared))
        {
            declared = 0;
        }

        var rows = new List<TacticalUnitRow>(Math.Max(0, pieces.Length - 1));
        var malformed = 0;
        for (var i = 1; i < pieces.Length; i++)
        {
            var row = pieces[i];
            if (string.IsNullOrWhiteSpace(row))
            {
                continue;
            }
            var parsedRow = TryParseRow(row);
            if (parsedRow is null)
            {
                malformed++;
                continue;
            }
            rows.Add(parsedRow);
        }
        return new TacticalUnitListParseResult(declared, malformed, rows);
    }

    private static TacticalUnitRow? TryParseRow(string row)
    {
        // Expected 7 fields: obj_addr;owner_slot;hull;invuln_flag;prevent_death;is_local;is_selected
        var fields = row.Split(';');
        if (fields.Length < 7)
        {
            return null;
        }
        if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var addr)
            || addr == 0L)
        {
            return null;
        }
        if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot))
        {
            return null;
        }
        if (!float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var hull))
        {
            return null;
        }
        if (!byte.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var invuln))
        {
            return null;
        }
        if (!byte.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var preventDeath))
        {
            return null;
        }
        if (!int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var isLocalInt))
        {
            return null;
        }
        if (!int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var isSelectedInt))
        {
            return null;
        }

        return new TacticalUnitRow(
            ObjAddr: addr,
            OwnerSlot: slot,
            Hull: hull,
            InvulnFlag: invuln,
            PreventDeath: preventDeath,
            IsLocal: isLocalInt != 0,
            IsSelected: isSelectedInt != 0);
    }
}

/// <summary>
/// Mutable selection-set model for the V2 DataGrid. Wraps a backing
/// list + an <c>ObservableCollection</c>-friendly interface (the
/// ViewModel's ItemsSource). Every operation returns the count of
/// affected rows so the ViewModel can compose a user-visible summary
/// like "applied HP=99999 to 4 selected units, 1 enemy skipped".
/// </summary>
public sealed class TacticalUnitSelection
{
    private readonly List<TacticalUnitRow> _rows = new();

    /// <summary>Every row currently loaded — selected or not.</summary>
    public IReadOnlyList<TacticalUnitRow> Rows => _rows;

    /// <summary>Rows flagged as selected (multi-selection from the grid).</summary>
    public IReadOnlyList<TacticalUnitRow> SelectedRows =>
        _rows.Where(r => r.IsSelected).ToList();

    /// <summary>Selected rows that are also local — the only ones writable.</summary>
    public IReadOnlyList<TacticalUnitRow> WritableSelectedRows =>
        _rows.Where(r => r.IsSelected && r.IsLocal).ToList();

    /// <summary>
    /// Replace the loaded rows with a fresh list (typically the parse
    /// result of a ListTacticalUnits refresh). Returns the row count.
    /// </summary>
    public int LoadRows(IEnumerable<TacticalUnitRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows.Clear();
        _rows.AddRange(rows);
        return _rows.Count;
    }

    /// <summary>
    /// Flip the selected flag on every row whose obj_addr is in the
    /// provided set. Rows not in the set get IsSelected=false.
    /// Returns the count of rows that ended up selected.
    /// </summary>
    public int ApplySelection(IReadOnlySet<long> selectedAddrs)
    {
        ArgumentNullException.ThrowIfNull(selectedAddrs);
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            _rows[i] = row with { IsSelected = selectedAddrs.Contains(row.ObjAddr) };
        }
        return _rows.Count(r => r.IsSelected);
    }

    /// <summary>
    /// Clear all selection flags. Returns the count before clearing so
    /// UI can report "deselected N units".
    /// </summary>
    public int ClearSelection()
    {
        var count = _rows.Count(r => r.IsSelected);
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (row.IsSelected)
            {
                _rows[i] = row with { IsSelected = false };
            }
        }
        return count;
    }

    /// <summary>
    /// Split a bulk-apply operation into (writable-selected, enemy-skipped,
    /// unselected) counts. The UI composes a status line from this summary
    /// without re-walking the row list.
    /// </summary>
    public BulkApplySummary ClassifyBulk()
    {
        var writable = _rows.Count(r => r.IsSelected && r.IsLocal);
        var enemySkipped = _rows.Count(r => r.IsSelected && !r.IsLocal);
        var unselected = _rows.Count(r => !r.IsSelected);
        return new BulkApplySummary(writable, enemySkipped, unselected);
    }
}

/// <summary>
/// Summary of a bulk-apply classification. See
/// <see cref="TacticalUnitSelection.ClassifyBulk"/>.
/// </summary>
public sealed record BulkApplySummary(
    int WritableSelected,
    int EnemySelectedSkipped,
    int Unselected);
