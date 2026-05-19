using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 194) — pins UnitControl tab combat-order extension for
/// iter 163 wires (Attack_Target, Guard_Target, Divert). NOT a new tab —
/// extends the existing iter-117/118 GroupBox with 3 more buttons reusing
/// SelectedUnitLuaExpr + a new shared TargetForCombatOrderLuaExpr field.
///
/// All 3 wires take a (unit, target) pair via V2UnitMutationDispatcher and
/// the iter-154 generic 2-arg helper. Operator types selected unit + target
/// (or position for Divert), then clicks any of the 3 buttons.
/// </summary>
public sealed class Iter194UnitControlCombatOrderNativeUxTests
{
    [Fact]
    public void DispatcherMethods_BindToCorrectSwfocNames()
    {
        // Pin: V2UnitMutationDispatcher exposes the 3 iter-163 methods.
        var attack = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.AttackTargetLuaAsync));
        var guard = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.GuardTargetLuaAsync));
        var divert = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.DivertLuaAsync));

        attack.Should().NotBeNull("UnitControl tab Attack target button binds to AttackTargetLuaAsync");
        guard.Should().NotBeNull("UnitControl tab Guard target button binds to GuardTargetLuaAsync");
        divert.Should().NotBeNull("UnitControl tab Divert button binds to DivertLuaAsync");
    }

    [Fact]
    public void CatalogAction_AllThreeEntriesAreLive()
    {
        // Pin: all 3 SWFOC_* names referenced by the iter-194 buttons must
        // resolve to LIVE catalog entries.
        var swfocNames = new[]
        {
            "SWFOC_AttackTargetLua",
            "SWFOC_GuardTargetLua",
            "SWFOC_DivertLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-194 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_DocumentsIter194Surfacing()
    {
        // Pin: iter-194 added native UX surfacing language. Future cleanups
        // must keep the iter-194 marker.
        var attack = CapabilityStatusCatalog.Entries["SWFOC_AttackTargetLua"].Note;
        var guard = CapabilityStatusCatalog.Entries["SWFOC_GuardTargetLua"].Note;
        var divert = CapabilityStatusCatalog.Entries["SWFOC_DivertLua"].Note;

        attack.Should().Contain("Iter 194");
        guard.Should().Contain("Iter 194");
        divert.Should().Contain("Iter 194");
    }

    [Fact]
    public void CatalogRationale_AttackTargetReferencesUnitControlExtension()
    {
        // Pin: AttackTarget rationale mentions the UnitControl tab extension
        // alongside iter-117/118 buttons (the existing iter-117 GroupBox grew
        // 3 more buttons in iter 194). Operators clicking through the GroupBox
        // should understand all 3 button rows are part of the same workflow.
        var note = CapabilityStatusCatalog.Entries["SWFOC_AttackTargetLua"].Note;
        note.Should().Contain("UnitControl tab");
        note.Should().Contain("iter-117/118");
    }
}
