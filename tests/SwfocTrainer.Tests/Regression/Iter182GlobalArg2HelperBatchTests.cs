using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 182) — pins the iter-182 batch shipping the FIRST
/// multi-arg expansion beyond the receiver × arg × read/write matrix.
/// NEW Lua_DispatchGlobalArg2Method (10th helper) covers 2-arg globals.
///
/// Wires: SWFOC_GlobalMakeAllyLua / SWFOC_GlobalMakeEnemyLua — the
/// global-form alternatives to iter-161 obj-receiver Make_Ally/Make_Enemy.
///
/// LIVE flips #136-137; master loop now at 137 LIVE wires.
/// </summary>
public sealed class Iter182GlobalArg2HelperBatchTests
{
    [Theory]
    [InlineData("SWFOC_GlobalMakeAllyLua")]
    [InlineData("SWFOC_GlobalMakeEnemyLua")]
    public void GlobalArg2Batch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void GlobalMakeAlly_NotePinsAsFirstWireViaNewHelper()
    {
        // Pin: catalog rationale should call out that this is the FIRST
        // wire shipped via the NEW iter-182 global-2-arg helper, and
        // that the helper is the 10th (first multi-arg expansion).
        var note = CapabilityStatusCatalog.Entries["SWFOC_GlobalMakeAllyLua"].Note;
        note.Should().Contain("NEW iter-182");
        note.Should().Contain("10th helper");
        note.Should().Contain("multi-arg expansion");
    }

    [Fact]
    public void GlobalMakeAlly_NoteDistinguishesFromIter161ObjForm()
    {
        // Pin: iter-161 has the obj-receiver form (player):Make_Ally(other);
        // iter-182 has the global form Make_Ally(p1, p2). Catalog should
        // make the distinction explicit so operators know both exist.
        var note = CapabilityStatusCatalog.Entries["SWFOC_GlobalMakeAllyLua"].Note;
        note.Should().Contain("iter-161");
        note.Should().Contain("obj-receiver");
    }

    [Fact]
    public void GlobalMakeAlly_NotePinsModeChangeResetCaveat()
    {
        // Pin: docs flag that Make_Ally state resets on game-mode change.
        // Catalog must propagate that warning.
        var note = CapabilityStatusCatalog.Entries["SWFOC_GlobalMakeAllyLua"].Note;
        note.Should().Contain("RESETS");
        note.Should().Contain("Galactic");
    }

    [Fact]
    public void GlobalMakeEnemy_NotePinsRelationshipsToOtherIters()
    {
        // Pin: GlobalMakeEnemy should reference iter-161 (obj form),
        // iter-179 (IsEnemy/IsAlly read-side), AND iter-182 (this batch).
        var note = CapabilityStatusCatalog.Entries["SWFOC_GlobalMakeEnemyLua"].Note;
        note.Should().Contain("iter-161");
        note.Should().Contain("iter-179");
        note.Should().Contain("iter-182");
    }

    [Fact]
    public void GlobalArg2Batch_AllReuseIter182Helper()
    {
        var iter182Entries = new[]
        {
            "SWFOC_GlobalMakeAllyLua",
            "SWFOC_GlobalMakeEnemyLua",
        };
        foreach (var name in iter182Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("iter-182",
                    $"{name} should reference iter-182 global-2-arg helper");
        }
    }

    [Fact]
    public void GlobalArg2Batch_AllTaggedIter182Live()
    {
        var iter182Entries = new[]
        {
            "SWFOC_GlobalMakeAllyLua",
            "SWFOC_GlobalMakeEnemyLua",
        };
        foreach (var name in iter182Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 182 LIVE",
                    $"{name} should be tagged as iter 182 LIVE");
        }
    }
}
