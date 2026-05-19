using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-28 (iter 113, master ralph loop) — UNIVERSAL Lua-method
/// dispatcher. <c>SWFOC_CallObjMethodLua(obj_expr, method_name, args_expr)</c>
/// composes <c>(&lt;obj&gt;):&lt;method&gt;(&lt;args&gt;)</c> and dispatches via DoString.
/// One wire that covers ANY remaining engine Lua method without per-method
/// catalog flips.
///
/// Trade-off vs the per-method wires (iter 100/107/108/109/110/111/112):
/// per-method wires give the catalog a typed surface with named LIVE
/// badges; the universal wire is the escape hatch for everything not
/// explicitly catalogued. Both ship in the same bridge build.
/// </summary>
public sealed class Iter113CallObjMethodLuaTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, adapter);
    }

    [Fact]
    public async Task CallObjMethodLua_WithSingleNumericArg_DispatchesLiveOk()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var result = await adapter.SendRawAsync(
            "return SWFOC_CallObjMethodLua('Find_Player(\"REBEL\")', 'Give_Money', '5000')",
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().Contain("Give_Money dispatched");
        result.Response.Should().Contain("LIVE");
        sim.GameState.LastCallObjMethodLua.Obj.Should().Be("Find_Player(\"REBEL\")");
        sim.GameState.LastCallObjMethodLua.Method.Should().Be("Give_Money");
        sim.GameState.LastCallObjMethodLua.Args.Should().Be("5000");
    }

    [Fact]
    public async Task CallObjMethodLua_WithEmptyArgs_DispatchesNoArgMethod()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var result = await adapter.SendRawAsync(
            "return SWFOC_CallObjMethodLua('Find_First_Object(\"Empire_AT_AT\")', 'Heal', '')",
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().Contain("Heal dispatched");
        sim.GameState.LastCallObjMethodLua.Method.Should().Be("Heal");
        sim.GameState.LastCallObjMethodLua.Args.Should().Be(string.Empty,
            "empty args expression must round-trip as empty string for "
            + "no-arg methods (Heal, Stop, Despawn, etc.)");
    }

    [Fact]
    public async Task CallObjMethodLua_WithMultiArgEnableBehavior_PreservesArgList()
    {
        // Real-world example: Enable_Behavior takes (string, bool) —
        // both spliced verbatim into the args_expr.
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var result = await adapter.SendRawAsync(
            "return SWFOC_CallObjMethodLua("
            + "'Find_First_Object(\"Empire_AT_AT\")', "
            + "'Enable_Behavior', "
            + "'\"INVULNERABLE\", true')",
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        sim.GameState.LastCallObjMethodLua.Args
            .Should().Be("\"INVULNERABLE\", true",
                "multi-arg expressions must round-trip as the full Lua "
                + "args list — the bridge splices them verbatim into "
                + "(<obj>):<method>(<args>)");
    }

    [Fact]
    public void Catalog_MarksCallObjMethodLua_AsLive()
    {
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_CallObjMethodLua");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 113 universal method dispatcher — LIVE");
        entry.Note.Should().Contain("universal");
    }
}
