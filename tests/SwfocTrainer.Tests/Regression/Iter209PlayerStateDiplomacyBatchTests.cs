using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 209) — pins the iter-161 player-method LIVE batch
/// (Lock_Tech / Make_Ally / Make_Enemy) surfacing on the PlayerState tab.
/// All three are 2-arg dispatcher calls via existing iter-154 generic
/// 2-arg helper (regex-invisible string-literal form via
/// BuildUnitLuaMethodCall). Zero new dispatcher helpers/builders.
///
/// Operator workflow this iter unlocks: bracket diplomacy probing+writing
/// — type other-player once into iter-199 OtherPlayerLuaExpr, ask
/// 'Is_Ally?' (read), then click 'Make ally' or 'Make enemy' (write)
/// without re-typing. Lock_Tech complements iter-155 Unlock_Tech via a
/// dedicated TechTypeLuaExpr field for the tech-name arg.
/// </summary>
public sealed class Iter209PlayerStateDiplomacyBatchTests
{
    [Fact]
    public void CatalogEntries_AllThreeRemainLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_LockTechLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_MakeAllyLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_MakeEnemyLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_AllThreeEntriesDocumentIter209Surfacing()
    {
        var lockTech = CapabilityStatusCatalog.Entries["SWFOC_LockTechLua"].Note;
        var makeAlly = CapabilityStatusCatalog.Entries["SWFOC_MakeAllyLua"].Note;
        var makeEnemy = CapabilityStatusCatalog.Entries["SWFOC_MakeEnemyLua"].Note;

        lockTech.Should().Contain("Iter 209");
        makeAlly.Should().Contain("Iter 209");
        makeEnemy.Should().Contain("Iter 209");

        // All three must mention PlayerState tab — the surfacing location.
        lockTech.Should().Contain("PlayerState");
        makeAlly.Should().Contain("PlayerState");
        makeEnemy.Should().Contain("PlayerState");
    }

    [Fact]
    public void CatalogRationale_LockTechReferencesTechFieldAndIter155Pairing()
    {
        // Pin: Lock_Tech entry must reference TechTypeLuaExpr (its own input
        // field, distinct from the iter-199 OtherPlayerLuaExpr) and the
        // iter-155 Unlock_Tech complement framing.
        var note = CapabilityStatusCatalog.Entries["SWFOC_LockTechLua"].Note;
        note.Should().Contain("TechTypeLuaExpr");
        note.Should().Contain("Unlock_Tech");
    }

    [Fact]
    public void CatalogRationale_MakeAllyAndMakeEnemyShareIter199OtherPlayerField()
    {
        // Pin: Make_Ally + Make_Enemy reuse the iter-199 OtherPlayerLuaExpr
        // input field. This shared-field framing is the "diplomacy probing
        // + writing without re-typing" workflow promise.
        var ally = CapabilityStatusCatalog.Entries["SWFOC_MakeAllyLua"].Note;
        var enemy = CapabilityStatusCatalog.Entries["SWFOC_MakeEnemyLua"].Note;
        ally.Should().Contain("OtherPlayerLuaExpr");
        enemy.Should().Contain("OtherPlayerLuaExpr");

        // The mode-change reset warning must remain visible in both entries.
        // Pinning this prevents a future "rationale cleanup" from dropping
        // the docs/lua-api.md section-6 caveat. Make_Ally uses "RESETS";
        // Make_Enemy uses "reset-on-game-mode-change" — both phrasings
        // satisfy the case-insensitive "reset" check.
        ally!.ToLowerInvariant().Should().Contain("reset");
        enemy!.ToLowerInvariant().Should().Contain("reset");
    }

    [Fact]
    public void Vm_ExposesAllThreeCommandsAndCapabilityActionsAndTechField()
    {
        // Pin: the new ICommand + capability action triple is on the public
        // surface, plus the new TechTypeLuaExpr property. Reflection walk so
        // we don't depend on the VM constructor (which has a real bridge
        // dependency).
        var t = typeof(SwfocTrainer.App.V2.ViewModels.PlayerStateTabViewModel);
        t.GetProperty("LockTechLuaCommand").Should().NotBeNull();
        t.GetProperty("MakeAllyLuaCommand").Should().NotBeNull();
        t.GetProperty("MakeEnemyLuaCommand").Should().NotBeNull();

        t.GetProperty("LockTechLuaAction").Should().NotBeNull();
        t.GetProperty("MakeAllyLuaAction").Should().NotBeNull();
        t.GetProperty("MakeEnemyLuaAction").Should().NotBeNull();

        t.GetProperty("TechTypeLuaExpr")
            .Should().NotBeNull("Lock_Tech needs a dedicated tech-name input field");
    }
}
