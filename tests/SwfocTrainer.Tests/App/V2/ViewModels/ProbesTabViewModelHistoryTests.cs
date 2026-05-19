using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// Iter 21 (2026-04-27) — recent-commands ring on the Probes / Lua
/// Playground tab. We can't drive the real bridge round-trip in a unit
/// test, but the history mutation logic is the part that's worth
/// covering anyway: dedupe, bounding, recall, clear.
/// </summary>
public sealed class ProbesTabViewModelHistoryTests
{
    private static ProbesTabViewModel CreateVm()
    {
        // V2BridgeAdapter wraps a NamedPipeLuaBridgeClient. Pointing it at a
        // guaranteed-not-running pipe is fine — none of these tests trigger
        // the SendAsync round-trip; they exercise PushHistory and the
        // recall/clear commands directly, bypassing the network.
        var pipe = new NamedPipeLuaBridgeClient(
            pipeName: "swfoc_probes_history_unittest_" + System.Guid.NewGuid().ToString("N"),
            connectTimeoutMs: 50,
            readTimeoutMs: 50);
        var adapter = new V2BridgeAdapter(pipe);
        return new ProbesTabViewModel(adapter);
    }

    [Fact]
    public void History_StartsEmpty()
    {
        var vm = CreateVm();
        vm.History.Should().BeEmpty();
        vm.SelectedHistory.Should().BeNull();
    }

    [Fact]
    public void PushHistory_AddsEntryToHead()
    {
        var vm = CreateVm();
        vm.PushHistory("return SWFOC_GetAllPlayers()");
        vm.History.Should().ContainSingle()
            .Which.Should().Be("return SWFOC_GetAllPlayers()");
    }

    [Fact]
    public void PushHistory_NewestFirst()
    {
        var vm = CreateVm();
        vm.PushHistory("first");
        vm.PushHistory("second");
        vm.PushHistory("third");
        vm.History.Should().HaveCount(3);
        vm.History[0].Should().Be("third");
        vm.History[1].Should().Be("second");
        vm.History[2].Should().Be("first");
    }

    [Fact]
    public void PushHistory_TrimsToBound()
    {
        var vm = CreateVm();
        for (int i = 0; i < ProbesTabViewModel.MaxHistoryEntries + 5; i++)
        {
            vm.PushHistory("probe_" + i);
        }
        vm.History.Should().HaveCount(ProbesTabViewModel.MaxHistoryEntries);
        // The newest entry is at the head, the oldest survivor is at the tail.
        vm.History[0].Should().Be("probe_" + (ProbesTabViewModel.MaxHistoryEntries + 4));
    }

    [Fact]
    public void PushHistory_Dedupe_MovesExistingToHead()
    {
        var vm = CreateVm();
        vm.PushHistory("alpha");
        vm.PushHistory("beta");
        vm.PushHistory("gamma");
        vm.PushHistory("alpha"); // resending alpha should not duplicate
        vm.History.Should().HaveCount(3);
        vm.History[0].Should().Be("alpha");
        vm.History[1].Should().Be("gamma");
        vm.History[2].Should().Be("beta");
    }

    [Fact]
    public void PushHistory_Dedupe_HeadResendIsNoOp()
    {
        var vm = CreateVm();
        vm.PushHistory("only");
        vm.PushHistory("only");
        vm.History.Should().ContainSingle()
            .Which.Should().Be("only");
    }

    [Fact]
    public void PushHistory_TrimsWhitespace()
    {
        var vm = CreateVm();
        vm.PushHistory("   probe   ");
        vm.History[0].Should().Be("probe");
    }

    [Fact]
    public void PushHistory_IgnoresEmpty()
    {
        var vm = CreateVm();
        vm.PushHistory(string.Empty);
        vm.PushHistory("   ");
        vm.PushHistory(null!);
        vm.History.Should().BeEmpty();
    }

    [Fact]
    public void RecallSelectedHistoryCommand_PullsIntoLuaInput()
    {
        var vm = CreateVm();
        vm.PushHistory("return SWFOC_GetAllPlayers()");
        vm.SelectedHistory = "return SWFOC_GetAllPlayers()";
        vm.RecallSelectedHistoryCommand.Execute(null);
        vm.LuaInput.Should().Be("return SWFOC_GetAllPlayers()");
    }

    [Fact]
    public void RecallSelectedHistoryCommand_Disabled_WhenNothingSelected()
    {
        var vm = CreateVm();
        vm.RecallSelectedHistoryCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ClearHistoryCommand_EmptiesTheRing()
    {
        var vm = CreateVm();
        vm.PushHistory("a");
        vm.PushHistory("b");
        vm.History.Should().HaveCount(2);
        vm.ClearHistoryCommand.Execute(null);
        vm.History.Should().BeEmpty();
    }

    [Fact]
    public void ClearHistoryCommand_Disabled_WhenAlreadyEmpty()
    {
        var vm = CreateVm();
        vm.ClearHistoryCommand.CanExecute(null).Should().BeFalse();
    }
}
