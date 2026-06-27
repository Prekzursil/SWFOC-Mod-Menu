using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-28 (iter 111, master ralph loop) — pins three per-unit Lua
/// bool-method LIVE wires that share <c>Lua_DispatchUnitBoolMethod</c>:
/// <list type="bullet">
///   <item><c>SWFOC_HideUnitLua</c> → <c>Hide(bool)</c></item>
///   <item><c>SWFOC_PreventAiUsageLua</c> → <c>Prevent_AI_Usage(bool)</c></item>
///   <item><c>SWFOC_SetUnitSelectableLua</c> → <c>Set_Selectable(bool)</c></item>
/// </list>
///
/// Same iter 99/100/107/108/109/110 pattern (DoString into engine's
/// Lua-registered API), batched into one shared C++ helper so the
/// pattern's marginal cost per new wire is now a 4-line wrapper.
/// </summary>
public sealed class Iter111UnitBoolMethodBatchTests
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
    public async Task HideUnitLua_DispatchesLiveOk()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var result = await adapter.SendRawAsync(
            "return SWFOC_HideUnitLua('Find_First_Object(\"Empire_AT_AT\")', 'true')",
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().Contain("Hide dispatched");
        result.Response.Should().Contain("LIVE");
        sim.GameState.LastUnitBoolMethodCalls["Hide"].Unit
            .Should().Be("Find_First_Object(\"Empire_AT_AT\")");
        sim.GameState.LastUnitBoolMethodCalls["Hide"].Bool.Should().Be("true");
    }

    [Fact]
    public async Task PreventAiUsageLua_DispatchesLiveOk()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var result = await adapter.SendRawAsync(
            "return SWFOC_PreventAiUsageLua('Find_First_Object(\"Rebel_Trooper_Squad\")', 'true')",
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().Contain("Prevent_AI_Usage dispatched");
        sim.GameState.LastUnitBoolMethodCalls["PreventAiUsage"].Bool.Should().Be("true");
    }

    [Fact]
    public async Task SetUnitSelectableLua_DispatchesLiveOk()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var result = await adapter.SendRawAsync(
            "return SWFOC_SetUnitSelectableLua('Find_First_Object(\"Empire_AT_ST\")', 'false')",
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().Contain("Set_Selectable dispatched");
        sim.GameState.LastUnitBoolMethodCalls["Selectable"].Bool.Should().Be("false");
    }

    [Fact]
    public void Catalog_MarksAllThreeBatchEntries_AsLive()
    {
        // The batch ships three catalog flips at once; the test pins
        // all three so a partial revert (someone flips one back to
        // PHASE 2 PENDING) is caught immediately.
        CapabilityStatusCatalog.Lookup("SWFOC_HideUnitLua").Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Lookup("SWFOC_PreventAiUsageLua").Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Lookup("SWFOC_SetUnitSelectableLua").Status
            .Should().Be(CapabilityStatus.Live);
    }
}
