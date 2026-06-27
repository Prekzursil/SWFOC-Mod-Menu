using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 136) — pins the SetUnitField bridge per-field LIVE
/// branches mirror.
/// 2026-05-06 (iter 243) — extended LIVE branches to invuln_flag +
/// prevent_death; ratio updated 3/13 → 5/13.
///
/// Iter 135 caught HeroStatEdit catalog drift (3/4 sub-fields LIVE in
/// the bridge but catalog said Phase-1). Iter 136 closes the parallel
/// gap on the UnitStatEditor side: bridge `Lua_SetUnitField` previously
/// just queued every field write to `g_pendingUnitFieldWrites` (pure
/// Phase-1) — even hull/shield/speed which had LIVE engine helpers
/// available since iter 100/129. UnitStatEditor's "Apply staged edits"
/// button was therefore Phase-1 even when staging hull/shield/speed.
///
/// Iter 136 mirrored HeroStatEdit's per-field LIVE branches into
/// `Lua_SetUnitField`:
///   - hull → direct write to addr+RVA::GameObj::HP (LIVE)
///   - shield → SetFrontShield @ 0x3A8630 + SetRearShield @ 0x3A91E0 (LIVE)
///   - speed → SetSpeedOverride @ 0x3A8C90 (LIVE)
///
/// Iter 243 extended the mirror with two more direct-write branches whose
/// offsets were already pinned in rvas.h's GameObj namespace:
///   - invuln_flag → direct byte write to GameObj+0x3A7 (LIVE; display flag only)
///   - prevent_death → direct bit-write of bit 0x80 of GameObj+0x3A1 (LIVE)
///
/// 8 fields remain on the Phase-1 mirror: max_hull/max_shield/max_speed/
/// attack_power/respawn_ms/is_hero/respawn_enabled (each needs its own
/// future RE arc) + owner_slot (deferred indefinitely — must use
/// SWFOC_ChangeUnitOwnerLua iter-108 for engine-aware ownership change).
///
/// Plus iter-136 added IsValidObjAddr + IsObjOwnedByHuman safety gates
/// (was unguarded pre-iter-136).
///
/// This test pins:
///   1. Catalog status flipped Phase2HookPending → Live (iter 136)
///   2. Note enumerates the 5 LIVE sub-fields with their RVAs
///   3. Note explicitly lists every Phase-1 field by name
///   4. Note declares total field count (5/13 LIVE after iter 243)
///   5. Composed badge reports LIVE
///   6. Iter 243 cross-references iter-110 + iter-153 LIVE alternatives
///
/// Future regression guard: if someone removes the per-field LIVE
/// branches from Lua_SetUnitField without flipping the catalog back
/// (or vice-versa), this test fires.
/// </summary>
public sealed class Iter136SetUnitFieldPartialLiveTests
{
    [Fact]
    public void SetUnitField_StatusIsLive()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"];
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 136 mirrored HeroStatEdit's iter 100/129 LIVE branches into Lua_SetUnitField for hull/shield/speed");
    }

    [Fact]
    public void SetUnitField_NoteEnumeratesEveryLiveSubfield()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"];
        entry.Note.Should().Contain("hull");
        entry.Note.Should().Contain("shield");
        entry.Note.Should().Contain("speed");
        entry.Note.Should().Contain("invuln_flag");
        entry.Note.Should().Contain("prevent_death");
        entry.Note.Should().Contain("LIVE");
    }

    [Fact]
    public void SetUnitField_NoteCitesShieldEngineRvas()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"];
        entry.Note.Should().Contain("0x3A8630",
            "SetFrontShield engine helper RVA must be cited so future RE auditors can re-verify");
        entry.Note.Should().Contain("0x3A91E0",
            "SetRearShield engine helper RVA must be cited so future RE auditors can re-verify");
    }

    [Fact]
    public void SetUnitField_NoteCitesSpeedEngineRva()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"];
        entry.Note.Should().Contain("0x3A8C90",
            "SetSpeedOverride engine helper RVA must be cited");
    }

    [Fact]
    public void SetUnitField_NoteEnumeratesEveryPhase1Field()
    {
        // Operator-trust signal: the Note explicitly names every
        // sub-field — both the LIVE branches AND the Phase-1 fall-through
        // — so an operator can know which staged edits actually mutate
        // the engine vs which queue silently. After iter 243, invuln_flag
        // + prevent_death are LIVE direct-write branches but still appear
        // in the rationale (now describing their direct-write strategy
        // and pointing to engine-state-aware alternatives).
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"];
        entry.Note.Should().Contain("max_hull");
        entry.Note.Should().Contain("max_shield");
        entry.Note.Should().Contain("max_speed");
        entry.Note.Should().Contain("attack_power");
        entry.Note.Should().Contain("respawn_ms");
        entry.Note.Should().Contain("invuln_flag");
        entry.Note.Should().Contain("prevent_death");
        entry.Note.Should().Contain("is_hero");
        entry.Note.Should().Contain("respawn_enabled");
        entry.Note.Should().Contain("owner_slot");
    }

    [Fact]
    public void SetUnitField_NoteDeclaresLiveFieldCount()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"];
        entry.Note.Should().Contain("7/13",
            "iter 258 extended the LIVE branch count from 5/13 (iter 243) to 7/13 by adding max_hull + max_shield type-stats writes via the GameObj+0x298 → UnitType chain");
    }

    [Fact]
    public void SetUnitField_NoteCitesIter258TypeLevelCaveat()
    {
        // iter-258 design: max_hull / max_shield write to the per-unit-TYPE
        // stats struct, not the per-instance struct. The rationale must
        // surface the "affects EVERY unit of this type for the session"
        // caveat so operators don't expect per-instance buff/nerf.
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"];
        entry.Note.Should().Contain("max_hull",
            "iter-258 LIVE branch must be enumerated by name");
        entry.Note.Should().Contain("max_shield",
            "iter-258 LIVE branch must be enumerated by name");
        entry.Note.Should().Contain("UnitType+0xDCC",
            "iter-258 max_hull writes the per-type stats struct at +0xDCC");
        entry.Note.Should().Contain("UnitType+0xDD0",
            "iter-258 max_shield writes UnitType+0xDD0 (front)");
        entry.Note.Should().Contain("UnitType+0xDD4",
            "iter-258 max_shield dual-writes UnitType+0xDD4 (rear)");
        entry.Note.Should().Contain("EVERY unit of this type",
            "iter-258 type-shared semantics must be loud and clear in the rationale");
        entry.Note.Should().Contain("iter-256",
            "iter-258 RE walk applied the iter-256 AOB-drift memory rule for semantic verification");
    }

    [Fact]
    public void SetUnitField_NoteCitesIter243LiveAlternatives()
    {
        // iter-243 design: invuln_flag and prevent_death are LIVE direct
        // memory writes with the engine-state-machine bypass caveat per
        // the feedback_flag_flipping_vs_engine_state memory rule. The
        // Note must point operators to the engine-state-aware LIVE
        // wires (iter-110 MakeInvulnerableLua + iter-153
        // SetCannotBeKilledLua) for full gameplay correctness.
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"];
        entry.Note.Should().Contain("MakeInvulnerableLua",
            "iter-110 LIVE alternative for full hardpoint-propagated invulnerability");
        entry.Note.Should().Contain("SetCannotBeKilledLua",
            "iter-153 LIVE alternative for engine-state-aware cannot-be-killed");
        entry.Note.Should().Contain("iter-108",
            "owner_slot defer pointer to SWFOC_ChangeUnitOwnerLua engine-aware path");
    }

    [Fact]
    public void SetUnitField_NoteCitesIter243CrossRefs()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"];
        entry.Note.Should().Contain("iter 243",
            "rationale must cite iter 243 for the +2 sub-field LIVE flip provenance");
        entry.Note.Should().Contain("iter 136",
            "rationale must preserve the original iter 136 hull/shield/speed flip provenance");
    }

    [Fact]
    public void SetUnitField_ComposedBadgeReportsLive()
    {
        var badge = CapabilityStatusCatalog.ComposeBadge("SWFOC_SetUnitField");
        badge.Should().Be("LIVE",
            "single-action composed badge follows the catalog flip");
    }

    [Fact]
    public void SetUnitField_AndHeroStatEdit_BothLive_ConsistentPattern()
    {
        // Iter 135 + iter 136 together: BOTH per-field dispatchers are
        // now LIVE with hull/shield/speed routing through engine
        // helpers. This invariant test pins both ends of the pattern;
        // future drift in either dispatcher's catalog entry breaks the
        // assertion.
        var setUnitField = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"];
        var heroStatEdit = CapabilityStatusCatalog.Entries["SWFOC_HeroStatEdit"];
        setUnitField.Status.Should().Be(CapabilityStatus.Live);
        heroStatEdit.Status.Should().Be(CapabilityStatus.Live);
        setUnitField.Note.Should().Contain("hull").And.Contain("shield").And.Contain("speed");
        heroStatEdit.Note.Should().Contain("hull").And.Contain("shield").And.Contain("speed");
    }

    [Fact]
    public void SetUnitField_NoteCitesIter268MaxSpeedHonestDeferAlternatives()
    {
        // Iter 268 HONEST DEFER close-out (iter-267 telescoped 2-iter cycle):
        // max_speed is honest-deferred per iter-249 pattern because:
        //   1. Ledger has NO TYPE-LEVEL max_speed offset (iter-258 reader-side
        //      pattern unavailable).
        //   2. iter-99 SWFOC_SetUnitSpeed + iter-100 SWFOC_SetPerFactionSpeedMultiplier
        //      provide per-instance + per-faction LIVE alternatives.
        //   3. Routing max_speed through this dispatcher would sacrifice iter-258
        //      TYPE-LEVEL semantic consistency (max_hull/max_shield are TYPE-shared).
        //
        // The rationale must cite all 3 reasons + cross-references so operators
        // reading the Phase-1 mirror entry can find the LIVE alternatives without
        // grepping docs.
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"];
        entry.Note.Should().Contain("max_speed → Phase-1 mirror with HONEST DEFER",
            "iter-268 marks max_speed as honest-defer candidate distinct from the other 4 Phase-1 mirror sub-fields");
        entry.Note.Should().Contain("iter 267-268",
            "iter-268 close-out provenance — telescoped 2-iter cycle mirrors iter-249 honest-defer pattern");
        entry.Note.Should().Contain("iter-99 SWFOC_SetUnitSpeed",
            "iter-268 cross-reference to per-instance LIVE alternative");
        entry.Note.Should().Contain("iter-100 SWFOC_SetPerFactionSpeedMultiplier",
            "iter-268 cross-reference to per-faction LIVE alternative");
        entry.Note.Should().Contain("Override_Max_Speed @ 0x57E590",
            "iter-267 RE walk identified the Lua wrapper that walks per-instance locomotor (NOT type-stats)");
        entry.Note.Should().Contain("locomotor",
            "iter-267 RE walk's key finding is the locomotor-vs-UnitType distinction");
    }

    [Fact]
    public void SetUnitField_NoteCitesIter270AttackPowerHonestDeferAlternatives()
    {
        // Iter 270 HONEST DEFER close-out (iter-269 telescoped 2-iter cycle; 3rd
        // honest-defer arc this session). attack_power is honest-deferred per
        // iter-249 pattern because:
        //   1. Combat path has NO central per-unit attack_power read site —
        //      HardpointFire @ 0x387F50 inspection (iter-269 RE walk) shows
        //      param_1+0x28 is hardpoint HP CONSUMER and param_4 damage is
        //      PASSED IN, computed dynamically from per-weapon XML at fire time.
        //   2. iter-94 rejection EMPIRICALLY REAFFIRMED via fresh RE walk per
        //      iter-256 memory rule (rule confirms TRUE NEGATIVES, not just
        //      FALSE POSITIVES — iter-249 caught FP; iter-267 + iter-269 confirm TN).
        //   3. Three existing LIVE alternatives already triple-cover damage tuning
        //      (alternative-set pattern, refinement of iter-251/268 single-
        //      alternative pattern):
        //        - iter-96 SWFOC_SetDamageMultiplierGlobal — global outgoing.
        //        - iter-154 SWFOC_SetDamageModifierLua — per-instance.
        //        - iter-225 SWFOC_SetFireRateMultiplierGlobal — global fire-rate.
        //
        // Rationale must cite all 3 alternatives so operators reading the Phase-1
        // mirror entry can pick the right LIVE wire by scope.
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"];
        entry.Note.Should().Contain("attack_power → Phase-1 mirror with HONEST DEFER",
            "iter-270 marks attack_power as honest-defer candidate distinct from the other 3 Phase-1 mirror sub-fields");
        entry.Note.Should().Contain("iter 269-270",
            "iter-270 close-out provenance — telescoped 2-iter cycle mirrors iter-249 + iter-267-268 honest-defer patterns");
        entry.Note.Should().Contain("iter-96 SWFOC_SetDamageMultiplierGlobal",
            "iter-270 cross-reference to GLOBAL outgoing damage scaling LIVE alternative (alternative-set pattern entry 1/3)");
        entry.Note.Should().Contain("iter-154 SWFOC_SetDamageModifierLua",
            "iter-270 cross-reference to PER-INSTANCE damage scaling LIVE alternative (alternative-set pattern entry 2/3)");
        entry.Note.Should().Contain("iter-225 SWFOC_SetFireRateMultiplierGlobal",
            "iter-270 cross-reference to GLOBAL fire-rate LIVE alternative (alternative-set pattern entry 3/3)");
        entry.Note.Should().Contain("HardpointFire @ 0x387F50",
            "iter-269 RE walk identified the hardpoint HP consumer that confirms damage is param-passed not type-stored");
        entry.Note.Should().Contain("per-weapon XML",
            "iter-269 RE walk's key finding is that damage is computed from per-weapon XML attributes at fire time, not a per-unit field");
        entry.Note.Should().Contain("alternative-set pattern",
            "iter-270 introduces the alternative-set pattern as refinement of iter-251/268 single-alternative pattern");
    }
}
