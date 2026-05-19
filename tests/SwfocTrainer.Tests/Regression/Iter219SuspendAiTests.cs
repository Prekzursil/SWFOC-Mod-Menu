using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 219) — pins the Combat tab Suspend_AI cinematic helper
/// surfacing. Single-wire iter — iter-162 SWFOC_SuspendAiLua was the last
/// unsurfaced LIVE wire from the iter-216 changelog queue. Single numeric
/// seconds arg via existing iter-158 global-arg helper (regex-invisible
/// string-literal form via BuildUnitLuaNoArgCall — same shape as 1-arg
/// global wires).
///
/// Operator workflow this iter unlocks: full battle-pause cinematic recording
/// chain — Lock_Controls(true) (iter-208) → Suspend_AI(seconds) (iter-219) →
/// record cutscene with iter-145 cinematic camera quad + iter-150 letterbox +
/// iter-201 PlayMusic → Resume_Mode_Based_Music (iter-202) + Unlock_Controls
/// (iter-208). Every step now has a native button.
///
/// **Closes the iter-216 changelog "What's NOT yet surfaced" queue.** All
/// 7 candidates from iter-216's queue have been surfaced: iter-160
/// (DisableOrbitalBombardment, iter-217), iter-162 (SuspendAi, iter-219 —
/// this iter), iter-179 (TaskForceMoveToTarget, iter-218), iter-180 (Corrupt,
/// iter-218), iter-182 (GLOBAL Make_Ally/Make_Enemy, iter-217).
/// </summary>
public sealed class Iter219SuspendAiTests
{
    [Fact]
    public void CatalogEntry_RemainsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_SuspendAiLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_DocumentsIter219SurfacingAndCombatTab()
    {
        var note = CapabilityStatusCatalog.Entries["SWFOC_SuspendAiLua"].Note;
        note.Should().Contain("Iter 219");
        note.Should().Contain("Combat");
    }

    [Fact]
    public void CatalogRationale_DocumentsCinematicWorkflowChain()
    {
        // Pin: rationale must reference the iter-208 + iter-145 cinematic
        // workflow chain that Suspend_AI participates in. This documents
        // the operator-facing composition story so the button isn't an
        // orphan in the Combat tab — its purpose is cinematic-mode
        // recording where battle pause is needed.
        var note = CapabilityStatusCatalog.Entries["SWFOC_SuspendAiLua"].Note;
        note.Should().Contain("iter-208");
        note.Should().Contain("Lock_Controls");
        note.Should().Contain("iter-145");
        note.Should().Contain("cinematic");
    }

    [Fact]
    public void CatalogRationale_DocumentsClosingTheIter216Queue()
    {
        // Pin: this is the LAST unsurfaced wire from the iter-216 changelog
        // queue. The catalog rationale must document the milestone so future
        // readers understand the iter-216 list is now closed.
        var note = CapabilityStatusCatalog.Entries["SWFOC_SuspendAiLua"].Note;
        note.Should().Contain("iter-216");
        note!.ToLowerInvariant().Should().Contain("closes");
    }

    [Fact]
    public void Vm_ExposesCommandAndCapabilityActionAndSecondsField()
    {
        // Pin: Combat VM gets SuspendAiLuaCommand + SuspendAiLua action +
        // NEW SuspendAiSecondsLuaExpr field. Reflection walk so we don't
        // depend on the VM constructor (which has a real bridge dependency).
        var t = typeof(SwfocTrainer.App.V2.ViewModels.CombatTabViewModel);
        t.GetProperty("SuspendAiLuaCommand").Should().NotBeNull();
        t.GetProperty("SuspendAiLua").Should().NotBeNull();
        t.GetProperty("SuspendAiSecondsLuaExpr")
            .Should().NotBeNull("Suspend_AI needs a dedicated numeric seconds field");
    }
}
