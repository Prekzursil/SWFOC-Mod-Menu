using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 210) — pins the iter-164 PlayerWrapper extension LIVE
/// batch (Enable_As_Actor / Release_Credits_For_Tactical / Select_Object)
/// surfacing on the PlayerState tab. Mixed-arity batch:
///   - Enable_As_Actor: no-arg via iter-112 helper (BuildUnitLuaNoArgCall)
///   - Release_Credits_For_Tactical: 2-arg via iter-154 helper (BuildUnitLuaMethodCall)
///   - Select_Object: 2-arg via iter-154 helper (BuildUnitLuaMethodCall)
///
/// Operator workflow this iter unlocks: cinematic-actor + galactic→tactical
/// economy + UI-level selection — all shared PlayerLuaExpr field with
/// iter-189/199/209 buttons. Two new dedicated fields for iter-210 args:
/// ReleaseCreditsAmount (numeric) + SelectObjectLuaExpr (object handle).
/// </summary>
public sealed class Iter210PlayerStateExtensionBatchTests
{
    [Fact]
    public void CatalogEntries_AllThreeRemainLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_EnableAsActorLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_ReleaseCreditsForTacticalLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_SelectObjectLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_AllThreeEntriesDocumentIter210Surfacing()
    {
        var actor = CapabilityStatusCatalog.Entries["SWFOC_EnableAsActorLua"].Note;
        var credits = CapabilityStatusCatalog.Entries["SWFOC_ReleaseCreditsForTacticalLua"].Note;
        var select = CapabilityStatusCatalog.Entries["SWFOC_SelectObjectLua"].Note;

        actor.Should().Contain("Iter 210");
        credits.Should().Contain("Iter 210");
        select.Should().Contain("Iter 210");

        // All three must mention PlayerState tab — the surfacing location.
        actor.Should().Contain("PlayerState");
        credits.Should().Contain("PlayerState");
        select.Should().Contain("PlayerState");
    }

    [Fact]
    public void CatalogRationale_EnableAsActorReferencesNoArgAndSharedPlayerField()
    {
        // Pin: Enable_As_Actor entry must reference its no-arg shape
        // (it's the only iter-210 wire that doesn't need a second field)
        // and the shared PlayerLuaExpr field framing.
        var note = CapabilityStatusCatalog.Entries["SWFOC_EnableAsActorLua"].Note;
        note.Should().Contain("no-arg");
        note.Should().Contain("PlayerLuaExpr");
    }

    [Fact]
    public void CatalogRationale_DedicatedFieldsForCreditsAndSelectObject()
    {
        // Pin: Release_Credits_For_Tactical and Select_Object each get
        // their own dedicated input field — pin both names so a future
        // "rationale cleanup" doesn't drop them.
        var credits = CapabilityStatusCatalog.Entries["SWFOC_ReleaseCreditsForTacticalLua"].Note;
        var select = CapabilityStatusCatalog.Entries["SWFOC_SelectObjectLua"].Note;
        credits.Should().Contain("ReleaseCreditsAmount");
        select.Should().Contain("SelectObjectLuaExpr");
    }

    [Fact]
    public void Vm_ExposesAllThreeCommandsCapabilityActionsAndTwoNewFields()
    {
        // Pin: the new ICommand + capability action triple is on the public
        // surface, plus the two new field properties (ReleaseCreditsAmount
        // + SelectObjectLuaExpr). Reflection walk so we don't depend on the
        // VM constructor (which has a real bridge dependency).
        var t = typeof(SwfocTrainer.App.V2.ViewModels.PlayerStateTabViewModel);
        t.GetProperty("EnableAsActorLuaCommand").Should().NotBeNull();
        t.GetProperty("ReleaseCreditsForTacticalLuaCommand").Should().NotBeNull();
        t.GetProperty("SelectObjectLuaCommand").Should().NotBeNull();

        t.GetProperty("EnableAsActorLuaAction").Should().NotBeNull();
        t.GetProperty("ReleaseCreditsForTacticalLuaAction").Should().NotBeNull();
        t.GetProperty("SelectObjectLuaAction").Should().NotBeNull();

        t.GetProperty("ReleaseCreditsAmount")
            .Should().NotBeNull("Release_Credits_For_Tactical needs a dedicated amount field");
        t.GetProperty("SelectObjectLuaExpr")
            .Should().NotBeNull("Select_Object needs a dedicated object-handle field");
    }
}
