using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 59) — pins per-button capability metadata across the
/// last cluster of bridge-using tabs (WorldState, Inspector, Lua
/// Playground, Event Stream, Story Events, Tactical Units, Probes). Each
/// VM exposes <c>AllActions</c> via <see cref="CapabilityAwareAction"/>
/// so the catalog status is queryable uniformly across the editor.
///
/// The 6 listed tabs together close out the operator-trust pattern
/// coverage — every bridge-using V2 tab now exposes per-action metadata.
/// </summary>
public sealed class Iter59CapabilityCoverageTests
{
    private static V2BridgeAdapter NewAdapter(out SwfocSimulator sim)
    {
        sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        return new V2BridgeAdapter(pipe);
    }

    [Fact]
    public void Inspector_Refresh_BadgeIsLiveOnly()
    {
        var adapter = NewAdapter(out var sim); using var _ = sim;
        var vm = new InspectorTabViewModel(adapter, new V2UnitMutationDispatcher(adapter));
        vm.Refresh.Badge.Should().Be("LIVE ONLY",
            "SWFOC_InspectUnit needs a running game — RequiresLiveSwfoc maps to LIVE ONLY");
        vm.HasNonLiveAction.Should().BeTrue();
        vm.CapabilityNoteLine.Should().Contain("running SWFOC session");
    }

    [Fact]
    public void LuaPlayground_Run_BadgeIsLive()
    {
        var adapter = NewAdapter(out var sim); using var _ = sim;
        var vm = new LuaPlaygroundTabViewModel(adapter);
        vm.Run.Badge.Should().Be("LIVE",
            "SWFOC_DoString is the catalogued LIVE escape hatch");
        vm.AllActions.Should().HaveCount(1);
    }

    [Fact]
    public void EventStream_Drain_BadgeIsLive()
    {
        var adapter = NewAdapter(out var sim); using var _ = sim;
        var vm = new EventStreamViewModel(adapter);
        vm.Drain.Badge.Should().Be("LIVE",
            "SWFOC_EventStreamDrain reads the SetHP detour ring buffer — LIVE");
    }

    [Fact]
    public void StoryEvents_FireAndSetFlag_BothLive()
    {
        var adapter = NewAdapter(out var sim); using var _ = sim;
        var vm = new StoryEventsTabViewModel(adapter);
        vm.FireEvent.Badge.Should().Be("LIVE");
        vm.SetFlag.Badge.Should().Be("LIVE",
            "Both route via SWFOC_DoString → engine globals (Story_Event, Set_Game_Flag)");
        vm.AllActions.Should().HaveCount(2);
    }

    [Fact]
    public void TacticalUnits_Refresh_BadgeIsLive()
    {
        var adapter = NewAdapter(out var sim); using var _ = sim;
        var vm = new TacticalUnitsFilterTabViewModel(adapter);
        vm.Refresh.Badge.Should().Be("LIVE",
            "SWFOC_ListTacticalUnits is the engine walker — LIVE");
    }

    [Fact]
    public void Probes_Send_BadgeIsLive()
    {
        var adapter = NewAdapter(out var sim); using var _ = sim;
        var vm = new ProbesTabViewModel(adapter);
        vm.Send.Badge.Should().Be("LIVE",
            "Send routes raw Lua via SWFOC_DoString — escape hatch is LIVE");
    }
}
