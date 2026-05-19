using System.Threading;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.Infrastructure;

/// <summary>
/// 2026-04-27 (iter 66) — pins the new activity-log Clear button +
/// command-name substring filter. Both extend the iter 45-52 activity
/// log surface; tests run via the simulator harness so the bridge
/// adapter actually records entries.
/// </summary>
public sealed class Iter66ActivityLogClearAndFilterTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter, DiagnosticsTabViewModel vm) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var settings = new V2Settings();
        var vm = new DiagnosticsTabViewModel(adapter, settings);
        return (sim, adapter, vm);
    }

    [Fact]
    public async Task ClearActivityLog_DropsEveryEntry()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);
        adapter.RecentCalls.Should().HaveCount(2);

        adapter.ClearActivityLog();

        adapter.RecentCalls.Should().BeEmpty(
            "ClearActivityLog drops every entry in the ring buffer");
    }

    [Fact]
    public async Task ClearActivityLogCommand_FiresAdapterClearAndNotifiesUi()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        vm.RecentBridgeCalls.Should().HaveCount(1);

        vm.ClearActivityLogCommand.Execute(null);

        adapter.RecentCalls.Should().BeEmpty();
        vm.RecentBridgeCalls.Should().BeEmpty(
            "the VM's filtered list reflects the cleared adapter");
    }

    [Fact]
    public async Task CommandFilter_NarrowsToMatchingEntriesOnly()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);

        vm.RecentBridgeCalls.Should().HaveCount(3, "no filter applied yet");

        vm.ActivityLogCommandFilter = "GetVersion";
        vm.RecentBridgeCalls.Should().HaveCount(2,
            "filter narrows to the 2 GetVersion calls");
        vm.RecentBridgeCalls.Should().AllSatisfy(e =>
            e.LuaCommand.Should().Contain("GetVersion"));
    }

    [Fact]
    public async Task CommandFilter_IsCaseInsensitive()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);

        vm.ActivityLogCommandFilter = "GETVERSION";
        vm.RecentBridgeCalls.Should().HaveCount(1);

        vm.ActivityLogCommandFilter = "getversion";
        vm.RecentBridgeCalls.Should().HaveCount(1);

        vm.ActivityLogCommandFilter = "GetVersion";
        vm.RecentBridgeCalls.Should().HaveCount(1);
    }

    [Fact]
    public async Task CommandFilter_EmptyOrWhitespace_DoesNotFilter()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);

        vm.ActivityLogCommandFilter = "";
        vm.RecentBridgeCalls.Should().HaveCount(2);

        vm.ActivityLogCommandFilter = "   ";
        vm.RecentBridgeCalls.Should().HaveCount(2,
            "whitespace-only filter must NOT narrow the list");
    }

    [Fact]
    public async Task CommandFilter_ComposesWithErrorsOnlyToggle()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);

        // Compose by AND: both filters narrow simultaneously. With both
        // calls succeeding in the simulator, ErrorsOnly=true should drop
        // the visible list to zero regardless of the substring filter.
        vm.ActivityLogCommandFilter = "GetVersion";
        vm.RecentBridgeCalls.Should().HaveCount(1,
            "name filter alone narrows to the GetVersion call");

        vm.ActivityLogErrorsOnly = true;
        vm.RecentBridgeCalls.Should().BeEmpty(
            "AND-composed: GetVersion filter + ErrorsOnly = 0 (the GetVersion call succeeded)");

        vm.ActivityLogErrorsOnly = false;
        vm.RecentBridgeCalls.Should().HaveCount(1,
            "removing ErrorsOnly restores the GetVersion match");
    }
}
