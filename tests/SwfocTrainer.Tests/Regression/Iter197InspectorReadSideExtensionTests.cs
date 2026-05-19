using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 197) — pins Inspector tab read-side EXTENSION for iter
/// 171/172 unit-receiver wires (GetParentObject, GetAttackTarget,
/// GetDamageModifier, GetContainedObjectCount, GetBehaviorId,
/// GetRateOfFireModifier). NOT a new tab — extends the iter-191 GroupBox
/// from 4 buttons to 10 buttons. All 6 reuse the existing UnitLuaExpr field.
///
/// All 6 wires are no-arg unit getters via V2UnitMutationDispatcher and the
/// iter-167 unit-getter helper (`BuildUnitLuaNoArgCall`). Same pattern as
/// iter-191; no signature differences.
/// </summary>
public sealed class Iter197InspectorReadSideExtensionTests
{
    [Fact]
    public void DispatcherMethods_BindToCorrectSwfocNames()
    {
        var t = typeof(V2UnitMutationDispatcher);
        t.GetMethod(nameof(V2UnitMutationDispatcher.GetParentObjectLuaAsync))
            .Should().NotBeNull("Inspector tab Parent obj button binds to GetParentObjectLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.GetAttackTargetLuaAsync))
            .Should().NotBeNull("Inspector tab Attack target button binds to GetAttackTargetLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.GetDamageModifierLuaAsync))
            .Should().NotBeNull("Inspector tab Damage mod button binds to GetDamageModifierLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.GetContainedObjectCountLuaAsync))
            .Should().NotBeNull("Inspector tab Contained count button binds to GetContainedObjectCountLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.GetBehaviorIdLuaAsync))
            .Should().NotBeNull("Inspector tab Behavior id button binds to GetBehaviorIdLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.GetRateOfFireModifierLuaAsync))
            .Should().NotBeNull("Inspector tab RoF mod button binds to GetRateOfFireModifierLuaAsync");
    }

    [Fact]
    public void CatalogAction_AllSixEntriesAreLive()
    {
        var swfocNames = new[]
        {
            "SWFOC_GetParentObjectLua",
            "SWFOC_GetAttackTargetLua",
            "SWFOC_GetDamageModifierLua",
            "SWFOC_GetContainedObjectCountLua",
            "SWFOC_GetBehaviorIdLua",
            "SWFOC_GetRateOfFireModifierLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-197 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_DocumentsIter197Surfacing()
    {
        // Pin: iter-197 added native UX surfacing language to all 6 entries.
        var parentObj = CapabilityStatusCatalog.Entries["SWFOC_GetParentObjectLua"].Note;
        var attackTarget = CapabilityStatusCatalog.Entries["SWFOC_GetAttackTargetLua"].Note;
        var dmgMod = CapabilityStatusCatalog.Entries["SWFOC_GetDamageModifierLua"].Note;
        var containedCount = CapabilityStatusCatalog.Entries["SWFOC_GetContainedObjectCountLua"].Note;
        var behaviorId = CapabilityStatusCatalog.Entries["SWFOC_GetBehaviorIdLua"].Note;
        var rofMod = CapabilityStatusCatalog.Entries["SWFOC_GetRateOfFireModifierLua"].Note;

        parentObj.Should().Contain("Iter 197");
        attackTarget.Should().Contain("Iter 197");
        dmgMod.Should().Contain("Iter 197");
        containedCount.Should().Contain("Iter 197");
        behaviorId.Should().Contain("Iter 197");
        rofMod.Should().Contain("Iter 197");
    }

    [Fact]
    public void CatalogRationale_DocumentsCrossTabComposition()
    {
        // Pin: 3 of the 6 entries form read-after-write or count-companion
        // composition pairs with buttons on OTHER tabs. The catalog rationale
        // must mention the cross-tab pairing so operators understand the
        // workflow.
        var dmgMod = CapabilityStatusCatalog.Entries["SWFOC_GetDamageModifierLua"].Note;
        var rofMod = CapabilityStatusCatalog.Entries["SWFOC_GetRateOfFireModifierLua"].Note;
        var containedCount = CapabilityStatusCatalog.Entries["SWFOC_GetContainedObjectCountLua"].Note;

        dmgMod.Should().Contain("Combat tab");
        rofMod.Should().Contain("Combat tab");
        containedCount.Should().Contain("UnitControl");
    }

    [Fact]
    public void CatalogRationale_ReferencesIter167Helper()
    {
        // Pin: all 6 wires use the iter-167 unit-getter helper. Catalog
        // rationale must reference it so future readers see the helper-
        // introduction provenance — same pattern as iter-188/191/197 surfacing.
        var swfocNames = new[]
        {
            "SWFOC_GetParentObjectLua",
            "SWFOC_GetAttackTargetLua",
            "SWFOC_GetDamageModifierLua",
            "SWFOC_GetContainedObjectCountLua",
            "SWFOC_GetBehaviorIdLua",
            "SWFOC_GetRateOfFireModifierLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("iter-167",
                    $"{name} must reference the iter-167 helper for provenance");
        }
    }
}
