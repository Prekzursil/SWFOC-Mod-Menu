using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 83) — pins the Pinned-calls expander header text
/// to include a bookmark count when N &gt; 0. Operators see at-a-glance
/// how many calls they've stashed without expanding the section. Empty
/// state reverts to "Pinned calls" so the UI stays clean when there
/// are no bookmarks.
/// </summary>
public sealed class Iter83PinnedCountHeaderTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter, DiagnosticsTabViewModel vm) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var settings = new V2Settings();
        return (sim, adapter, new DiagnosticsTabViewModel(adapter, settings));
    }

    private static BridgeActivityEntry Entry(string lua) =>
        new BridgeActivityEntry(
            Timestamp: DateTimeOffset.UtcNow,
            LuaCommand: lua,
            Succeeded: true,
            ResponseOrError: "OK",
            DurationMs: 5);

    [Fact]
    public void PinnedHeader_NoBookmarks_ShowsPlainText()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.PinnedBridgeCallsHeader.Should().Be("Pinned calls");
    }

    [Fact]
    public void PinnedHeader_OneBookmark_ShowsSingularSuffix()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        var entry = Entry("return SWFOC_X(1)");
        adapter.RecordForTest(entry);
        adapter.PinActivity(entry);

        vm.PinnedBridgeCallsHeader.Should().Be("Pinned calls (1 bookmark)",
            "singular form for a single pinned entry");
    }

    [Fact]
    public void PinnedHeader_MultipleBookmarks_ShowsPluralSuffix()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        for (int i = 0; i < 5; i++)
        {
            var entry = Entry($"return SWFOC_X({i})");
            adapter.RecordForTest(entry);
            adapter.PinActivity(entry);
        }

        vm.PinnedBridgeCallsHeader.Should().Be("Pinned calls (5 bookmarks)",
            "plural form for 5 pinned entries");
    }

    [Fact]
    public void PinnedHeader_ClearPinned_RevertsToPlain()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        var entry = Entry("return SWFOC_X(1)");
        adapter.RecordForTest(entry);
        adapter.PinActivity(entry);
        vm.PinnedBridgeCallsHeader.Should().Contain("(1 bookmark)");

        adapter.ClearPinnedActivity();

        vm.PinnedBridgeCallsHeader.Should().Be("Pinned calls",
            "header reverts to plain text after clear");
    }
}
