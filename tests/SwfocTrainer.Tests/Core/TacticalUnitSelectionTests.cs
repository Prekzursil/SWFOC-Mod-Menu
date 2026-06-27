using FluentAssertions;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Task #103 — tests for the multi-selection model the V2 DataGrid
/// binds to. Wire-format parse + bulk-selection semantics live here so
/// the WPF DataGrid layer can stay a thin adapter.
/// </summary>
public sealed class TacticalUnitSelectionTests
{
    private const string ThreeRowPayload =
        "count=3" +
        "|4096;1;100.000;0;0;1;1" +        // local + selected
        "|8192;0;250.500;1;128;0;0" +      // enemy, unselected, invuln+preventDeath set
        "|12288;1;50.000;0;0;1;0";         // local, unselected

    // ─── Parser ──────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyPayload_ReturnsEmptyResult()
    {
        var result = TacticalUnitListParser.Parse("");
        result.DeclaredCount.Should().Be(0);
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NullPayload_ReturnsEmptyResult()
    {
        var result = TacticalUnitListParser.Parse(null);
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_CountOnly_ReturnsZeroRows()
    {
        var result = TacticalUnitListParser.Parse("count=0");
        result.DeclaredCount.Should().Be(0);
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ThreeValidRows_ReturnsAll()
    {
        var result = TacticalUnitListParser.Parse(ThreeRowPayload);
        result.DeclaredCount.Should().Be(3);
        result.MalformedRowCount.Should().Be(0);
        result.Rows.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_Row0_DecodesEveryField()
    {
        var row = TacticalUnitListParser.Parse(ThreeRowPayload).Rows[0];
        row.ObjAddr.Should().Be(4096L);
        row.OwnerSlot.Should().Be(1);
        row.Hull.Should().BeApproximately(100.0f, 0.01f);
        row.InvulnFlag.Should().Be(0);
        row.PreventDeath.Should().Be(0);
        row.IsLocal.Should().BeTrue();
        row.IsSelected.Should().BeTrue();
        row.ObjAddrHex.Should().Be("0x1000");
    }

    [Fact]
    public void Parse_Row1_DecodesEnemyAndFlagBytes()
    {
        var row = TacticalUnitListParser.Parse(ThreeRowPayload).Rows[1];
        row.ObjAddr.Should().Be(8192L);
        row.OwnerSlot.Should().Be(0);
        row.InvulnFlag.Should().Be(1, "invuln flag byte populated on row 1");
        row.PreventDeath.Should().Be(128, "prevent_death uses bit 0x80");
        row.IsLocal.Should().BeFalse();
        row.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void Parse_MalformedRow_IsCountedAndSkipped()
    {
        // Row 2 has only 5 fields, should be skipped but not crash.
        var payload = "count=2|4096;1;100.0;0;0;1;1|not;enough;fields";
        var result = TacticalUnitListParser.Parse(payload);
        result.Rows.Should().HaveCount(1);
        result.MalformedRowCount.Should().Be(1);
    }

    [Fact]
    public void Parse_ZeroObjAddr_IsRejectedAsMalformed()
    {
        // obj_addr=0 is a sentinel meaning "no unit" — never valid.
        var payload = "count=1|0;1;100;0;0;1;0";
        var result = TacticalUnitListParser.Parse(payload);
        result.Rows.Should().BeEmpty();
        result.MalformedRowCount.Should().Be(1);
    }

    [Fact]
    public void Parse_NonNumericHull_IsRejected()
    {
        var payload = "count=1|4096;1;banana;0;0;1;0";
        var result = TacticalUnitListParser.Parse(payload);
        result.Rows.Should().BeEmpty();
        result.MalformedRowCount.Should().Be(1);
    }

    [Fact]
    public void Parse_MissingCountHeader_ReturnsEmpty()
    {
        var result = TacticalUnitListParser.Parse("4096;1;100;0;0;1;0");
        result.Rows.Should().BeEmpty();
    }

    // ─── Selection model ────────────────────────────────────────

    [Fact]
    public void LoadRows_ReturnsLoadedCount()
    {
        var sel = new TacticalUnitSelection();
        var rows = TacticalUnitListParser.Parse(ThreeRowPayload).Rows;
        sel.LoadRows(rows).Should().Be(3);
        sel.Rows.Should().HaveCount(3);
    }

    [Fact]
    public void SelectedRows_FiltersOnlySelected()
    {
        var sel = new TacticalUnitSelection();
        sel.LoadRows(TacticalUnitListParser.Parse(ThreeRowPayload).Rows);
        sel.SelectedRows.Should().HaveCount(1);
        sel.SelectedRows[0].ObjAddr.Should().Be(4096L);
    }

    [Fact]
    public void WritableSelectedRows_RejectsEnemyEvenWhenSelected()
    {
        var sel = new TacticalUnitSelection();
        // Select both a local and an enemy row.
        var rows = TacticalUnitListParser.Parse(
            "count=2|4096;1;100;0;0;1;1|8192;0;200;0;0;0;1").Rows;
        sel.LoadRows(rows);

        sel.SelectedRows.Should().HaveCount(2, "both rows are selected");
        sel.WritableSelectedRows.Should().HaveCount(1, "enemy is filtered out");
        sel.WritableSelectedRows[0].ObjAddr.Should().Be(4096L);
    }

    [Fact]
    public void ApplySelection_ReplacesSelectedFlags()
    {
        var sel = new TacticalUnitSelection();
        sel.LoadRows(TacticalUnitListParser.Parse(ThreeRowPayload).Rows);

        // Select rows 1 and 2 (addrs 8192 + 12288), deselect row 0.
        var count = sel.ApplySelection(new HashSet<long> { 8192L, 12288L });
        count.Should().Be(2);
        sel.Rows[0].IsSelected.Should().BeFalse();
        sel.Rows[1].IsSelected.Should().BeTrue();
        sel.Rows[2].IsSelected.Should().BeTrue();
    }

    [Fact]
    public void ApplySelection_EmptySet_DeselectsEverything()
    {
        var sel = new TacticalUnitSelection();
        sel.LoadRows(TacticalUnitListParser.Parse(ThreeRowPayload).Rows);
        sel.ApplySelection(new HashSet<long>()).Should().Be(0);
        sel.SelectedRows.Should().BeEmpty();
    }

    [Fact]
    public void ClearSelection_ReturnsPriorSelectedCount()
    {
        var sel = new TacticalUnitSelection();
        sel.LoadRows(TacticalUnitListParser.Parse(ThreeRowPayload).Rows);
        sel.ClearSelection().Should().Be(1, "one row was pre-selected in the fixture");
        sel.SelectedRows.Should().BeEmpty();
    }

    [Fact]
    public void ClearSelection_OnEmpty_ReturnsZero()
    {
        var sel = new TacticalUnitSelection();
        sel.ClearSelection().Should().Be(0);
    }

    [Fact]
    public void ClassifyBulk_CountsByCategory()
    {
        var sel = new TacticalUnitSelection();
        var rows = TacticalUnitListParser.Parse(
            "count=4" +
            "|100;1;10;0;0;1;1" +   // local + selected → writable
            "|200;1;10;0;0;1;1" +   // local + selected → writable
            "|300;0;10;0;0;0;1" +   // enemy + selected → skipped
            "|400;1;10;0;0;1;0"     // local + unselected → unselected
        ).Rows;
        sel.LoadRows(rows);

        var summary = sel.ClassifyBulk();
        summary.WritableSelected.Should().Be(2);
        summary.EnemySelectedSkipped.Should().Be(1);
        summary.Unselected.Should().Be(1);
    }

    [Fact]
    public void ObjAddrHex_MatchesLowercasePrefixedUppercaseHex()
    {
        var row = new TacticalUnitRow(0xDEADBEEFL, 1, 10f, 0, 0, true, true);
        row.ObjAddrHex.Should().Be("0xDEADBEEF");
    }
}
