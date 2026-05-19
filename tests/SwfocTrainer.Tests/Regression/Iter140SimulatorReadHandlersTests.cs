using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 140) — pins the simulator read-side handlers added
/// for the iter 96/100/107/131 LIVE wires.
///
/// Iter 138's bridge-vs-simulator coverage audit found 12 bridge
/// functions without simulator handlers. 4 of those were read-side
/// companions to LIVE wires:
///   - SWFOC_GetUnitShield (iter 131 LIVE pair-flip)
///   - SWFOC_GetUnitSpeed (iter 100 LIVE)
///   - SWFOC_GetDamageMultiplier per-slot (iter 96 LIVE)
///   - SWFOC_GetCameraPos (iter 107 readback companion)
///
/// Without these, simulator-driven E2E tests calling the read-side
/// helpers got the catch-all "(sim: unhandled probe)" sentinel
/// instead of the round-trip value the writer just stored. That
/// masked actual round-trip behavior in the test harness.
///
/// Iter 140 added the handlers reading FakeUnit/FakeGameState fields
/// the corresponding writers populate. This test pins each one with
/// a write→read round-trip assertion.
/// </summary>
public sealed class Iter140SimulatorReadHandlersTests
{
    [Fact]
    public async System.Threading.Tasks.Task GetUnitShield_RoundTripsAfterSet()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        // NewTacticalSkirmish seeds players + type names but no units;
        // seed one explicitly so we have a real unit-id to round-trip.
        var unit = new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0 };
        sim.GameState.Units.Add(unit);
        var unitId = unit.Id;

        var setResp = await adapter.SendRawAsync(
            $"return SWFOC_SetUnitShield({unitId}, 12345.0)", System.Threading.CancellationToken.None);
        setResp.Succeeded.Should().BeTrue();

        var getResp = await adapter.SendRawAsync(
            $"return SWFOC_GetUnitShield({unitId})", System.Threading.CancellationToken.None);
        getResp.Succeeded.Should().BeTrue();
        getResp.Response.Should().NotStartWith("(sim:",
            "iter 140 added the read handler — should no longer fall through to catch-all");
        float.Parse(getResp.Response!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(12345.0f, 0.01f,
                "GetUnitShield must return the value SetUnitShield just wrote");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUnitSpeed_RoundTripsAfterSet()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var unit = new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0 };
        sim.GameState.Units.Add(unit);
        var unitId = unit.Id;

        var setResp = await adapter.SendRawAsync(
            $"return SWFOC_SetUnitSpeed({unitId}, 250.0)", System.Threading.CancellationToken.None);
        setResp.Succeeded.Should().BeTrue();

        var getResp = await adapter.SendRawAsync(
            $"return SWFOC_GetUnitSpeed({unitId})", System.Threading.CancellationToken.None);
        getResp.Succeeded.Should().BeTrue();
        getResp.Response.Should().NotStartWith("(sim:");
        float.Parse(getResp.Response!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(250.0f, 0.01f);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetDamageMultiplier_PerSlot_RoundTripsAfterSet()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        // Slot 0 is the human player in NewTacticalSkirmish seeding.
        var setResp = await adapter.SendRawAsync(
            "return SWFOC_SetDamageMultiplier(0, 2.5)", System.Threading.CancellationToken.None);
        setResp.Succeeded.Should().BeTrue();

        var getResp = await adapter.SendRawAsync(
            "return SWFOC_GetDamageMultiplier(0)", System.Threading.CancellationToken.None);
        getResp.Succeeded.Should().BeTrue();
        getResp.Response.Should().NotStartWith("(sim:");
        float.Parse(getResp.Response!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(2.5f, 0.01f);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetDamageMultiplier_UnsetSlot_ReturnsIdentity()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var getResp = await adapter.SendRawAsync(
            "return SWFOC_GetDamageMultiplier(0)", System.Threading.CancellationToken.None);
        getResp.Succeeded.Should().BeTrue();
        float.Parse(getResp.Response!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(1.0f, 0.01f,
                "unset slot returns 1.0 engine-identity multiplier — matches real bridge fall-through behavior");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetCameraPos_RoundTripsAfterSet()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var setResp = await adapter.SendRawAsync(
            "return SWFOC_SetCameraPos(100.0, 200.0, 300.0)", System.Threading.CancellationToken.None);
        setResp.Succeeded.Should().BeTrue();

        var getResp = await adapter.SendRawAsync(
            "return SWFOC_GetCameraPos()", System.Threading.CancellationToken.None);
        getResp.Succeeded.Should().BeTrue();
        getResp.Response.Should().NotStartWith("(sim:");
        getResp.Response.Should().Contain("100").And.Contain("200").And.Contain("300",
            "comma-separated x,y,z must round-trip through the simulator");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUnitShield_UnknownUnitId_ReturnsMinusOneSentinel()
    {
        // Real bridge's pre-iter-131 behavior was to return -1 on unknown
        // unit ids (the cache map's default). Iter 131 LIVE wire reads the
        // engine; for replay/dev builds without the engine module loaded,
        // it falls back to the same cache-default. Simulator mirrors that.
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var resp = await adapter.SendRawAsync(
            "return SWFOC_GetUnitShield(99999999)", System.Threading.CancellationToken.None);
        resp.Succeeded.Should().BeTrue();
        resp.Response.Should().Be("-1");
    }
}
