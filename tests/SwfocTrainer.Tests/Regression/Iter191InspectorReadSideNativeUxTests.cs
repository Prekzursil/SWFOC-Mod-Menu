using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 191) — pins the Inspector tab native UX for the iter
/// 168/169 unit-receiver read-side wires (Get_Type, Get_Owner,
/// Has_Attack_Target, Are_Engines_Online). Fourth tab in the iter-188/189/190
/// surfacing arc, completing the read-side coverage across UnitControl /
/// PlayerState / Diagnostics / Inspector.
///
/// All 4 wires are no-arg unit getters; bridge dispatches via the iter-167
/// helper (Lua_DispatchUnitGetterNoArg) and returns the engine value as a
/// string. Operator types a unit Lua expression once (e.g.
/// Find_First_Object("Empire_AT_AT")) then clicks any of the 4 buttons.
/// Result lands in the Inspector's LastStatus border.
/// </summary>
public sealed class Iter191InspectorReadSideNativeUxTests
{
    [Fact]
    public void DispatcherMethods_BindToCorrectSwfocNames()
    {
        // Pin: V2UnitMutationDispatcher exposes the 4 read-side methods that
        // the InspectorTabViewModel commands invoke. A future rename or
        // signature change in the dispatcher fails the build (CS missing-
        // method) before this test even runs, but explicit assertion here
        // keeps the iter-191 surface contract visible.
        var typeMethod = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.GetTypeLuaAsync));
        var ownerMethod = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.GetOwnerLuaAsync));
        var hasAttackMethod = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.HasAttackTargetLuaAsync));
        var enginesMethod = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.AreEnginesOnlineLuaAsync));

        typeMethod.Should().NotBeNull("Inspector tab Read unit type button binds to GetTypeLuaAsync");
        ownerMethod.Should().NotBeNull("Inspector tab Read unit owner button binds to GetOwnerLuaAsync");
        hasAttackMethod.Should().NotBeNull("Inspector tab Has attack target button binds to HasAttackTargetLuaAsync");
        enginesMethod.Should().NotBeNull("Inspector tab Engines online button binds to AreEnginesOnlineLuaAsync");
    }

    [Fact]
    public void CatalogAction_AllFourEntriesAreLive()
    {
        // Pin: all 4 SWFOC_* names referenced by the iter-191 buttons must
        // resolve to LIVE catalog entries. If any of these flips to
        // Phase2HookPending or Unavailable, the operator-trust badge under
        // each button will show "PHASE 2 PENDING" — the test catches a
        // regression that would silently break the operator's mental model.
        var swfocNames = new[]
        {
            "SWFOC_GetTypeLua",
            "SWFOC_GetOwnerLua",
            "SWFOC_HasAttackTargetLua",
            "SWFOC_AreEnginesOnlineLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-191 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_ReferencesIter167Helper()
    {
        // Pin: iter-191's wires all use the iter-167 unit-getter helper
        // (Lua_DispatchUnitGetterNoArg). The catalog rationale must reference
        // iter-167 so future readers see the helper-introduction provenance.
        // This pin matches the iter-188 cross-iter pattern for read-side
        // surfacings.
        var getType = CapabilityStatusCatalog.Entries["SWFOC_GetTypeLua"].Note;
        var getOwner = CapabilityStatusCatalog.Entries["SWFOC_GetOwnerLua"].Note;
        getType.Should().Contain("iter-167");
        getOwner.Should().Contain("iter-167");
    }

    [Fact]
    public void CatalogRationale_DocumentsIter191InspectorSurface()
    {
        // Pin: iter-191 added native UX surfacing language to the catalog
        // rationale for all 4 wires. Future "consistency" cleanups must keep
        // the iter-191 marker — operators reading the surface report should
        // see "Inspector tab" wherever applicable so they know which tab
        // exposes the wire.
        var getType = CapabilityStatusCatalog.Entries["SWFOC_GetTypeLua"].Note;
        var getOwner = CapabilityStatusCatalog.Entries["SWFOC_GetOwnerLua"].Note;
        var hasAttack = CapabilityStatusCatalog.Entries["SWFOC_HasAttackTargetLua"].Note;
        var engines = CapabilityStatusCatalog.Entries["SWFOC_AreEnginesOnlineLua"].Note;

        getType.Should().Contain("Iter 191");
        getOwner.Should().Contain("Iter 191");
        hasAttack.Should().Contain("Iter 191");
        engines.Should().Contain("Iter 191");
    }
}
