using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 166) — pins LIVE batch shipped via NEW
/// Lua_DispatchGlobalNoArgMethod helper (5th in dispatcher set).
/// LIVE flips #79-81; master loop now at 81 LIVE wires.
/// The new helper completes the 2x2 matrix of canonical Lua API call
/// shapes: (receiver: obj/global) × (args: 0/1).
/// </summary>
public sealed class Iter166NewHelperBatchTests
{
    [Theory]
    [InlineData("SWFOC_StopAllMusicLua")]
    [InlineData("SWFOC_ResumeModeBasedMusicLua")]
    [InlineData("SWFOC_ShowGuiObjectLua")]
    public void NewHelperBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void StopAllMusic_NotePinsAsFirstWireViaNewHelper()
    {
        // Pin: catalog rationale should explicitly call out that this
        // is the first wire shipped via the iter-166 helper, so future
        // readers can trace dispatcher provenance.
        CapabilityStatusCatalog.Entries["SWFOC_StopAllMusicLua"].Note
            .Should().Contain("NEW iter-166");
    }

    [Fact]
    public void ResumeModeBasedMusic_NoteContrastsWithStopAllMusic()
    {
        // Pin: pairing relationship with Stop_All_Music for cinematic
        // audio control workflow.
        CapabilityStatusCatalog.Entries["SWFOC_ResumeModeBasedMusicLua"].Note
            .Should().Contain("StopAllMusic");
    }

    [Fact]
    public void ShowGuiObject_NoteContrastsWithIter158HideGuiObject()
    {
        // Pin: counterpart relationship with iter-158 Hide_GUI_Object.
        CapabilityStatusCatalog.Entries["SWFOC_ShowGuiObjectLua"].Note
            .Should().Contain("Hide_GUI_Object");
    }

    [Fact]
    public void NewHelperBatch_AllTaggedIter166()
    {
        var iter166Entries = new[]
        {
            "SWFOC_StopAllMusicLua",
            "SWFOC_ResumeModeBasedMusicLua",
            "SWFOC_ShowGuiObjectLua",
        };
        foreach (var name in iter166Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 166 LIVE",
                    $"{name} should be tagged as iter 166 LIVE in catalog rationale");
        }
    }

    [Fact]
    public void DispatcherSet_AtLeastFiveHelpersDocumentedInCatalog()
    {
        // Pin: the catalog rationale for at least 5 entries should
        // reference the dispatcher genealogy (iter-111/112/154/158/166).
        // This is a structural invariant — if a future refactor
        // collapses helpers, the catalog rationale should reflect it.
        var iter111Entries = CapabilityStatusCatalog.Entries.Values
            .Where(e => e.Note!.Contains("iter-111")).ToList();
        var iter112Entries = CapabilityStatusCatalog.Entries.Values
            .Where(e => e.Note!.Contains("iter-112")).ToList();
        var iter154Entries = CapabilityStatusCatalog.Entries.Values
            .Where(e => e.Note!.Contains("iter-154")).ToList();
        var iter158Entries = CapabilityStatusCatalog.Entries.Values
            .Where(e => e.Note!.Contains("iter-158")).ToList();
        var iter166Entries = CapabilityStatusCatalog.Entries.Values
            .Where(e => e.Note!.Contains("iter-166")).ToList();

        iter111Entries.Should().NotBeEmpty("iter-111 obj-bool helper should still be referenced");
        iter112Entries.Should().NotBeEmpty("iter-112 obj-no-arg helper should still be referenced");
        iter154Entries.Should().NotBeEmpty("iter-154 generic 2-arg helper should still be referenced");
        iter158Entries.Should().NotBeEmpty("iter-158 global-arg helper should still be referenced");
        iter166Entries.Should().NotBeEmpty("iter-166 global-no-arg helper should still be referenced");
    }
}
