using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 199) — pins PlayerState tab read-side extension for
/// iter-170 GetName + iter-179 Is_Enemy/Is_Ally wires. NOT a new tab —
/// extends iter-189 GroupBox from 3 → 6 buttons. New OtherPlayerLuaExpr
/// field for the iter-179 predicate args.
///
/// GetName is no-arg via iter-167 helper (shape-agnostic for player
/// receivers). Is_Enemy/Is_Ally take a 2nd player Lua expression via
/// iter-173 helper.
/// </summary>
public sealed class Iter199PlayerStateReadSideExtensionTests
{
    [Fact]
    public void DispatcherMethods_BindToCorrectSwfocNames()
    {
        var t = typeof(V2UnitMutationDispatcher);
        t.GetMethod(nameof(V2UnitMutationDispatcher.GetPlayerNameLuaAsync))
            .Should().NotBeNull("PlayerState tab Read name button binds to GetPlayerNameLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.IsEnemyLuaAsync))
            .Should().NotBeNull("PlayerState tab Is enemy of? button binds to IsEnemyLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.IsAllyLuaAsync))
            .Should().NotBeNull("PlayerState tab Is ally of? button binds to IsAllyLuaAsync");
    }

    [Fact]
    public void CatalogAction_AllThreeEntriesAreLive()
    {
        var swfocNames = new[]
        {
            "SWFOC_GetNameLua",
            "SWFOC_IsEnemyLua",
            "SWFOC_IsAllyLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-199 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_DocumentsIter199Surfacing()
    {
        var getName = CapabilityStatusCatalog.Entries["SWFOC_GetNameLua"].Note;
        var isEnemy = CapabilityStatusCatalog.Entries["SWFOC_IsEnemyLua"].Note;
        var isAlly = CapabilityStatusCatalog.Entries["SWFOC_IsAllyLua"].Note;

        getName.Should().Contain("Iter 199");
        isEnemy.Should().Contain("Iter 199");
        isAlly.Should().Contain("Iter 199");
    }

    [Fact]
    public void CatalogRationale_DiplomacyPredicatesReferenceModeResetCaveat()
    {
        // Pin: Is_Ally rationale must document the game-mode-change-reset
        // caveat — same caveat as the iter-161 Make_Ally writer. Operators
        // reading Is_Ally need to know the result is mode-bound and re-read
        // after Galactic↔Tactical transitions.
        var note = CapabilityStatusCatalog.Entries["SWFOC_IsAllyLua"].Note;
        note.Should().Contain("RESETS");
        note.Should().Contain("Galactic");
        note.Should().Contain("Tactical");
    }

    [Fact]
    public void CatalogRationale_IsEnemyDocumentsLocalPlayerComposition()
    {
        // Pin: Is_Enemy rationale must show the iter-178 GetLocalPlayer
        // composition workflow ("is THIS player my enemy?") so operators see
        // the Diagnostics tab → PlayerState tab cross-tab pairing.
        var note = CapabilityStatusCatalog.Entries["SWFOC_IsEnemyLua"].Note;
        note.Should().Contain("iter-178");
        note.Should().Contain("Get_Local_Player");
    }
}
