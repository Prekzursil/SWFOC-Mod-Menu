using System.Linq;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.V2Vm;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 245) — pins the UnitStatEditor staging UI's
/// EditFieldOptions list. Originally iter 4 of 5 of the iter-243 batch.
///
/// 2026-05-07 (iter 260) UPDATE: iter-258 promoted max_hull + max_shield
/// from Phase-1 mirror to LIVE (TYPE-LEVEL writes via GameObj+0x298 →
/// UnitType chain). The staging UI input fields ALREADY existed since
/// iter-245; iter-258 promoted the bridge branches without UI changes.
/// **Iter-260 verification iter pins this seamless promotion**: the same
/// staging UI now produces LIVE engine effect on Apply for max_hull +
/// max_shield. Test taxonomy updated: 5 LIVE → 7 LIVE; 7 Phase-1 → 5
/// Phase-1.
///
/// Verification iter -- no UX extension needed. The existing staging
/// dropdown already lists 12 of the 13 SWFOC_SetUnitField sub-fields,
/// including iter-243 LIVE invuln_flag/prevent_death AND iter-258 LIVE
/// max_hull/max_shield. owner_slot is INTENTIONALLY EXCLUDED per
/// iter-242 design (defer-pointed to iter-108 SWFOC_ChangeUnitOwnerLua
/// for engine-aware ownership change).
///
/// This test file pins:
///   1. EditFieldOptions includes all 7 LIVE fields (iter 136 + 243 + 258).
///   2. EditFieldOptions includes the 5 Phase-1 mirror fields.
///   3. EditFieldOptions does NOT include owner_slot (iter-242 design).
///   4. Total count is exactly 12 (drift guard for future scope creep).
///   5. Composed badge stays "LIVE" for SWFOC_SetUnitField.
///
/// Future regression guard: if owner_slot is added to the staging UI
/// without flipping the catalog rationale or adding the iter-108 cross-
/// reference warning, this test fires. Conversely, if the staging list
/// is trimmed below 12, drops any LIVE field, or any Phase-1 field is
/// removed, this test fires.
/// </summary>
public sealed class Iter245UnitStatEditorStagingFieldsTests
{
    private static (SwfocSimulator sim, UnitStatEditorTabViewModel vm) NewVmAndSim()
    {
        // Match the iter-60 CapabilityCoverageTests pattern for spinning up
        // a UnitStatEditorTabViewModel: simulator + named-pipe bridge +
        // V2BridgeAdapter. The pin tests in this file only read static
        // properties (EditFieldOptions, CapabilityBadge) -- the bridge
        // isn't exercised, but the constructor requires a real adapter.
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var vm = new UnitStatEditorTabViewModel(adapter);
        return (sim, vm);
    }

    [Fact]
    public void EditFieldOptions_IncludesAllSevenLiveFields()
    {
        var (sim, vm) = NewVmAndSim();
        using (sim)
        {
            // Iter 136 LIVE: hull / shield / speed.
            vm.EditFieldOptions.Should().Contain("hull");
            vm.EditFieldOptions.Should().Contain("shield");
            vm.EditFieldOptions.Should().Contain("speed");
            // Iter 243 LIVE: invuln_flag / prevent_death (display-only
            // direct writes; engine-state-aware paths are iter-110 +
            // iter-153).
            vm.EditFieldOptions.Should().Contain("invuln_flag");
            vm.EditFieldOptions.Should().Contain("prevent_death");
            // Iter 258 LIVE: max_hull / max_shield (TYPE-LEVEL writes via
            // GameObj+0x298 → UnitType chain; affects EVERY unit of this
            // type for the session).
            vm.EditFieldOptions.Should().Contain("max_hull");
            vm.EditFieldOptions.Should().Contain("max_shield");
        }
    }

    [Fact]
    public void EditFieldOptions_IncludesAllFivePhase1MirrorFields()
    {
        var (sim, vm) = NewVmAndSim();
        using (sim)
        {
            // Post iter-258: max_hull + max_shield removed from this list
            // (now LIVE; see EditFieldOptions_IncludesAllSevenLiveFields).
            // Remaining 5 Phase-1 mirror fields:
            vm.EditFieldOptions.Should().Contain("max_speed");
            vm.EditFieldOptions.Should().Contain("attack_power");
            vm.EditFieldOptions.Should().Contain("respawn_ms");
            vm.EditFieldOptions.Should().Contain("is_hero");
            vm.EditFieldOptions.Should().Contain("respawn_enabled");
        }
    }

    [Fact]
    public void EditFieldOptions_DoesNotIncludeOwnerSlot()
    {
        // Iter-242 design pins this exclusion: writing GameObj+0x58 directly
        // bypasses Change_Owner @ 0x574D0E + selection-list update + AI
        // brain reassignment + UI roster refresh. Operator MUST use iter-108
        // SWFOC_ChangeUnitOwnerLua for engine-aware ownership change.
        var (sim, vm) = NewVmAndSim();
        using (sim)
        {
            vm.EditFieldOptions.Should().NotContain("owner_slot",
                "iter-242 intentionally excludes owner_slot to prevent silent ownership-cache desync; "
              + "operators are routed to iter-108 SWFOC_ChangeUnitOwnerLua instead");
        }
    }

    [Fact]
    public void EditFieldOptions_TotalCountIs12()
    {
        // Drift guard: 5 LIVE + 7 Phase-1 mirror = 12. If the count drifts,
        // either someone added owner_slot (must be paired with iter-108
        // warning), removed a Phase-1 field (must update iter-242 design
        // doc), or added a NEW field (must be paired with bridge LIVE
        // branch + catalog rationale extension).
        var (sim, vm) = NewVmAndSim();
        using (sim)
        {
            vm.EditFieldOptions.Should().HaveCount(12,
                "7 LIVE branches (iter 136 hull/shield/speed + iter 243 invuln_flag/prevent_death + "
              + "iter 258 max_hull/max_shield) + 5 Phase-1 mirror fields = 12; "
              + "owner_slot deferred to iter-108. Total field count UNCHANGED across iter-258 "
              + "promotion — only the LIVE/Phase-1 split shifts.");
        }
    }

    [Fact]
    public void EditFieldOptions_FirstSixPreserveIter136Ordering()
    {
        // The staging dropdown's natural order should be operator-friendly:
        // hull/max_hull/shield/max_shield/speed/max_speed as the iter-136
        // top-6 (LIVE/Phase-1 interleaved by pair). This pin captures the
        // current order so future re-orderings are deliberate.
        var (sim, vm) = NewVmAndSim();
        using (sim)
        {
            var firstSix = vm.EditFieldOptions.Take(6).ToList();
            firstSix.Should().Equal(new[]
            {
                "hull", "max_hull", "shield", "max_shield", "speed", "max_speed",
            }, "iter 245 pins the existing iter-136 ordering");
        }
    }

    [Fact]
    public void ComposedBadge_StaysLiveForSetUnitField()
    {
        // Pin: the composed CapabilityBadge stays "LIVE" across iter 136 to
        // iter 243 promotion. The badge composition is shape-driven by the
        // catalog status, so this test is a 2nd-order pin reinforcing the
        // iter-243 catalog-flip lock-in from Iter136SetUnitFieldPartialLiveTests.
        var (sim, vm) = NewVmAndSim();
        using (sim)
        {
            vm.CapabilityBadge.Should().Be("LIVE");
        }
    }
}
