using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using SwfocTrainer.Tests.V2;
using Xunit;

namespace SwfocTrainer.Tests.Simulator.E2E;

/// <summary>
/// 2026-04-27 (iter 27 — Phase D continued) — VM-driven scenarios for the
/// DiagnosticsTabViewModel. Validates that clicking Refresh runs all four
/// canonical probes (GetVersion / GetBuildInfo / DiagListRegisteredFunctions
/// / DiagSelfTest) end-to-end and surfaces the responses through the bound
/// VersionText / BuildInfoText / RegisteredHelpersText / SelfTestText
/// properties.
/// </summary>
public sealed class DiagnosticsViewModelScenarioTests
{
    [Fact]
    public async Task RefreshCommand_PopulatesAllFourProbeStrings()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        using var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var settings = new V2Settings();
        var vm = new DiagnosticsTabViewModel(adapter, settings);

        await AsyncCommandPump.PumpAsync(vm.RefreshCommand);

        // The simulator returns plausible canned responses for each probe.
        // We just verify the four bound texts are non-empty (i.e. the
        // probes ran AND the response made it back into the VM).
        vm.VersionText.Should().NotBeNullOrWhiteSpace();
        vm.VersionText.Should().Contain("simulator", "the simulator self-identifies");
        vm.BuildInfoText.Should().NotBeNullOrWhiteSpace();
        vm.RegisteredHelpersText.Should().NotBeNullOrWhiteSpace();
        vm.RegisteredHelpersText.Should().Contain("SWFOC_GetVersion");
        vm.SelfTestText.Should().NotBeNullOrWhiteSpace();
        vm.SelfTestText.Should().Contain("OK");
    }

    [Fact]
    public async Task RefreshCommand_GameTickCounterIncrements()
    {
        // The Diagnostics tab also fires SWFOC_DiagGameTick to demonstrate
        // forward progress; assert the simulator's tick counter advances.
        var state = FakeGameState.NewTacticalSkirmish();
        using var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        // Drive the tick directly through the adapter (not through the VM,
        // which doesn't expose tick as a command).
        var first = await adapter.SendRawAsync("return SWFOC_DiagGameTick()", System.Threading.CancellationToken.None);
        var second = await adapter.SendRawAsync("return SWFOC_DiagGameTick()", System.Threading.CancellationToken.None);

        first.Response.Should().Be("1");
        second.Response.Should().Be("2");
        state.GameTickCount.Should().Be(2);
    }
}
