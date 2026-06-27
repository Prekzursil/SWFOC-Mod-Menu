using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 214) — pins the iter-174 cross-receiver arg-getter LIVE
/// surfacing on the Inspector tab. 4 wires across 3 receiver types via
/// existing iter-173 helper (shape-agnostic):
///   - Get_Bone_Position: unit + bone-name → unit receiver
///   - Contains_Object_Type: unit + child-type → unit receiver
///   - Get_Space_Station_Level: player + planet → PLAYER receiver
///   - Get_Type_Of_Unit: TaskForce + idx → TASKFORCE receiver (first TaskForce wire)
///
/// Operator workflow: 4 buttons all reuse iter-198 UnitLuaExpr + UnitArgExpr
/// field pair. Operator types receiver Lua expression (unit/player/TaskForce)
/// into UnitLuaExpr — field name reflects iter-198 history but helper accepts
/// any receiver type. Catalog rationales document the receiver type per wire.
/// </summary>
public sealed class Iter214InspectorCrossReceiverArgGetterTests
{
    [Fact]
    public void CatalogEntries_AllFourRemainLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_GetBonePositionLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_ContainsObjectTypeLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_GetSpaceStationLevelLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_GetTypeOfUnitLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_AllFourEntriesDocumentIter214Surfacing()
    {
        var entries = new[]
        {
            "SWFOC_GetBonePositionLua",
            "SWFOC_ContainsObjectTypeLua",
            "SWFOC_GetSpaceStationLevelLua",
            "SWFOC_GetTypeOfUnitLua"
        };

        foreach (var key in entries)
        {
            var note = CapabilityStatusCatalog.Entries[key].Note;
            note.Should().Contain("Iter 214", $"{key} should be marked iter-214 surfaced");
            note.Should().Contain("Inspector", $"{key} should reference Inspector tab");
        }
    }

    [Fact]
    public void CatalogRationale_FieldReuseDocumentsIter198UnitLuaExprAndUnitArgExpr()
    {
        // Pin: all 4 wires reuse iter-198 UnitLuaExpr + UnitArgExpr field pair.
        // Field naming reflects iter-198 history (originally for unit-receiver
        // wires) but helper is shape-agnostic so accepts any receiver type.
        // Catalog rationale must document this for each wire.
        var entries = new[]
        {
            "SWFOC_GetBonePositionLua",
            "SWFOC_ContainsObjectTypeLua",
            "SWFOC_GetSpaceStationLevelLua",
            "SWFOC_GetTypeOfUnitLua"
        };
        foreach (var key in entries)
        {
            var note = CapabilityStatusCatalog.Entries[key].Note;
            note.Should().Contain("UnitLuaExpr", $"{key} should reference UnitLuaExpr field reuse");
            note.Should().Contain("UnitArgExpr", $"{key} should reference UnitArgExpr field reuse");
        }
    }

    [Fact]
    public void CatalogRationale_ReceiverTypesDocumentedPerWire()
    {
        // Pin: cross-receiver design is the iter-214 architectural finding.
        // Each of the 4 rationales must explicitly document which receiver
        // type the wire targets (unit/player/TaskForce) so operators know
        // what to type into UnitLuaExpr.
        var spaceStation = CapabilityStatusCatalog.Entries["SWFOC_GetSpaceStationLevelLua"].Note;
        var typeOfUnit = CapabilityStatusCatalog.Entries["SWFOC_GetTypeOfUnitLua"].Note;

        // Player receiver should be explicitly noted for Get_Space_Station_Level.
        spaceStation!.ToUpperInvariant().Should().Contain("PLAYER",
            "Get_Space_Station_Level uses PLAYER receiver — operator must type Find_Player(...) into UnitLuaExpr");

        // TaskForce receiver should be explicitly noted for Get_Type_Of_Unit.
        typeOfUnit!.ToUpperInvariant().Should().Contain("TASKFORCE",
            "Get_Type_Of_Unit uses TASKFORCE receiver — first TaskForce wire shipped via iter-173 helper");
    }

    [Fact]
    public void Vm_ExposesAllFourCommandsAndCapabilityActions()
    {
        // Pin: 4 ICommands + 4 capability actions on the Inspector VM public
        // surface. Reflection walk so we don't depend on the VM constructor.
        var t = typeof(SwfocTrainer.App.V2.ViewModels.InspectorTabViewModel);
        t.GetProperty("GetBonePositionLuaCommand").Should().NotBeNull();
        t.GetProperty("ContainsObjectTypeLuaCommand").Should().NotBeNull();
        t.GetProperty("GetSpaceStationLevelLuaCommand").Should().NotBeNull();
        t.GetProperty("GetTypeOfUnitLuaCommand").Should().NotBeNull();

        t.GetProperty("GetBonePositionLuaAction").Should().NotBeNull();
        t.GetProperty("ContainsObjectTypeLuaAction").Should().NotBeNull();
        t.GetProperty("GetSpaceStationLevelLuaAction").Should().NotBeNull();
        t.GetProperty("GetTypeOfUnitLuaAction").Should().NotBeNull();
    }
}
