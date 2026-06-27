using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 217) — pins the PlayerState tab final-extension batch
/// (iter-160 Disable_Orbital_Bombardment + iter-182 GLOBAL Make_Ally /
/// Make_Enemy) surfacing on the PlayerState tab. Disable_Orbital_Bombardment
/// is a player-method bool toggle surfaced as on/off pair (iter-204 hardcoded-
/// bool lineage now 7 iters deep: 204→208→211→212→213→215→217). Global form
/// Make_Ally/Make_Enemy are iter-182 alternatives to iter-209 obj-receiver
/// forms — both forms work; operator preference. All three reuse PlayerLuaExpr
/// (player1) + OtherPlayerLuaExpr (player2) — zero new fields.
///
/// Operator workflow this iter unlocks: complete PlayerWrapper LIVE-wire
/// surface in PlayerState tab. Operators can A/B test obj-receiver vs GLOBAL
/// diplomacy forms without re-typing args, and toggle per-player orbital
/// bombardment with a single click.
/// </summary>
public sealed class Iter217PlayerStateFinalExtensionTests
{
    [Fact]
    public void CatalogEntries_AllThreeRemainLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_DisableOrbitalBombardmentLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_GlobalMakeAllyLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_GlobalMakeEnemyLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_AllThreeEntriesDocumentIter217Surfacing()
    {
        var dob = CapabilityStatusCatalog.Entries["SWFOC_DisableOrbitalBombardmentLua"].Note;
        var ally = CapabilityStatusCatalog.Entries["SWFOC_GlobalMakeAllyLua"].Note;
        var enemy = CapabilityStatusCatalog.Entries["SWFOC_GlobalMakeEnemyLua"].Note;

        dob.Should().Contain("Iter 217");
        ally.Should().Contain("Iter 217");
        enemy.Should().Contain("Iter 217");

        // All three must mention PlayerState tab — the surfacing location.
        dob.Should().Contain("PlayerState");
        ally.Should().Contain("PlayerState");
        enemy.Should().Contain("PlayerState");
    }

    [Fact]
    public void CatalogRationale_DisableOrbitalBombardmentDocumentsIter204OnOffLineage()
    {
        // Pin: Disable_Orbital_Bombardment is the 7th iter in the on/off
        // lineage. Catalog rationale must explicitly document the chain so
        // future readers can grep for "iter-204" and find every wire that
        // adopted the hardcoded-bool pattern.
        var note = CapabilityStatusCatalog.Entries["SWFOC_DisableOrbitalBombardmentLua"].Note;
        note.Should().Contain("iter-204");
        note!.ToLowerInvariant().Should().Contain("on/off");

        // Lineage depth must be documented as "7 iters deep" (or contain
        // the chain 204→208→211→212→213→215→217). This is the self-
        // documenting cross-iter convention pin.
        note.Should().Contain("7 iters deep");
    }

    [Fact]
    public void CatalogRationale_GlobalFormsDocumentObjReceiverAlternativeAndModeReset()
    {
        // Pin: GlobalMakeAlly/GlobalMakeEnemy must reference iter-209
        // obj-receiver forms (the alternatives) AND preserve the
        // mode-change-reset caveat. Both casings ("RESETS" / "reset-on-game-
        // mode-change") satisfy the case-insensitive "reset" check.
        var ally = CapabilityStatusCatalog.Entries["SWFOC_GlobalMakeAllyLua"].Note;
        var enemy = CapabilityStatusCatalog.Entries["SWFOC_GlobalMakeEnemyLua"].Note;

        // Both must reference the iter-209 obj-receiver alternative.
        ally.Should().Contain("iter-209");
        enemy.Should().Contain("iter-209");

        // Both must reference field-reuse with PlayerLuaExpr + OtherPlayerLuaExpr.
        ally.Should().Contain("PlayerLuaExpr");
        ally.Should().Contain("OtherPlayerLuaExpr");

        // The mode-change reset warning must remain visible in both entries.
        ally!.ToLowerInvariant().Should().Contain("reset");
        enemy!.ToLowerInvariant().Should().Contain("reset");
    }

    [Fact]
    public void Vm_ExposesAllFourCommandsAndCapabilityActions()
    {
        // Pin: the four new ICommand + capability action pairs are on the
        // public surface. Reflection walk so we don't depend on the VM
        // constructor (which has a real bridge dependency).
        var t = typeof(SwfocTrainer.App.V2.ViewModels.PlayerStateTabViewModel);

        // 4 commands: 2 from iter-160 on/off pair + 2 iter-182 GLOBAL forms.
        t.GetProperty("DisableOrbitalBombardmentOnLuaCommand").Should().NotBeNull();
        t.GetProperty("DisableOrbitalBombardmentOffLuaCommand").Should().NotBeNull();
        t.GetProperty("GlobalMakeAllyLuaCommand").Should().NotBeNull();
        t.GetProperty("GlobalMakeEnemyLuaCommand").Should().NotBeNull();

        // 4 capability actions paired with the commands.
        t.GetProperty("DisableOrbitalBombardmentOnLuaAction").Should().NotBeNull();
        t.GetProperty("DisableOrbitalBombardmentOffLuaAction").Should().NotBeNull();
        t.GetProperty("GlobalMakeAllyLuaAction").Should().NotBeNull();
        t.GetProperty("GlobalMakeEnemyLuaAction").Should().NotBeNull();

        // Zero new fields — pure reuse of PlayerLuaExpr + OtherPlayerLuaExpr
        // already exposed by iter-189/199/209/210. Pin the absence of any
        // iter-217-specific input field by verifying the existing fields
        // are still on the surface (regression guard against accidental
        // field renames mid-iter).
        t.GetProperty("PlayerLuaExpr")
            .Should().NotBeNull("iter-189/199/209/210/217 anchor field must remain");
        t.GetProperty("OtherPlayerLuaExpr")
            .Should().NotBeNull("iter-199/209/217 player2 field must remain");
    }
}
