using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 218) — pins the cross-tab single-wire batch covering
/// iter-180 SWFOC_CorruptLua (UnitControl) + iter-179 SWFOC_TaskForceMoveToTargetLua
/// (Galactic). Both 2-arg dispatchers via existing BuildUnitLuaMethodCall
/// (helper shape-agnostic — works for unit-method + TaskForce-method).
///
/// Operator workflow this iter unlocks:
/// - UnitControl Corrupt: Underworld signature ability A/B test against
///   iter-212 Bribe (both Underworld; Bribe takes ownership, Corrupt degrades).
/// - Galactic TaskForceMoveToTarget: TaskForce target-object move complement
///   to iter-215 TaskForceMoveTo (position-targeted).
/// </summary>
public sealed class Iter218CorruptAndTaskForceMoveToTargetTests
{
    [Fact]
    public void CatalogEntries_BothRemainLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_CorruptLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_TaskForceMoveToTargetLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_BothEntriesDocumentIter218Surfacing()
    {
        var corrupt = CapabilityStatusCatalog.Entries["SWFOC_CorruptLua"].Note;
        var moveToTarget = CapabilityStatusCatalog.Entries["SWFOC_TaskForceMoveToTargetLua"].Note;

        corrupt.Should().Contain("Iter 218");
        moveToTarget.Should().Contain("Iter 218");

        // Surfacing-location pins.
        corrupt.Should().Contain("UnitControl");
        moveToTarget.Should().Contain("Galactic");
    }

    [Fact]
    public void CatalogRationale_CorruptDocumentsBribePairAndAmountField()
    {
        // Pin: Corrupt rationale must reference iter-212 Bribe pair-completion
        // (both Underworld signature abilities) AND the iter-218
        // CorruptAmountLuaExpr field (only NEW field this iter).
        var note = CapabilityStatusCatalog.Entries["SWFOC_CorruptLua"].Note;
        note.Should().Contain("iter-212");
        note.Should().Contain("Bribe");
        note.Should().Contain("CorruptAmountLuaExpr");
        note.Should().Contain("Underworld");
    }

    [Fact]
    public void CatalogRationale_TaskForceMoveToTargetDocumentsIter215Distinction()
    {
        // Pin: Move_To_Target must reference iter-215 Move_To distinction
        // (target-object vs position) AND TaskForceLuaExpr/TaskForceTargetLuaExpr
        // field-reuse (zero new fields this iter on Galactic side).
        var note = CapabilityStatusCatalog.Entries["SWFOC_TaskForceMoveToTargetLua"].Note;
        note.Should().Contain("iter-215");
        note.Should().Contain("TaskForceLuaExpr");
        note.Should().Contain("TaskForceTargetLuaExpr");
    }

    [Fact]
    public void Vms_ExposeAllNewCommandsAndCapabilityActions()
    {
        // Pin: UnitControl gets CorruptLuaCommand + CorruptLuaAction +
        // CorruptAmountLuaExpr (NEW field). Galactic gets
        // TaskForceMoveToTargetLuaCommand + TaskForceMoveToTargetLuaAction
        // (no new fields — reuses iter-215 TaskForceLuaExpr + TaskForceTargetLuaExpr).
        // Reflection walk so we don't depend on VM constructors (which have
        // real bridge dependencies).
        var unitControl = typeof(SwfocTrainer.App.V2.ViewModels.UnitControlTabViewModel);
        unitControl.GetProperty("CorruptLuaCommand").Should().NotBeNull();
        unitControl.GetProperty("CorruptLuaAction").Should().NotBeNull();
        unitControl.GetProperty("CorruptAmountLuaExpr")
            .Should().NotBeNull("Corrupt needs a dedicated numeric amount field");

        var galactic = typeof(SwfocTrainer.App.V2.ViewModels.GalacticTabViewModel);
        galactic.GetProperty("TaskForceMoveToTargetLuaCommand").Should().NotBeNull();
        galactic.GetProperty("TaskForceMoveToTargetLuaAction").Should().NotBeNull();

        // Field-reuse regression guard — iter-215 TaskForce fields must remain.
        galactic.GetProperty("TaskForceLuaExpr")
            .Should().NotBeNull("iter-215/218 TaskForce anchor field must remain");
        galactic.GetProperty("TaskForceTargetLuaExpr")
            .Should().NotBeNull("iter-215/218 TaskForce secondary-arg field must remain");
    }
}
