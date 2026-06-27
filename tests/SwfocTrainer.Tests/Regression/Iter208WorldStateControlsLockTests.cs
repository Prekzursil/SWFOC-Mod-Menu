using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 208) — pins the iter-160 Lock_Controls(bool) + iter-180
/// Unlock_Controls() symmetric pair surfacing on the WorldState tab Story+Audio
/// GroupBox. Hardcoded bool args (iter-204 pattern: Lock-on emits "1", Lock-off
/// emits "0") + no-arg unlock via BuildGlobalLuaNoArgCall (iter-202 pattern).
///
/// Operator workflow this iter unlocks: bracket cinematic recording with
/// Lock_Controls(true) → cutscene action → Unlock_Controls(). Pairs with
/// iter-150 Letter_Box_On/Off, iter-145 cinematic camera quad, iter-207
/// Hide/Show GUI for full filming control without dropping into Lua Playground.
/// </summary>
public sealed class Iter208WorldStateControlsLockTests
{
    [Fact]
    public void CatalogEntries_BothRemainLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_LockControlsLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_UnlockControlsLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_BothEntriesDocumentIter208Surfacing()
    {
        var lockNote = CapabilityStatusCatalog.Entries["SWFOC_LockControlsLua"].Note;
        var unlockNote = CapabilityStatusCatalog.Entries["SWFOC_UnlockControlsLua"].Note;

        lockNote.Should().Contain("Iter 208");
        unlockNote.Should().Contain("Iter 208");

        // Both entries must mention WorldState tab — the surfacing location.
        lockNote.Should().Contain("WorldState");
        unlockNote.Should().Contain("WorldState");
    }

    [Fact]
    public void CatalogRationale_LockReferencesUnlockAndCinematicWorkflow()
    {
        // Pin: the symmetric-pair framing must be explicit. The Lock entry
        // must reference the Unlock counterpart so an operator reading just
        // the Lock rationale knows to expect the bracketing button.
        var lockNote = CapabilityStatusCatalog.Entries["SWFOC_LockControlsLua"].Note;
        lockNote.Should().Contain("UnlockControls");
        lockNote.Should().Contain("cinematic");
    }

    [Fact]
    public void CatalogRationale_UnlockReferencesLockSymmetricPairing()
    {
        // Pin: the Unlock entry must explicitly call out the symmetric pair
        // framing so the iter-208 surfacing arc is self-describing.
        var unlockNote = CapabilityStatusCatalog.Entries["SWFOC_UnlockControlsLua"].Note;
        unlockNote.Should().Contain("symmetric pair");
        unlockNote.Should().Contain("Lock");
    }

    [Fact]
    public void Vm_ExposesLockOnLockOffAndUnlockCommandsAndCapabilityActions()
    {
        // Pin: the new ICommand + capability action triple is on the public
        // surface. Reflection walk so we don't depend on the VM constructor
        // (which has a real bridge dependency).
        var t = typeof(SwfocTrainer.App.V2.ViewModels.WorldStateTabViewModel);
        t.GetProperty("LockControlsOnCommand").Should().NotBeNull();
        t.GetProperty("LockControlsOffCommand").Should().NotBeNull();
        t.GetProperty("UnlockControlsLuaCommand").Should().NotBeNull();

        t.GetProperty("LockControlsOn").Should().NotBeNull();
        t.GetProperty("LockControlsOff").Should().NotBeNull();
        t.GetProperty("UnlockControlsLua").Should().NotBeNull();
    }
}
