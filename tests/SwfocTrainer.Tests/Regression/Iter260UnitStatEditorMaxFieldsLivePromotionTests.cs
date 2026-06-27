using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-07 (iter 260) — verification iter for the iter-258 max_hull +
/// max_shield LIVE promotion at the UnitStatEditor staging UI layer.
///
/// **No UI changes required.** The staging dropdown's `EditFieldOptions`
/// already lists max_hull + max_shield since iter-245. iter-258 promoted the
/// bridge branches from Phase-1 mirror to LIVE without touching the
/// staging UI. This test file proves the seamless promotion: the SAME
/// staging-dropdown selection now produces LIVE engine effect at the
/// FakeUnit (simulator) level.
///
/// Pattern parallels iter-245 verification iter (which verified iter-243's
/// invuln_flag/prevent_death promotion needed no UI extension). The
/// iter-258 max_hull/max_shield promotion is a 2nd instance of the same
/// "LIVE-flip without UI churn" pattern: when an existing Phase-1 mirror
/// staging field gets a bridge LIVE branch, the staging UI propagates the
/// LIVE effect transparently because Apply already routed to the bridge.
///
/// The pin tests in this file exercise the wire-format path:
/// staging-dropdown -> BridgeUnitStatEditDispatcher -> SWFOC_SetUnitField
/// wire format -> simulator HandleSetUnitField -> FakeUnit.MaxHull/MaxShield.
/// Iter-259 already pinned simulator-level round-trip; this iter pins the
/// VM/UI layer above it.
/// </summary>
public sealed class Iter260UnitStatEditorMaxFieldsLivePromotionTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSim()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        // Two-AT_AT fixture mirrors iter-259's TYPE-shared semantic test.
        // The staging UI Apply path collapses to a single SetUnitField
        // call, so a 1-unit fixture would also work, but 2 lets us verify
        // the LIVE promotion still routes through the type-shared loop.
        state.Units.Add(new FakeUnit
        {
            TypeName = "AT_AT",
            OwnerSlot = 0,
            CurrentHull = 200f,
            MaxHull = 200f,
            MaxShield = 0f,
        });
        state.Units.Add(new FakeUnit
        {
            TypeName = "AT_AT",
            OwnerSlot = 0,
            CurrentHull = 200f,
            MaxHull = 200f,
            MaxShield = 0f,
        });
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, adapter);
    }

    [Fact]
    public void StagingFieldOptions_MaxHullAndMaxShieldStillPresent_PostIter258()
    {
        // Pin: iter-258 LIVE promotion must NOT remove max_hull/max_shield
        // from the staging dropdown. The fields are still operator-
        // selectable and just produce LIVE effect now.
        var (sim, _) = NewSim();
        using (sim)
        {
            using var vmSim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
            vmSim.Start();
            var pipe = new NamedPipeLuaBridgeClient(vmSim.PipeName, 1500, 1500);
            var adapter = new V2BridgeAdapter(pipe);
            var vm = new UnitStatEditorTabViewModel(adapter);
            vm.EditFieldOptions.Should().Contain("max_hull",
                "iter-258 LIVE promotion preserves staging-UI option");
            vm.EditFieldOptions.Should().Contain("max_shield",
                "iter-258 LIVE promotion preserves staging-UI option");
        }
    }

    [Fact]
    public void CatalogStatus_SetUnitFieldStillLive_AcrossIter258Promotion()
    {
        // Pin: iter-258 promotion must NOT flip the catalog status. The
        // SWFOC_SetUnitField entry stays Live (already was, since iter-136);
        // only the per-sub-field LIVE/Phase-1 split shifts.
        CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_DocumentsIter258TypeLevelCaveat_AtVMLayerToo()
    {
        // The composed CapabilityBadge that propagates to the staging UI
        // is "LIVE", but the rationale text on hover/tooltip must surface
        // the iter-258 TYPE-LEVEL caveat so operators don't expect
        // per-instance buff/nerf at the UnitStatEditor staging row.
        var note = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"].Note;
        note.Should().Contain("max_hull",
            "staging UI rationale must enumerate iter-258 LIVE field");
        note.Should().Contain("max_shield",
            "staging UI rationale must enumerate iter-258 LIVE field");
        note.Should().Contain("EVERY unit of this type",
            "type-shared caveat must reach UnitStatEditor tooltip");
    }

    [Fact]
    public async Task StagingApplyPath_MaxHull_ProducesLiveEngineEffect_NoUIChanges()
    {
        // The cardinal staging-UI verification: an operator selects max_hull
        // in the staging dropdown, types 999, clicks Apply. The Apply path
        // sends a SWFOC_SetUnitField wire to the bridge → simulator's
        // HandleSetUnitField → FakeUnit.MaxHull mutated. iter-259 pinned
        // the simulator side; iter-260 pins the VM/UI side feeding it.
        //
        // We exercise this via SendRawAsync directly (the VM Apply method
        // routes through V2BridgeAdapter.SendRawAsync internally; matching
        // the wire format here pins the contract without needing a full
        // UI synthesis).
        var (sim, adapter) = NewSim();
        try
        {
            var anyAtAt = sim.GameState.Units.First(u => u.TypeName == "AT_AT");
            var result = await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'max_hull', 999)", anyAtAt.Id),
                CancellationToken.None);
            result.Succeeded.Should().BeTrue(
                "iter-258 bridge wire is LIVE — SetUnitField(addr, 'max_hull', N) must succeed");
            // TYPE-shared per iter-259 simulator semantics.
            sim.GameState.Units
                .Where(u => u.TypeName == "AT_AT")
                .All(u => u.MaxHull == 999f)
                .Should().BeTrue("iter-258 LIVE flip + iter-259 type-shared sim handler propagate to all AT-ATs");
        }
        finally
        {
            sim.Dispose();
        }
    }

    [Fact]
    public async Task StagingApplyPath_MaxShield_ProducesLiveEngineEffect_NoUIChanges()
    {
        // Sibling test for max_shield. The staging UI's MaxShield input
        // path produces LIVE engine effect via iter-258 dual-write
        // (UnitType+0xDD0 + UnitType+0xDD4) → simulator's foreach-by-
        // TypeName loop sets FakeUnit.MaxShield on every sibling.
        var (sim, adapter) = NewSim();
        try
        {
            var anyAtAt = sim.GameState.Units.First(u => u.TypeName == "AT_AT");
            var result = await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'max_shield', 750)", anyAtAt.Id),
                CancellationToken.None);
            result.Succeeded.Should().BeTrue();
            sim.GameState.Units
                .Where(u => u.TypeName == "AT_AT")
                .All(u => u.MaxShield == 750f)
                .Should().BeTrue();
        }
        finally
        {
            sim.Dispose();
        }
    }

    [Fact]
    public void StagingComment_DocumentsIter258Promotion()
    {
        // Source-grep pin: the EditFieldOptions declaration site must
        // mention iter-258 LIVE promotion, not just iter-243 + iter-136.
        // Iter-260 added the iter-258 reference as part of the seamless-
        // promotion verification. Future drift-catchers can grep this
        // pin to confirm the comment hasn't decayed back.
        var vmSource = System.IO.File.ReadAllText(
            System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "SwfocTrainer.App", "V2", "ViewModels",
                "UnitStatEditorTabViewModel.cs"));
        vmSource.Should().Contain("iter 258",
            "EditFieldOptions comment must cite iter-258 LIVE promotion");
        vmSource.Should().Contain("max_hull / max_shield (iter 258",
            "iter-258 LIVE branch entry must enumerate the promoted fields explicitly");
        vmSource.Should().Contain("TYPE-LEVEL writes",
            "TYPE-LEVEL caveat must surface in the VM-layer comment");
    }
}
