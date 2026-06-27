using System.IO;
using FluentAssertions;
using Xunit;

namespace SwfocTrainer.Tests.App.V2;

/// <summary>
/// 2026-04-27 (iter 35) — regression guard for the PHASE 2 PENDING amber
/// banners on Combat / Speed / Galactic / Hero Lab tabs. The banners are
/// the operator's main signal that those features are replay-mirror only;
/// a future refactor that drops them silently would mislead the operator
/// into trusting controls that don't actually move the needle in-game.
/// </summary>
/// <remarks>
/// <para>
/// We assert against the raw XAML string at test time so the test runs
/// without launching WPF — works in any test runner. The dispatcher header
/// comments list which commands are Phase-1 mirrors today; this test
/// asserts the corresponding tab has at least one "REPLAY MIRROR ONLY"
/// banner.
/// </para>
/// <para>
/// When a Phase-1 mirror gets promoted to truly live (e.g. the IDA pin
/// for the hero detection table lands and the hero commands route to the
/// real engine path), update both the dispatcher comment AND this test in
/// the same commit.
/// </para>
/// </remarks>
public sealed class PhaseTwoPendingBadgeAuditTests
{
    private static string ReadMainWindowXaml()
    {
        // Resolve the source-tree XAML, not a copy in bin/. The XAML is
        // copied into bin/ during build (BAML), but for this audit we want
        // the verbatim source so the assertion catches removal even before
        // a rebuild.
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir,
                "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("MainWindowV2.xaml not found in any parent directory.");
    }

    [Fact]
    public void CombatTab_HasReplayMirrorBadge()
    {
        var xaml = ReadMainWindowXaml();
        // Combat tab's banner — covers Damage/Shield/FireRate/TargetFilter.
        xaml.Should().Contain("REPLAY MIRROR ONLY",
            "the Combat scalar group must surface its PHASE 2 PENDING status");
        xaml.Should().Contain("not yet live in the running game",
            "the Combat banner's specific wording must remain");
    }

    [Fact]
    public void SpeedTab_PhaseTwoBannersRemovedAfterIter100Flips()
    {
        // 2026-04-28 (iter 100/102/107 update): per-faction and per-unit
        // speed flipped LIVE in iter 100 (SetSpeedOverride engine call).
        // Their amber MIRROR banners were replaced with green LIVE banners
        // in iter 102. PHASE 2 PENDING status for SetGameSpeed is now
        // surfaced through the per-button `{Binding SetGameSpeed.Badge}`
        // text, not a separate banner. This test now pins the negative —
        // the OBSOLETE banner strings must NOT come back, otherwise the
        // operator gets misled about features that ARE LIVE today.
        var xaml = ReadMainWindowXaml();
        xaml.Should().NotContain("per-faction speed isn't wired live yet",
            "iter 100 flipped per-faction speed LIVE; the obsolete amber "
            + "warning text must NOT reappear (would mislead the operator)");
        xaml.Should().NotContain("per-unit speed isn't wired live yet",
            "iter 100 flipped per-unit speed LIVE; the obsolete amber "
            + "warning text must NOT reappear");
        // Positive guard: the LIVE banner must remain. (Iter-N suffix was
        // stripped from operator-visible text during the final-product UI
        // polish per feedback_stale_groupbox_header_drift.md — operators
        // only need the LIVE marker + the verb that explains Apply/Revert.)
        xaml.Should().Contain("✓  LIVE  —  Apply calls SetSpeedOverride engine helper, Revert calls ClearSpeedOverride",
            "the green LIVE banner on the per-unit speed group must stay so the "
            + "operator sees the LIVE confirmation");
    }

    [Fact]
    public void GalacticTab_HasReplayMirrorBadgeOnPlanetMutationGroup()
    {
        var xaml = ReadMainWindowXaml();
        // Iter 35 addition, updated after SetDiplomacy promotion — owner
        // mutations remain Phase 2, while diplomacy is live.
        xaml.Should().Contain(
            "Change-owner, Convert, Pure-kick, and story-arrival spawn are PHASE 2 and disabled",
            "the Galactic Change-planet-owner group must surface its PHASE 2 PENDING status");
    }

    [Fact]
    public void GalacticTab_StoryArrivalGroup_HasReplayMirrorBadge()
    {
        var xaml = ReadMainWindowXaml();
        xaml.Should().Contain(
            "story-arrival spawn isn't wired to the campaign",
            "the Story-arrival spawn group must call out its Phase-1-mirror status");
    }

    [Fact]
    public void HeroLabTab_HasReplayMirrorBadgeOnHeroActions()
    {
        var xaml = ReadMainWindowXaml();
        xaml.Should().Contain(
            "Permadeath is PHASE 2 and disabled",
            "the Hero Lab actions group must surface its remaining PHASE 2 PENDING status");
    }

    [Fact]
    public void SpawningTab_HasReplayMirrorBadgeOnSpawnButton()
    {
        var xaml = ReadMainWindowXaml();
        // Iter 36 addition — Spawn is the most-used tab; per
        // BridgeSpawningDispatcher.cs:9 the live engine Spawn_Unit pin is
        // still pending. Banner must be visible above the Spawn button.
        // Iter 119 (2026-04-29) refined the wording to "This Spawn button
        // isn't yet wired" so operators see the iter 119 LIVE Lua
        // alternative below it; the assertion was narrowed accordingly.
        xaml.Should().Contain(
            "Spawn button isn't yet wired to the live engine Spawn_Unit",
            "the Spawning tab must surface its PHASE 2 PENDING status above the Spawn button");
    }

    [Fact]
    public void DirectorTab_HasReplayMirrorBadgeOnCameraWaypoints()
    {
        var xaml = ReadMainWindowXaml();
        // Iter 36 addition — SetCameraPos is Phase-1 per
        // BridgeDirectorDispatcher.cs:10. Global SetGameSpeed is also disabled.
        xaml.Should().Contain(
            "Camera waypoint navigation is PHASE 2 PENDING",
            "the Director tab's Add-waypoint group must surface its PHASE 2 PENDING status");
    }
}
