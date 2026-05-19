using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Simulator.E2E;

/// <summary>
/// 2026-04-27 (iter 23 — Phase C) — global toggles, inspector, hardpoints,
/// log/tick. Brings the simulator's coverage of frequently-used trainer
/// functions to feature-complete for the editor's V2 tab surface.
/// </summary>
public sealed class PhaseCSimulatorTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession(FakeGameState state)
    {
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, connectTimeoutMs: 1500, readTimeoutMs: 1500);
        return (sim, new V2BridgeAdapter(pipe));
    }

    [Fact]
    public async Task GodMode_FlipsInvulnerableOnEveryAliveUnit()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0 });
        state.Units.Add(new FakeUnit { TypeName = "Empire_AT_AT", OwnerSlot = 1 });
        state.Units.Add(new FakeUnit { TypeName = "Underworld_Mercenary_Squad", OwnerSlot = 2, Alive = false });
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync("return SWFOC_GodMode(1)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.GodModeEnabled.Should().BeTrue();
        state.Units.Where(u => u.Alive).All(u => u.Invulnerable).Should().BeTrue();
        state.Units.First(u => !u.Alive).Invulnerable.Should().BeFalse("dead units don't get the flag");
    }

    [Fact]
    public async Task GodMode_Off_ClearsInvulnerable()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0, Invulnerable = true });
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GodMode(0)", CancellationToken.None);

        state.GodModeEnabled.Should().BeFalse();
        state.Units[0].Invulnerable.Should().BeFalse();
    }

    [Fact]
    public async Task HealAllLocal_FullyHealsLocalPlayerUnits()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        state.Units.Add(new FakeUnit
        {
            TypeName = "Rebel_Trooper_Squad",
            OwnerSlot = 0,
            MaxHull = 100,
            CurrentHull = 17,
            MaxShield = 50,
            CurrentShield = 5,
            Alive = false,
        });
        state.Units.Add(new FakeUnit
        {
            TypeName = "Empire_AT_AT",
            OwnerSlot = 1, // not local — should NOT be healed
            MaxHull = 9999,
            CurrentHull = 1,
        });
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_HealAllLocal()", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        var rebel = state.Units.First(u => u.OwnerSlot == 0);
        rebel.Alive.Should().BeTrue();
        rebel.CurrentHull.Should().Be(rebel.MaxHull);
        rebel.CurrentShield.Should().Be(rebel.MaxShield);

        var empire = state.Units.First(u => u.OwnerSlot == 1);
        empire.CurrentHull.Should().Be(1, "non-local units must NOT be healed");
    }

    [Fact]
    public async Task FreeBuild_FlipsToggle()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;
        await adapter.SendRawAsync("return SWFOC_FreeBuild(1)", CancellationToken.None);
        state.FreeBuildEnabled.Should().BeTrue();
        await adapter.SendRawAsync("return SWFOC_FreeBuild(0)", CancellationToken.None);
        state.FreeBuildEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task FreeCam_FlipsToggle()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;
        await adapter.SendRawAsync("return SWFOC_FreeCam(1)", CancellationToken.None);
        state.FreeCamEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task FreezeCredits_FlipsToggle()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;
        await adapter.SendRawAsync("return SWFOC_FreezeCredits(1)", CancellationToken.None);
        state.CreditsFrozen.Should().BeTrue();
    }

    [Fact]
    public async Task UncapCredits_SetsMaxToMinusOne()
    {
        var state = FakeGameState.NewGalacticCampaign();
        state.MaxCredits.Should().Be(999999);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_UncapCredits()", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.MaxCredits.Should().Be(-1);
    }

    [Fact]
    public async Task GetMaxCredits_ReturnsCurrentValue()
    {
        var state = FakeGameState.NewGalacticCampaign();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_GetMaxCredits()", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().Be("999999");
    }

    [Fact]
    public async Task ToggleOhk_FlipsToggle()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;
        await adapter.SendRawAsync("return SWFOC_ToggleOHKAttackPower(1)", CancellationToken.None);
        state.OneHitKillEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task CombinedGodOhk_FlipsBothTogglesAtOnce()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0 });
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_CombinedGodOHK(1)", CancellationToken.None);

        state.GodModeEnabled.Should().BeTrue();
        state.OneHitKillEnabled.Should().BeTrue();
        state.Units[0].Invulnerable.Should().BeTrue();
    }

    [Fact]
    public async Task InspectUnit_ReturnsDetailedSnapshot()
    {
        // The bridge emits SPACE-DELIMITED key=value tokens (see
        // BridgeInspectorDispatcher.ParseKeyValueSpaceList). The keys the
        // editor reads are hull, owner, invuln_flag, prevent_death.
        var state = FakeGameState.NewTacticalSkirmish();
        var unit = new FakeUnit
        {
            TypeName = "Han_Solo",
            OwnerSlot = 0,
            IsHero = true,
            MaxHull = 250,
            CurrentHull = 200,
            MaxShield = 50,
            CurrentShield = 40,
            Speed = 120,
            MaxSpeed = 120,
            Invulnerable = true,
            DeathPrevented = true,
        };
        state.Units.Add(unit);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            $"return SWFOC_InspectUnit({unit.Id})", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().Contain("hull=200");
        round.Response.Should().Contain("owner=0");
        round.Response.Should().Contain("invuln_flag=1");
        round.Response.Should().Contain("prevent_death=1");
        round.Response.Should().Contain($"obj_id={unit.Id}");
        state.SelectedUnitId.Should().Be(unit.Id, "Inspect should mark this as the selected unit");
    }

    [Fact]
    public async Task GetSelectedUnit_ReturnsZero_WhenNoneSelected()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_GetSelectedUnit()", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().Be("0");
    }

    [Fact]
    public async Task GetHardpoints_ReturnsThreePieceLoadout()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var unit = new FakeUnit { TypeName = "Empire_AT_AT", OwnerSlot = 1 };
        state.Units.Add(unit);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            $"return SWFOC_GetHardpoints({unit.Id})", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        var rows = round.Response!.Split('|');
        rows.Should().HaveCount(3);
        rows[0].Should().Contain("MAIN_GUN");
        rows.All(r => r.EndsWith(";OK")).Should().BeTrue("non-invuln unit reports OK status");
    }

    [Fact]
    public async Task GetHardpoints_ReportsInvulnerable_WhenFlagSet()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var unit = new FakeUnit { TypeName = "Empire_AT_AT", OwnerSlot = 1, Invulnerable = true };
        state.Units.Add(unit);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            $"return SWFOC_GetHardpoints({unit.Id})", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        var rows = round.Response!.Split('|');
        rows.All(r => r.EndsWith(";INVULNERABLE")).Should().BeTrue();
    }

    [Fact]
    public async Task SetTargetFilter_StoresMask()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_SetTargetFilter(7)", CancellationToken.None);

        state.TargetFilterMask.Should().Be(7);
    }

    [Fact]
    public async Task SetUnitCapOverride_StoresPerFactionCap()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_SetUnitCapOverride(\"REBEL\", 999)", CancellationToken.None);

        state.PerFactionUnitCap["REBEL"].Should().Be(999);
    }

    [Fact]
    public async Task SetUnitField_UpdatesFieldByName()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var unit = new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0, MaxHull = 100 };
        state.Units.Add(unit);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            $"return SWFOC_SetUnitField({unit.Id}, \"MaxHull\", 9999)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        unit.MaxHull.Should().Be(9999);
    }

    [Fact]
    public async Task GetPlayerWrapper_ReturnsNonZeroPseudoPointer()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_GetPlayerWrapper(0)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().NotBe("0");
        round.Response.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DiagGameTick_IncrementsCounter()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var first = await adapter.SendRawAsync("return SWFOC_DiagGameTick()", CancellationToken.None);
        var second = await adapter.SendRawAsync("return SWFOC_DiagGameTick()", CancellationToken.None);

        first.Response.Should().Be("1");
        second.Response.Should().Be("2");
        state.GameTickCount.Should().Be(2);
    }

    [Fact]
    public async Task DumpState_ReturnsKeyValueDigest()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0 });
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync("return SWFOC_DumpState()", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().Contain("mode=TacticalLand");
        round.Response.Should().Contain("ai=1");
        round.Response.Should().Contain("units=1");
        round.Response.Should().Contain("alive=1");
    }

    [Fact]
    public async Task Log_AppendsToLogBuffer()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_Log(\"hello world\")", CancellationToken.None);
        await adapter.SendRawAsync(
            "return SWFOC_Log(\"second line\")", CancellationToken.None);

        state.LogLines.Should().HaveCount(2);
        state.LogLines[0].Should().Be("hello world");
        state.LogLines[1].Should().Be("second line");
    }

    [Fact]
    public async Task EnumerateUnits_IncludesDeadUnitsToo()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0 });
        state.Units.Add(new FakeUnit { TypeName = "Empire_AT_AT", OwnerSlot = 1, Alive = false });
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_EnumerateUnits()", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        var rows = round.Response!.Split('|');
        rows.Should().HaveCount(2, "EnumerateUnits includes dead units (unlike ListTacticalUnits)");
    }

    [Fact]
    public async Task SetCreditsForSlot_BridgesToSetCredits()
    {
        // SWFOC_SetCreditsForSlot is registered but currently shares semantics
        // with SWFOC_SetCredits. Confirm it works.
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SetCreditsForSlot(0, 12345)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.GetPlayer(0)!.Credits.Should().Be(12345);
    }
}
