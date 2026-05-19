using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 135) — pins the HeroStatEdit catalog drift fix.
///
/// Bridge `Lua_HeroStatEdit` has had per-field LIVE branches since:
///   - hull → direct write to GameObj::HP (always LIVE)
///   - speed → SetSpeedOverride (LIVE iter 100)
///   - shield → SetFrontShield + SetRearShield (LIVE iter 129)
///   - respawn_ms → still Phase-1 mirror (g_pendingRespawnWrites)
///
/// But the catalog said "Phase 1 mirror — composes per-field setters"
/// — under-reporting 3/4 sub-fields as PHASE 2 PENDING.
///
/// This test pins:
///   1. Catalog status flipped to Live
///   2. Note enumerates the 3 LIVE sub-fields with their RVAs
///   3. Note explicitly calls out respawn_ms as Phase-1 mirror only
///   4. Composed badge reports LIVE
///
/// Future regression guard: if someone flips HeroStatEdit back to
/// Phase2HookPending without removing the per-field LIVE branches
/// from `Lua_HeroStatEdit`, this test fires and surfaces the drift.
/// </summary>
public sealed class Iter135HeroStatEditPartialLiveTests
{
    [Fact]
    public void HeroStatEdit_StatusIsLive()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_HeroStatEdit"];
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 135 catalog drift fix: 3/4 sub-fields LIVE through engine helpers (hull/shield/speed); only respawn_ms is Phase-1");
    }

    [Fact]
    public void HeroStatEdit_NoteEnumeratesEveryLiveSubfield()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_HeroStatEdit"];
        entry.Note.Should().Contain("hull");
        entry.Note.Should().Contain("shield");
        entry.Note.Should().Contain("speed");
        entry.Note.Should().Contain("LIVE");
    }

    [Fact]
    public void HeroStatEdit_NoteCitesShieldEngineRvas()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_HeroStatEdit"];
        entry.Note.Should().Contain("0x3A8630",
            "iter 129 SetFrontShield engine helper RVA must be cited so future RE auditors can re-verify");
        entry.Note.Should().Contain("0x3A91E0",
            "iter 129 SetRearShield engine helper RVA must be cited so future RE auditors can re-verify");
    }

    [Fact]
    public void HeroStatEdit_NoteCitesSpeedEngineRva()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_HeroStatEdit"];
        entry.Note.Should().Contain("0x3A8C90",
            "iter 100 SetSpeedOverride engine helper RVA must be cited");
    }

    [Fact]
    public void HeroStatEdit_NoteFlagsRespawnMsAsPhase1()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_HeroStatEdit"];
        entry.Note.Should().Contain("respawn_ms",
            "respawn_ms must be explicitly named so operator/auditor can see which sub-field is still Phase-1");
        entry.Note.Should().Contain("Phase-1",
            "respawn_ms's Phase-1 status must be plain in the catalog note");
    }

    [Fact]
    public void HeroStatEdit_ComposedBadgeReportsLive()
    {
        var badge = CapabilityStatusCatalog.ComposeBadge("SWFOC_HeroStatEdit");
        badge.Should().Be("LIVE",
            "single-action composed badge must follow the catalog flip");
    }

    [Fact]
    public void HeroStatEdit_ComposedBadge_ReportsMixedWhenPairedWithPhase2Action()
    {
        // Mixed-status composition: ComposeBadge returns "MIXED (m/n LIVE)"
        // when statuses differ across the helper set. With HeroStatEdit (now
        // LIVE) + ListHeroes (Phase2), we expect "MIXED (1/2 LIVE)".
        // This regression-guards both the catalog flip and ComposeBadge's
        // mixed-set contract — if either changes, the test fires.
        var badge = CapabilityStatusCatalog.ComposeBadge(
            "SWFOC_HeroStatEdit", "SWFOC_ListHeroes");
        badge.Should().Be("MIXED (1/2 LIVE)",
            "HeroStatEdit is LIVE iter 135; ListHeroes is still Phase-2; the tab-level badge must signal the mixed-status state to the operator");
    }
}
