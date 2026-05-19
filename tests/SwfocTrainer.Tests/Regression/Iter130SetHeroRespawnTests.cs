using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 130) — SetHeroRespawn catalog drift caught.
/// <para>
/// The bridge's <c>Lua_SetHeroRespawn(seconds)</c> writes a float to
/// <c>g_base + RVA::DefaultHeroRespawnTime</c> (RVA <c>0xB169F0</c>,
/// matches <c>fact_global_default_hero_respawn_time</c> in
/// <c>verified_facts.json</c>) — that's a real LIVE wire. But the
/// catalog entry still said <c>Phase2HookPending</c> /
/// <c>BLOCKED-NO-RVA</c> until iter 130's re-audit caught it.
/// </para>
/// <para>
/// Iter 128 introduced the "fix tools, then re-audit deferred status"
/// pattern via the iter-124-fixed callgraph CLI. Iter 130 applied the
/// same pattern to A1.4 Hero respawn and caught this drift — the bridge
/// has been LIVE all along; the operator-facing catalog just lied.
/// </para>
/// </summary>
public sealed class Iter130SetHeroRespawnTests
{
    [Fact]
    public void SetHeroRespawn_IsLive_NotPhase2Pending()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetHeroRespawn"];
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 130 caught the catalog drift — bridge has been writing " +
            "to the global Default_Hero_Respawn_Time float at RVA 0xB169F0 " +
            "all along. Iter 105's deferred / iter 130's caught pattern.");
    }

    [Fact]
    public void SetHeroRespawn_NoteCallsOutGlobalOnlyScope()
    {
        // The catalog Note must clarify "GLOBAL only, doesn't reset
        // already-queued respawns" so operators don't expect per-hero
        // override semantics. Iter 128/129 SetUnitShield set the precedent
        // of putting the engine RVA in the Note for operator transparency.
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetHeroRespawn"];
        entry.Note.Should().Contain("0xB169F0",
            "operator tooltip should surface the engine RVA");
        entry.Note.Should().Contain("LIVE");
        entry.Note.Should().Contain("doesn't reset already-queued respawns",
            "scope note prevents operator confusion vs per-hero override");
    }

    [Fact]
    public void SetHeroRespawn_Badge_Reads_LIVE()
    {
        var badge = CapabilityStatusCatalog.ComposeBadge("SWFOC_SetHeroRespawn");
        badge.Should().Be("LIVE");
    }

    [Fact]
    public void SetHeroRespawnTimer_StillPhase2_NotConfusedWithGlobalForm()
    {
        // The PER-HERO `SWFOC_SetHeroRespawnTimer` (Lua_SetHeroRespawnTimer
        // at lua_bridge.cpp:4072) remains PHASE 2 PENDING — it's a
        // different surface that needs the per-hero respawn-timer table
        // RVA, which iter 130's audit confirmed is NOT in the verified
        // ledger. Don't conflate the two.
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetHeroRespawnTimer"];
        entry.Status.Should().Be(CapabilityStatus.Phase2HookPending);
    }
}
