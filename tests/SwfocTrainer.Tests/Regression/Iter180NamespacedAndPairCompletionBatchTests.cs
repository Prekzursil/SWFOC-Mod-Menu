using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-04 (iter 180) — pins the iter-180 batch demonstrating that:
///   1. iter-158 helper handles NAMESPACED method names (FOWManager.X) — the
///      `.` is just part of the Lua method-name lookup, not a syntactic
///      barrier the helper has to know about.
///   2. Pair-completion patterns: Unlock_Controls pairs with iter-160
///      LockControls; Corrupt (Underworld) pairs with iter-157 Bribe.
/// LIVE flips #130-133; master loop now at 133 LIVE wires.
/// </summary>
public sealed class Iter180NamespacedAndPairCompletionBatchTests
{
    [Theory]
    [InlineData("SWFOC_FOWRevealAllLua")]
    [InlineData("SWFOC_FOWUndoRevealAllLua")]
    [InlineData("SWFOC_UnlockControlsLua")]
    [InlineData("SWFOC_CorruptLua")]
    public void NamespacedAndPairCompletionBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void FOWRevealAll_NotePinsNamespaceAgnosticism()
    {
        // Pin: catalog should explicitly call out that FOWManager.X works
        // through the iter-158 helper without any helper changes — this
        // is the architectural finding worth pinning.
        var note = CapabilityStatusCatalog.Entries["SWFOC_FOWRevealAllLua"].Note;
        note.Should().Contain("namespace-agnostic");
        note.Should().Contain("iter-158");
    }

    [Fact]
    public void FOWRevealAll_NoteDocumentsFullyQualifiedName()
    {
        // Pin: rationale should show the actual Lua expression dispatched.
        CapabilityStatusCatalog.Entries["SWFOC_FOWRevealAllLua"].Note
            .Should().Contain("FOWManager.Reveal_All");
    }

    [Fact]
    public void UnlockControls_NotePinsPairWithIter160LockControls()
    {
        // Pin: this is a pair-completion wire and the catalog should make
        // the pairing explicit so operators discover both halves.
        var note = CapabilityStatusCatalog.Entries["SWFOC_UnlockControlsLua"].Note;
        note.Should().Contain("iter-160");
        note.Should().Contain("LockControls");
    }

    [Fact]
    public void Corrupt_NotePinsPairWithIter157Bribe()
    {
        // Pin: Underworld faction connection should be discoverable.
        var note = CapabilityStatusCatalog.Entries["SWFOC_CorruptLua"].Note;
        note.Should().Contain("Underworld");
        note.Should().Contain("Bribe");
    }

    [Fact]
    public void NamespacedAndPairCompletionBatch_AllTaggedIter180Live()
    {
        var iter180Entries = new[]
        {
            "SWFOC_FOWRevealAllLua",
            "SWFOC_FOWUndoRevealAllLua",
            "SWFOC_UnlockControlsLua",
            "SWFOC_CorruptLua",
        };
        foreach (var name in iter180Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 180 LIVE",
                    $"{name} should be tagged as iter 180 LIVE");
        }
    }
}
