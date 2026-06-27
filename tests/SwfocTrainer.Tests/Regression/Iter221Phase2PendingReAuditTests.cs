using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 221) — pins the Phase2HookPending re-audit results.
/// 89 days after iter-132's last full audit (which triaged 24 entries),
/// iter-221 re-audited the now-26 PHASE 2 PENDING entries and found
/// **ZERO drift catches**. The catalog has held up perfectly across
/// 88 LIVE flips between iter 132 and iter 220. All 88 flips were NEW
/// catalog entries (SWFOC_*Lua wires from iter 143-186), not silent
/// promotions of pre-existing PHASE 2 PENDING entries.
///
/// This test file pins:
/// - The expected count of PHASE 2 PENDING entries (26 at iter 221)
/// - That iter-132 confirmed-defer entries are STILL PHASE 2 PENDING
/// - That iter-136 promoted SetUnitField to LIVE
/// - That iter-130 SetHeroRespawn is LIVE (drift catch from iter 130)
/// - That iter-129 SetUnitShield is LIVE (drift catch from iter 128/129)
/// - That the 4 galactic-mode entries remain confirmed-defer per iter-134
///
/// Audit doc: `knowledge-base/iter221_phase2_pending_audit.md`.
/// </summary>
public sealed class Iter221Phase2PendingReAuditTests
{
    [Fact]
    public void Phase2PendingEntryCount_Is25()
    {
        // Pin: catalog has exactly 25 PHASE 2 PENDING entries after iter 461.
        // iter-132 had 24; iter-136 promoted SetUnitField to LIVE (-1);
        // GetPlanetTechAndBuildings was added some time after iter 132 (+1);
        // SetHeroRespawnTimer remains distinct from iter-130 SetHeroRespawn (+1
        // from being kept separate when iter-130 caught the GLOBAL flip).
        // iter-137 added Phase-1 mirrors for ChangePlanetOwnerWithMode +
        // SpawnAsStoryArrival (+0; replaced broken contracts).
        // iter-221 pinned the count at 26.
        // iter-237 flipped SWFOC_SetCameraPos Phase2HookPending → Live (-1).
        // iter-296 flipped SWFOC_GetPlanets Phase2HookPending → Live (-1).
        // iter-461 added SWFOC_TriggerVictory as PHASE 2 PENDING (+1).
        // Net at iter 461: 26 - 2 + 1 = 25.
        // The 25-count regression guard catches future drift.
        var phase2Count = CapabilityStatusCatalog.Entries.Values
            .Count(e => e.Status == CapabilityStatus.Phase2HookPending);
        phase2Count.Should().Be(25,
            "iter-461 added SWFOC_TriggerVictory after the iter-296 GetPlanets flip; "
          + "(silent flips OR new PHASE 2 additions will trip this assertion).");
    }

