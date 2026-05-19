using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 212) — pins the iter-157 unit-method MEGA-batch LIVE
/// surfacing on the UnitControl tab. 6 wires across 4 dispatcher patterns:
///   - Set_In_Limbo + Set_Check_Contested_Space: 1-arg bool via iter-111 helper
///     (iter-204 hardcoded-bool on/off pair pattern)
///   - Sell: no-arg via iter-112 helper
///   - Bribe + Move_To + Fire_Special_Weapon: 1-arg via iter-154 helper
///
/// Field-reuse principle pushed further:
///   - Bribe reuses iter-118 TargetPlayerLuaExpr (target player)
///   - Move_To reuses iter-194 TargetForCombatOrderLuaExpr (position handle,
///     semantically interchangeable with iter-163 Divert's "where to go")
///   - Fire_Special_Weapon needs NEW SpecialWeaponSlotLuaExpr field
///
/// Operator workflow: 8 buttons surfacing 6 wires (2 wires get on/off pairs)
/// in a single iter, layered onto iter-117 SelectedUnitLuaExpr anchor.
/// </summary>
public sealed class Iter212UnitControlMegaBatchTests
{
    [Fact]
    public void CatalogEntries_AllSixRemainLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_SetInLimboLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_SetCheckContestedSpaceLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_SellUnitLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_BribeLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_MoveToLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_FireSpecialWeaponLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_AllSixEntriesDocumentIter212Surfacing()
    {
        var entries = new[]
        {
            "SWFOC_SetInLimboLua",
            "SWFOC_SetCheckContestedSpaceLua",
            "SWFOC_SellUnitLua",
            "SWFOC_BribeLua",
            "SWFOC_MoveToLua",
            "SWFOC_FireSpecialWeaponLua"
        };

        foreach (var key in entries)
        {
            var note = CapabilityStatusCatalog.Entries[key].Note;
            note.Should().Contain("Iter 212", $"{key} should be marked iter-212 surfaced");
            note.Should().Contain("UnitControl", $"{key} should reference UnitControl tab");
        }
    }

    [Fact]
    public void CatalogRationale_FieldReuseDocumentedForBribeAndMoveTo()
    {
        // Pin: Bribe reuses iter-118 TargetPlayerLuaExpr, Move_To reuses
        // iter-194 TargetForCombatOrderLuaExpr. The cross-iter field-reuse
        // is the iter-212 design choice — pin it so a future "rationale
        // cleanup" doesn't drop the lineage.
        var bribe = CapabilityStatusCatalog.Entries["SWFOC_BribeLua"].Note;
        var moveTo = CapabilityStatusCatalog.Entries["SWFOC_MoveToLua"].Note;
        bribe.Should().Contain("iter-118");
        bribe.Should().Contain("TargetPlayerLuaExpr");
        moveTo.Should().Contain("iter-194");
        moveTo.Should().Contain("TargetForCombatOrderLuaExpr");
    }

    [Fact]
    public void CatalogRationale_BoolPairsReferenceIter204OnOffPattern()
    {
        // Pin: Set_In_Limbo + Set_Check_Contested_Space use iter-204
        // hardcoded-bool on/off pattern. Lineage now spans iter-204 →
        // iter-208 → iter-211 → iter-212.
        var limbo = CapabilityStatusCatalog.Entries["SWFOC_SetInLimboLua"].Note;
        var contested = CapabilityStatusCatalog.Entries["SWFOC_SetCheckContestedSpaceLua"].Note;
        limbo.Should().Contain("iter-204");
        limbo.Should().Contain("on/off");
        contested.Should().Contain("iter-204");
        contested.Should().Contain("on/off");
    }

    [Fact]
    public void Vm_ExposesAllEightCommandsCapabilityActionsAndSlotField()
    {
        // Pin: 8 ICommands (2 limbo + 2 contested + Sell + Bribe + MoveTo +
        // FireSpecialWeapon) + 8 capability actions + new SpecialWeaponSlotLuaExpr
        // property. Reflection walk so we don't depend on the VM constructor.
        var t = typeof(SwfocTrainer.App.V2.ViewModels.UnitControlTabViewModel);
        t.GetProperty("SetInLimboOnLuaCommand").Should().NotBeNull();
        t.GetProperty("SetInLimboOffLuaCommand").Should().NotBeNull();
        t.GetProperty("SetCheckContestedSpaceOnLuaCommand").Should().NotBeNull();
        t.GetProperty("SetCheckContestedSpaceOffLuaCommand").Should().NotBeNull();
        t.GetProperty("SellUnitLuaCommand").Should().NotBeNull();
        t.GetProperty("BribeLuaCommand").Should().NotBeNull();
        t.GetProperty("MoveToLuaCommand").Should().NotBeNull();
        t.GetProperty("FireSpecialWeaponLuaCommand").Should().NotBeNull();

        t.GetProperty("SetInLimboOnLuaAction").Should().NotBeNull();
        t.GetProperty("SetInLimboOffLuaAction").Should().NotBeNull();
        t.GetProperty("SetCheckContestedSpaceOnLuaAction").Should().NotBeNull();
        t.GetProperty("SetCheckContestedSpaceOffLuaAction").Should().NotBeNull();
        t.GetProperty("SellUnitLuaAction").Should().NotBeNull();
        t.GetProperty("BribeLuaAction").Should().NotBeNull();
        t.GetProperty("MoveToLuaAction").Should().NotBeNull();
        t.GetProperty("FireSpecialWeaponLuaAction").Should().NotBeNull();

        t.GetProperty("SpecialWeaponSlotLuaExpr")
            .Should().NotBeNull("Fire_Special_Weapon needs a dedicated slot field");
    }
}
