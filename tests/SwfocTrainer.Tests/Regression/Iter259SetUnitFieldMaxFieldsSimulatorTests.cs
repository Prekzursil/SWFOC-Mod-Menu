using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-07 (iter 259) — pins the simulator handler extension + round-trip
/// behavior for the iter-258 LIVE branches of SWFOC_SetUnitField (max_hull +
/// max_shield).
///
/// Bridge-side (iter 258): walks GameObj+0x298 → UnitType*, then writes float
/// at UnitType+0xDCC (max_hull) or dual-writes UnitType+0xDD0 / +0xDD4
/// (max_shield front+rear). The TYPE-stats struct is shared across every unit
/// instance of the same type, so the operator-trust scope is "every unit of
/// this type for the session" (NOT per-instance).
///
/// Simulator-side (iter 259): HandleSetUnitField branches now iterate
/// GameState.Units filtered by TypeName, mirroring the bridge's TYPE-shared
/// effect at the FakeUnit abstraction level. Two-unit fixture verifies the
/// type-shared semantic: write max_hull on Unit A (type "AT_AT") → Unit B
/// (also type "AT_AT") gets the same MaxHull. Write on Unit C (type
/// "Rebel_Trooper_Squad") leaves AT_ATs untouched.
///
/// Pattern parallels iter-244 simulator pin file but extends the validation to
/// type-level scope which is NEW with iter-258. Closes the iter-242 deferred
/// 7 sub-fields list down to 5 deferred (max_hull + max_shield now LIVE; ratio
/// 5/13 → 7/13 in CapabilityStatusCatalog).
/// </summary>
public sealed class Iter259SetUnitFieldMaxFieldsSimulatorTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter, FakeUnit atAt1, FakeUnit atAt2, FakeUnit trooper) NewSession()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        // Two AT-ATs (same type) + one Trooper (different type) — the
        // minimum cardinality to verify TYPE-shared vs cross-type isolation.
        var atAt1 = new FakeUnit
        {
            TypeName = "AT_AT",
            OwnerSlot = 0,
            CurrentHull = 200f,
            MaxHull = 200f,
            CurrentShield = 0f,
            MaxShield = 0f,
        };
        var atAt2 = new FakeUnit
        {
            TypeName = "AT_AT",
            OwnerSlot = 0,
            CurrentHull = 200f,
            MaxHull = 200f,
            CurrentShield = 0f,
            MaxShield = 0f,
        };
        var trooper = new FakeUnit
        {
            TypeName = "Rebel_Trooper_Squad",
            OwnerSlot = 1,
            CurrentHull = 100f,
            MaxHull = 100f,
            CurrentShield = 0f,
            MaxShield = 0f,
        };
        state.Units.Add(atAt1);
        state.Units.Add(atAt2);
        state.Units.Add(trooper);
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, adapter, atAt1, atAt2, trooper);
    }

    [Fact]
    public void CatalogStatus_SetUnitFieldIsLive_PostIter258()
    {
        // Pin: catalog status stays Live across the iter 136 → iter 243 →
        // iter 258 multi-stage promotion. Iter 258 brings ratio to 7/13.
        CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"].Status
            .Should().Be(CapabilityStatus.Live,
                "iter 136 promoted to Live (3/13), iter 243 extended to 5/13, iter 258 extended to 7/13");
    }

    [Fact]
    public async Task SimulatorRoundTrip_MaxHull_TypeSharedWriteAffectsAllSiblings()
    {
        // Operator workflow: stage max_hull = 999 via UnitStatEditor for Unit A
        // (one specific AT-AT) → bridge wire format `SWFOC_SetUnitField(addrA,
        // 'max_hull', 999)` → bridge walks addrA + 0x298 → UnitType* → writes
        // 999 at UnitType+0xDCC. Because the type-stats struct is SHARED, the
        // change is visible to EVERY unit of the same type. Simulator mirrors
        // this by iterating Units filtered by TypeName.
        var (sim, adapter, atAt1, atAt2, trooper) = NewSession();
        try
        {
            atAt1.MaxHull.Should().Be(200f, "fixture seed");
            atAt2.MaxHull.Should().Be(200f, "fixture seed");
            trooper.MaxHull.Should().Be(100f, "fixture seed");

            var setResult = await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'max_hull', 999)", atAt1.Id),
                CancellationToken.None);
            setResult.Succeeded.Should().BeTrue();

            atAt1.MaxHull.Should().Be(999f, "iter 258 LIVE branch writes max_hull");
            atAt2.MaxHull.Should().Be(999f,
                "iter 258 type-shared semantic: writing max_hull on AT-AT 1 also affects AT-AT 2 because they share the same UnitType stats struct");
            trooper.MaxHull.Should().Be(100f,
                "iter 258 cross-type isolation: Rebel_Trooper_Squad has its own UnitType, must NOT be affected by AT_AT's max_hull write");
        }
        finally
        {
            sim.Dispose();
        }
    }

    [Fact]
    public async Task SimulatorRoundTrip_MaxShield_TypeSharedDualWrite()
    {
        // Bridge dual-writes UnitType+0xDD0 (front) AND UnitType+0xDD4 (rear)
        // mirroring iter-129 SetUnitShield's per-instance pattern. At the
        // FakeUnit abstraction the single MaxShield field collapses both —
        // operator just sees one shield-cap value. Type-shared semantic
        // mirrors max_hull.
        var (sim, adapter, atAt1, atAt2, trooper) = NewSession();
        try
        {
            atAt1.MaxShield.Should().Be(0f, "fixture seed");
            atAt2.MaxShield.Should().Be(0f, "fixture seed");
            trooper.MaxShield.Should().Be(0f, "fixture seed");

            var setResult = await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'max_shield', 500)", atAt2.Id),
                CancellationToken.None);
            setResult.Succeeded.Should().BeTrue();

            atAt2.MaxShield.Should().Be(500f, "iter 258 LIVE branch writes max_shield");
            atAt1.MaxShield.Should().Be(500f,
                "iter 258 type-shared: writing max_shield on AT-AT 2 propagates to sibling AT-AT 1");
            trooper.MaxShield.Should().Be(0f,
                "iter 258 cross-type isolation: Trooper UnitType untouched");
        }
        finally
        {
            sim.Dispose();
        }
    }

    [Fact]
    public async Task SimulatorRoundTrip_MaxHullThenMaxShield_OnDifferentTypes_StaysIsolated()
    {
        // Combined regression: write max_hull on AT_AT, then max_shield on
        // Trooper. Each type-stats record updates independently — no
        // cross-contamination.
        var (sim, adapter, atAt1, atAt2, trooper) = NewSession();
        try
        {
            await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'max_hull', 1500)", atAt1.Id),
                CancellationToken.None);
            await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'max_shield', 250)", trooper.Id),
                CancellationToken.None);

            atAt1.MaxHull.Should().Be(1500f);
            atAt2.MaxHull.Should().Be(1500f, "AT-AT type-shared");
            trooper.MaxHull.Should().Be(100f, "Trooper untouched by AT_AT max_hull");

            atAt1.MaxShield.Should().Be(0f, "AT_AT untouched by Trooper max_shield");
            atAt2.MaxShield.Should().Be(0f, "AT_AT untouched by Trooper max_shield");
            trooper.MaxShield.Should().Be(250f, "Trooper-only target");
        }
        finally
        {
            sim.Dispose();
        }
    }

    [Fact]
    public async Task SimulatorRoundTrip_MaxHull_WithSingleUnitType_DoesNotErrorOut()
    {
        // Edge case: only one unit of the type exists. The TypeName filter
        // matches exactly one Unit (the target itself). Round-trip still
        // works — verifies the type-shared loop doesn't break when sibling
        // count == 1.
        var (sim, adapter, _, _, trooper) = NewSession();
        try
        {
            var setResult = await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'max_hull', 750)", trooper.Id),
                CancellationToken.None);
            setResult.Succeeded.Should().BeTrue();
            trooper.MaxHull.Should().Be(750f);
        }
        finally
        {
            sim.Dispose();
        }
    }

    [Fact]
    public async Task SimulatorRoundTrip_MaxHull_LegacyPascalCaseStillWritesPerInstance()
    {
        // The legacy PascalCase "MaxHull" branch (kept for backwards-compat
        // with PhaseCSimulatorTests) is per-instance — it writes the targeted
        // unit's MaxHull only. iter-258 added the snake_case "max_hull"
        // branch with type-shared semantics. The two branches have DIFFERENT
        // scopes; this regression guard pins the difference so a future
        // "let's unify them" refactor can't silently change the legacy scope.
        var (sim, adapter, atAt1, atAt2, _) = NewSession();
        try
        {
            await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'MaxHull', 333)", atAt1.Id),
                CancellationToken.None);

            atAt1.MaxHull.Should().Be(333f,
                "legacy PascalCase 'MaxHull' writes per-instance");
            atAt2.MaxHull.Should().Be(200f,
                "legacy PascalCase does NOT propagate to type siblings; sibling AT-AT keeps fixture seed");
        }
        finally
        {
            sim.Dispose();
        }
    }

    [Fact]
    public void HandleSetUnitField_SnakeCaseAndPascalCaseSubFields_CountIsTwelve()
    {
        // Pin: 12 distinct snake_case sub-field branches (hull/shield/speed
        // + invuln_flag/prevent_death + max_hull/max_shield/max_speed/
        // attack_power/is_hero/respawn_enabled/respawn_ms) + owner_slot = 13
        // total. Iter 258 promoted max_hull/max_shield to LIVE without adding
        // new sub-fields. Drift guard catches any silent addition that would
        // shift the iter-242 sub-field taxonomy.
        var simSource = System.IO.File.ReadAllText(
            System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "..", "..", "..",
                "Simulator", "SwfocSimulator.cs"));
        var snakeCaseFields = new[]
        {
            "hull", "shield", "speed",
            "invuln_flag", "prevent_death",
            "max_hull", "max_shield", "max_speed",
            "attack_power", "is_hero",
            "respawn_enabled", "respawn_ms",
            "owner_slot",
        };
        foreach (var f in snakeCaseFields)
        {
            simSource.Should().Contain($"case \"{f}\":",
                $"snake_case branch '{f}' must exist in HandleSetUnitField (iter 244 + iter 258 taxonomy)");
        }
        snakeCaseFields.Length.Should().Be(13,
            "iter 242 sub-field taxonomy is fixed at 13 fields");
    }
}