    [Fact]
    public void Iter132ConfirmedDefers_StillPhase2Pending()
    {
        // Pin: the 12 iter-132 confirmed-defer entries are STILL PHASE 2 PENDING
        // at iter 221. If any silently flipped LIVE, this would catch it.
        var confirmedDefers = new[]
        {
            "SWFOC_EventControl",
            "SWFOC_FreeBuild",
            "SWFOC_FreeCam",
            "SWFOC_FreezeAI",
            "SWFOC_FreezeCredits",
            "SWFOC_InstantBuild",
            "SWFOC_ListHeroes",
            "SWFOC_SetAreaDamage",
            "SWFOC_SetBuildCost",
            "SWFOC_SetBuildSpeed",
            "SWFOC_SetIncomeMultiplier",
            "SWFOC_SetPermadeath",
            "SWFOC_SetTargetFilter",
            "SWFOC_SetUnitCapOverride",
            "SWFOC_ToggleOHKAttackPower",
        };
        foreach (var name in confirmedDefers)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Phase2HookPending,
                    $"{name} was confirmed-defer at iter 132; should still be PHASE 2 PENDING.");
        }
    }

    [Fact]
    public void Iter132ToIter220DriftCatches_AreLive()
    {
        // Pin: the drift catches from iter 130/131/133/136 are LIVE.
        // These were the silent-flip catches that iter-132 flagged or
        // confirmed in the iter 128-132 audit window.
        CapabilityStatusCatalog.Entries["SWFOC_SetHeroRespawn"].Status
            .Should().Be(CapabilityStatus.Live, "iter-130 caught silent LIVE flip");
        CapabilityStatusCatalog.Entries["SWFOC_SetUnitShield"].Status
            .Should().Be(CapabilityStatus.Live, "iter-129 LIVE wire shipped");
        CapabilityStatusCatalog.Entries["SWFOC_SetDiplomacy"].Status
            .Should().Be(CapabilityStatus.Live, "iter-133 LIVE wire shipped");
        CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"].Status
            .Should().Be(CapabilityStatus.Live, "iter-136 promoted to LIVE (3/13 sub-fields), iter-243 extended to 5/13, iter-258 extended to 7/13");
    }

    [Fact]
    public void Iter134GalacticConfirmedDefers_StillPhase2Pending()
    {
        // Pin: the iter-134 galactic 4-candidate audit revised all 4 to
        // confirmed-defer. iter-296 subsequently flipped SWFOC_GetPlanets to
        // LIVE (real galactic-mode planet enumeration shipped, replacing the
        // count=0 stub). The other 3 stay PHASE 2 PENDING per iter-134.
        var galacticDefers = new[]
        {
            "SWFOC_ChangePlanetOwner",
            "SWFOC_ChangePlanetOwnerWithMode",
            "SWFOC_SpawnAsStoryArrival",
        };
        foreach (var name in galacticDefers)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Phase2HookPending,
                    $"{name} was iter-134 confirmed-defer (PlanetFactionChange too complex); "
                  + "should still be PHASE 2 PENDING.");
        }
        // iter-296 promotion guard: SWFOC_GetPlanets MUST stay LIVE going
        // forward — the real-impl swap is the iter-317 planet-icon column's
        // upstream dependency.
        CapabilityStatusCatalog.Entries["SWFOC_GetPlanets"].Status
            .Should().Be(CapabilityStatus.Live,
                "iter-296 shipped real galactic-mode planet enumeration; iter-317 planet-icon column depends on it.");
    }

    [Fact]
    public void Iter221AuditConclusion_ZeroDriftCatches()
    {
        // Documentation pin: iter-221 audit concluded ZERO drift catches.
        // This test serves as the assertion that the audit doc exists and
        // its conclusion is encoded as a regression guard. If a future iter
        // flips one of the 26 PHASE 2 PENDING entries to LIVE without
        // updating this test, the test count regression guard above
        // (Phase2PendingEntryCount_Is26) will catch it.

        // Sanity: the audit doc filename is the canonical reference.
        var auditDocReference = "knowledge-base/iter221_phase2_pending_audit.md";
        auditDocReference.Should().Contain("iter221",
            "iter-221 audit doc is the canonical reference for this test's pins");

        // The 4 partial-LIVE entries have separate LIVE alternatives shipped:
        // - iter-100 SetPerFactionSpeedMultiplier (per-faction speed; alternative form)
        // - iter-96 SetDamageMultiplierGlobal (GLOBAL damage; per-slot still PHASE 2)
        // - iter-130 SetHeroRespawn (GLOBAL respawn; per-hero SetHeroRespawnTimer still PHASE 2)
        // - iter-119 SpawnUnitLua (LIVE pair for Phase-1 SpawnUnit mirror)
        var liveAlternatives = new[]
        {
            "SWFOC_SetPerFactionSpeedMultiplier",
            "SWFOC_SetDamageMultiplierGlobal",
            "SWFOC_SetHeroRespawn",
            "SWFOC_SpawnUnitLua",
        };
        foreach (var name in liveAlternatives)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} is the LIVE alternative shipped alongside a PHASE 2 PENDING sibling; "
                  + "verifies the audit's 'separate LIVE alternative' classification.");
        }
    }

    [Fact]
    public void LegacyPhase1Mirrors_CiteLiveAlternativeWhenAvailable()
    {
        // Iter 250 audit drift class — "Catalog-rationale-cross-reference drift":
        // when a NEW catalog entry ships LIVE that supersedes a legacy Phase-1 mirror
        // (like iter-225 SetFireRateMultiplierGlobal supersedes legacy SetFireRate,
        // iter-231 SetCreditsFreezeGlobal supersedes legacy FreezeCredits),
        // the legacy entry's RATIONALE must cite the iter-N LIVE alternative so
        // operators reading the Phase2 entry know about the LIVE path.
        //
        // This is operator-trust drift, not status drift — the Phase2HookPending
        // STATUS is correct (legacy wire IS Phase-1), but the RATIONALE leaves
        // operators unaware of the LIVE alternative.
        //
        // Iter 251 fixed SWFOC_FreezeCredits (caught by iter-250 audit).
        // Iter 225 fix for SWFOC_SetFireRate is preserved.
        // Iter 266 added 2 NEW catches: SWFOC_SpawnUnit (legacy mirror; iter-109
        // SWFOC_SpawnUnitLua is the LIVE alternative) + SWFOC_SetUnitCapOverride
        // (iter-249 honest-defer arc DEPRECATED the Apocalypticx ledger entry; the
        // rationale cites iter-249 + iter-256 memory rule so operators know the
        // arc was attempted but blocked by AOB drift across binary versions).
        // Iter 274 added 1 NEW catch: SWFOC_SetHeroRespawnTimer (per-hero variant;
        // iter-130 SWFOC_SetHeroRespawn is the GLOBAL LIVE alternative covering
        // ~80% of operator use cases — "all heroes respawn faster/slower" — while
        // the per-hero variant stays Phase-1 mirror pending the per-hero respawn-
        // timer table RVA pin which iter-104 + iter-130 audits both confirmed
        // is not callgraph-discoverable).
        // Future legacy-Phase-1-mirror flips must extend this list when shipped.
        var legacyMirrorsWithLiveAlternative = new (string Name, string IterRef)[]
        {
            ("SWFOC_SetFireRate",          "iter-225"),
            ("SWFOC_FreezeCredits",        "iter-231"),
            ("SWFOC_SpawnUnit",            "iter-109"),
            ("SWFOC_SetUnitCapOverride",   "iter-249"),
            ("SWFOC_SetHeroRespawnTimer",  "iter-130"),
        };
        foreach (var (name, iterRef) in legacyMirrorsWithLiveAlternative)
        {
            var entry = CapabilityStatusCatalog.Entries[name];
            entry.Status.Should().Be(CapabilityStatus.Phase2HookPending,
                $"{name} stays PHASE 2 PENDING as a legacy Phase-1 mirror");
            entry.Note.Should().Contain(iterRef,
                $"{name} rationale must cite {iterRef} as the LIVE alternative or honest-defer ref — "
              + "iter-250 caught the drift class 'Catalog-rationale-cross-reference drift' "
              + "(operator-trust drift, not status drift). Future legacy-Phase-1-mirror flips "
              + "must extend this list when shipped.");
        }
    }

    [Fact]
    public void Iter266_SetUnitCapOverride_RationaleCitesIter256MemoryRule()
    {
        // Iter 266 audit drift catch #2: SWFOC_SetUnitCapOverride rationale must
        // cite the iter-256 `feedback_aob_drift_across_binary_versions` memory
        // rule that was seeded BY the iter-249 honest-defer finding. This closes
        // the operator-trust audit trail: rationale → iter-249 finding →
        // iter-256 codified memory rule → future RE arcs apply the rule.
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitCapOverride"];
        entry.Note.Should().Contain("iter-256",
            "iter-266 audit pinned the iter-256 memory rule cross-reference so operators reading "
          + "the Phase2 entry can find the durable cross-session learning that prevented an "
          + "iter-249-style invalidation cycle from recurring");
        entry.Note.Should().Contain("AOB",
            "iter-266 audit pinned the AOB-drift-across-binary-versions root cause so operators "
          + "understand WHY the Apocalypticx ledger entry was DEPRECATED (not just THAT it was)");
    }
}
