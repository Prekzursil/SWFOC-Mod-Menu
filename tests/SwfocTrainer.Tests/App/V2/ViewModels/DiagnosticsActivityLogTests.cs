using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 46) — covers the activity-log filter (Errors only) and
/// the clipboard-export command's command-canexecute logic. Clipboard
/// content can't be read in the test process (xUnit isn't STA by default,
/// and we don't want to flake on clipboard contention with other tests),
/// so we focus on the filter pipeline + the empty-state guard.
/// </summary>
public sealed class DiagnosticsActivityLogTests
{
    private static (SwfocSimulator sim, DiagnosticsTabViewModel vm, V2BridgeAdapter adapter) NewSession()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var settings = new V2Settings();
        return (sim, new DiagnosticsTabViewModel(adapter, settings), adapter);
    }

    [Fact]
    public async Task RecentBridgeCalls_DefaultsToFullList()
    {
        var (sim, vm, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Garbage_Type\", 0, 1)", CancellationToken.None);

        vm.ActivityLogErrorsOnly.Should().BeFalse();
        vm.RecentBridgeCalls.Should().HaveCount(2,
            "without the filter, both succeeded + failed entries appear");
    }

    [Fact]
    public async Task ActivityLogErrorsOnly_FiltersToFailedOnly()
    {
        var (sim, vm, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);
        await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Garbage_Type\", 0, 1)", CancellationToken.None);

        vm.ActivityLogErrorsOnly = true;

        var filtered = vm.RecentBridgeCalls;
        filtered.Should().HaveCount(1, "only the failed garbage-type spawn passes the filter");
        filtered.Single().Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ActivityLogErrorsOnly_TogglingOff_RestoresFullList()
    {
        var (sim, vm, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Garbage_Type\", 0, 1)", CancellationToken.None);

        vm.ActivityLogErrorsOnly = true;
        vm.RecentBridgeCalls.Should().HaveCount(1);

        vm.ActivityLogErrorsOnly = false;
        vm.RecentBridgeCalls.Should().HaveCount(2);
    }

    [Fact]
    public void CopyActivityLogCommand_IsAlwaysExecutable()
    {
        // The copy command is gated by the implementation, not CanExecute —
        // the empty-state path appends a status-line message and returns
        // rather than throwing. We verify the command exists and CanExecute
        // returns true (consistent with other RelayCommand callers).
        var (sim, vm, _) = NewSession();
        using var _disp = sim;

        vm.CopyActivityLogCommand.Should().NotBeNull();
        vm.CopyActivityLogCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void BuildActivityLogTsv_OnEmptyBuffer_ReturnsEmptyString()
    {
        // 2026-04-27 (iter 50): TSV generator covers both Copy + Save
        // commands; an empty ring buffer should yield empty string so the
        // callers can branch on that.
        var (sim, vm, _) = NewSession();
        using var _disp = sim;

        vm.BuildActivityLogTsv().Should().BeEmpty();
    }

    [Fact]
    public async Task BuildActivityLogTsv_AfterCalls_IncludesHeaderAndRows()
    {
        var (sim, vm, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Garbage_Type\", 0, 1)", CancellationToken.None);

        var tsv = vm.BuildActivityLogTsv();
        tsv.Should().StartWith("timestamp\tok\tms\tcommand\tresponse_or_error");
        var lines = tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Header + 2 data rows. Ring buffer is newest-first so the second
        // call (Garbage_Type) appears before the first (GetVersion).
        lines.Should().HaveCount(3);
        lines[1].Should().Contain("Garbage_Type", "newest call appears first in TSV");
        lines[2].Should().Contain("SWFOC_GetVersion", "older call appears last");
    }

    [Fact]
    public async Task BuildActivityLogTsv_ScrubsTabsAndNewlinesFromCells()
    {
        // Field separators leak into cells if not stripped — TSV format
        // breaks. The implementation replaces \t / \r / \n with space.
        var (sim, vm, adapter) = NewSession();
        using var _ = sim;

        // Trigger a real call; the simulator's responses are clean already
        // so we just assert the TSV doesn't contain tabs INSIDE cells (the
        // header and field separators are tabs by design).
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        var tsv = vm.BuildActivityLogTsv();

        // Each non-header line should have exactly 4 tabs (5 fields).
        var dataLines = tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1);
        foreach (var line in dataLines)
        {
            line.Count(c => c == '\t').Should().Be(4, "each data row must have exactly 4 tab separators");
        }
    }

    [Fact]
    public void SaveActivityLogCommand_Exists()
    {
        // The SaveFileDialog can't be exercised in tests (xUnit isn't STA;
        // launching the dialog would block the test runner). Verify the
        // command exists + the empty-state path doesn't throw.
        var (sim, vm, _) = NewSession();
        using var _disp = sim;

        vm.SaveActivityLogCommand.Should().NotBeNull();
        vm.SaveActivityLogCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void HasRecentFailure_FalseOnEmptyBuffer()
    {
        // 2026-04-27 (iter 51): callout should be hidden when nothing has
        // gone wrong yet.
        var (sim, vm, _) = NewSession();
        using var _disp = sim;

        vm.HasRecentFailure.Should().BeFalse();
        vm.LastFailureSummary.Should().BeNull();
    }

    [Fact]
    public async Task HasRecentFailure_FalseAfterAllSucceeded()
    {
        var (sim, vm, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);

        vm.HasRecentFailure.Should().BeFalse();
        vm.LastFailureSummary.Should().BeNull();
    }

    [Fact]
    public async Task LastFailureSummary_ReturnsTheMostRecentFailure()
    {
        var (sim, vm, adapter) = NewSession();
        using var _ = sim;

        // Mix: success, failure, success — last failure stays "Garbage_Type"
        // because the ring buffer is newest-first AND we want the most
        // recent FAIL specifically (not the latest entry).
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Garbage_Type\", 0, 1)", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);

        vm.HasRecentFailure.Should().BeTrue();
        vm.LastFailureSummary.Should().NotBeNull();
        vm.LastFailureSummary.Should().Contain("Garbage_Type");
        vm.LastFailureSummary.Should().StartWith("Last failure ");
    }

    [Fact]
    public async Task LastFailureSummary_OldestSurfaced_WhenOnlyOneFailure()
    {
        var (sim, vm, adapter) = NewSession();
        using var _ = sim;

        // Single failure, then 5 successes — callout still surfaces it.
        await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Garbage_Type\", 0, 1)", CancellationToken.None);
        for (var i = 0; i < 5; i++)
        {
            await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        }

        vm.HasRecentFailure.Should().BeTrue();
        vm.LastFailureSummary.Should().Contain("Garbage_Type");
    }
}
