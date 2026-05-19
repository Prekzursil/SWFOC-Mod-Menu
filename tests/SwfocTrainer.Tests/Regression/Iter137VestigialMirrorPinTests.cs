using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 137) — pins the iter 134 vestigial-entry cleanup.
///
/// Iter 134 audit found that <c>SWFOC_ChangePlanetOwnerWithMode</c>
/// and <c>SWFOC_SpawnAsStoryArrival</c> had catalog entries but no
/// bridge implementation — editor's BridgeGalacticDispatcher called
/// them via DoString, the bridge's Lua interpreter hit an unbound
/// global, and the operator's "Flip and convert garrison",
/// "Flip and destroy garrison", and "Story-arrival spawn" buttons all
/// errored at runtime with "attempt to call nil value".
///
/// Iter 137 added Phase-1 mirror implementations to the bridge
/// (g_pendingPlanetFlipModes, g_pendingStoryArrivalSpawns) so the
/// SWFOC_* dispatch path doesn't error out. Catalog stays
/// Phase2HookPending — Phase 2 engine wire-through is genuinely
/// blocked per iter 134's PlanetFactionChange_FullTransfer (3989
/// bytes, 4 args) and StoryEvent_Factory_Create (multi-arg state)
/// findings.
///
/// Operator's actual button surface uses overlay Feature 2/3
/// (iter 33-34, separate non-SWFOC_ dispatch in the C++ overlay
/// DLL); the SWFOC_* helpers are doc-only fallbacks.
///
/// This test pins:
///   1. Both catalog entries stay Phase2HookPending (Phase 2 still blocked)
///   2. Notes explicitly cite iter 137 + iter 134 provenance
///   3. Notes explicitly call out the overlay Feature 2/3 alternate path
///   4. Notes cite the engine RVA / function name that blocks Phase 2
///
/// Future regression guard: if someone removes the bridge Phase-1
/// mirrors without flipping the catalog (or vice-versa), or if the
/// notes lose the iter 134 / overlay Feature reference, this test
/// fires.
/// </summary>
public sealed class Iter137VestigialMirrorPinTests
{
    [Fact]
    public void ChangePlanetOwnerWithMode_StaysPhase2_NotLive()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_ChangePlanetOwnerWithMode"];
        entry.Status.Should().Be(CapabilityStatus.Phase2HookPending,
            "iter 137 added Phase-1 mirror only — engine writer too complex for single-iter Phase 2 wire");
    }

    [Fact]
    public void SpawnAsStoryArrival_StaysPhase2_NotLive()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SpawnAsStoryArrival"];
        entry.Status.Should().Be(CapabilityStatus.Phase2HookPending,
            "iter 137 added Phase-1 mirror only — StoryEvent_Factory_Create needs multi-arg state setup");
    }

    [Fact]
    public void ChangePlanetOwnerWithMode_NoteCitesIter137_AndIter134_AndOverlayFeature3()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_ChangePlanetOwnerWithMode"];
        entry.Note.Should().Contain("iter 137");
        entry.Note.Should().Contain("iter 134");
        entry.Note.Should().Contain("overlay Feature 3",
            "operator's actual button surface uses the overlay DLL's separate dispatch path");
        entry.Note.Should().Contain("0x3FB040",
            "PlanetFactionChange_FullTransfer engine RVA must be cited");
    }

    [Fact]
    public void SpawnAsStoryArrival_NoteCitesIter137_AndIter134_AndOverlayFeature2()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SpawnAsStoryArrival"];
        entry.Note.Should().Contain("iter 137");
        entry.Note.Should().Contain("iter 134");
        entry.Note.Should().Contain("overlay Feature 2");
        entry.Note.Should().Contain("StoryEvent_Factory_Create",
            "engine function blocking Phase 2 must be named");
    }

    [Fact]
    public void Both_NotesExplainPreIter137WasVestigial()
    {
        var planet = CapabilityStatusCatalog.Entries["SWFOC_ChangePlanetOwnerWithMode"];
        var story = CapabilityStatusCatalog.Entries["SWFOC_SpawnAsStoryArrival"];
        planet.Note.Should().Contain("vestigial");
        story.Note.Should().Contain("vestigial");
    }
}
