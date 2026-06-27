using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 161) — pins player-method LIVE batch via existing
/// iter-154 generic 2-arg helper. LIVE flips #63-65; master loop now
/// at 65 LIVE wires.
/// Lock_Tech complements iter-155 Unlock_Tech. Make_Ally/Make_Enemy
/// are diplomacy primitives flagged in docs/lua-api.md as RESET on
/// every game-mode change — the catalog rationale must mention this.
/// </summary>
public sealed class Iter161PlayerMethodBatchTests
{
    [Theory]
    [InlineData("SWFOC_LockTechLua")]
    [InlineData("SWFOC_MakeAllyLua")]
    [InlineData("SWFOC_MakeEnemyLua")]
    public void PlayerMethodBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void LockTech_NoteContrastsWithIter155UnlockTech()
    {
        // Pin: catalog rationale should explicitly call out the
        // complementary relationship to iter-155 Unlock_Tech, so
        // future readers don't think they're duplicates.
        CapabilityStatusCatalog.Entries["SWFOC_LockTechLua"].Note
            .Should().Contain("Unlock_Tech");
    }

    [Theory]
    [InlineData("SWFOC_MakeAllyLua")]
    [InlineData("SWFOC_MakeEnemyLua")]
    public void Diplomacy_NoteFlagsResetCaveat(string entryName)
    {
        // Pin: catalog must warn about the reset-on-game-mode-change
        // behavioral quirk that docs/lua-api.md flagged. If a future
        // refactor strips the warning, this test fires.
        var note = CapabilityStatusCatalog.Entries[entryName].Note;
        // Match either "RESETS" (uppercase warning) or "resets"
        // (lowercase prose) — both forms preserve the caveat.
        var hasResetWarning = note!.Contains("RESET")
                           || note.Contains("reset");
        hasResetWarning.Should().BeTrue(
            $"{entryName} catalog note must flag the reset-on-game-mode-change caveat");
    }

    [Fact]
    public void PlayerMethodBatch_AllTaggedIter161()
    {
        var iter161Entries = new[]
        {
            "SWFOC_LockTechLua",
            "SWFOC_MakeAllyLua",
            "SWFOC_MakeEnemyLua",
        };
        foreach (var name in iter161Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 161 LIVE",
                    $"{name} should be tagged as iter 161 LIVE in catalog rationale");
        }
    }
}
