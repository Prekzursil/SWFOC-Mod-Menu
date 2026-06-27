using System.Threading;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.Infrastructure;

/// <summary>
/// 2026-04-28 (iter 75) — pins the new pinning surface on
/// <see cref="V2BridgeAdapter"/>. Operators bookmark interesting
/// activity entries so they survive the iter-45 ring rotation.
/// </summary>
public sealed class Iter75ActivityPinningTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        return (sim, new V2BridgeAdapter(pipe));
    }

    [Fact]
    public void PinnedCalls_StartsEmpty()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        adapter.PinnedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task PinActivity_AddsEntryToPinnedList()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        var entry = adapter.RecentCalls.Single();

        var added = adapter.PinActivity(entry);

        added.Should().BeTrue();
        adapter.PinnedCalls.Should().ContainSingle()
            .Which.Should().BeSameAs(entry);
    }

    [Fact]
    public async Task PinActivity_SameEntryTwice_NoOpsOnSecondCall()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        var entry = adapter.RecentCalls.Single();

        adapter.PinActivity(entry);
        adapter.PinActivity(entry);

        adapter.PinnedCalls.Should().HaveCount(1,
            "pinning the same entry twice mustn't duplicate it");
    }

    [Fact]
    public async Task UnpinActivity_RemovesEntry()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        var entry = adapter.RecentCalls.Single();
        adapter.PinActivity(entry);
        adapter.PinnedCalls.Should().HaveCount(1);

        adapter.UnpinActivity(entry);

        adapter.PinnedCalls.Should().BeEmpty();
    }

    [Fact]
    public void UnpinActivity_NotPinned_NoOps()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        var phantom = new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "X", true, "Y", 1);

        // Should not throw.
        adapter.UnpinActivity(phantom);
        adapter.PinnedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task PinnedEntry_SurvivesRingRotation()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        // Send 1 call, pin it, then push 60 more so the ring rotates
        // the pinned entry out. The pinned list must still have it.
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        var pinned = adapter.RecentCalls.Single();
        adapter.PinActivity(pinned);

        for (var i = 0; i < 60; i++)
        {
            await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);
        }
        adapter.RecentCalls.Should().HaveCount(50,
            "ring rotated old entries out (cap = 50)");
        adapter.RecentCalls.Should().NotContain(pinned,
            "the pinned entry is no longer in the rolling buffer");

        adapter.PinnedCalls.Should().ContainSingle()
            .Which.Should().BeSameAs(pinned,
                "but it's still in the pinned list — that's the point");
    }

    [Fact]
    public void PinActivity_HitsCapAt50_ReturnsFalse()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var entries = new List<BridgeActivityEntry>();
        for (var i = 0; i < 51; i++)
        {
            // Pump enough traffic that we have 51 unique entries — but the ring
            // caps at 50 so we'd never have 51 in RecentCalls. Use synthesised
            // entries instead, paired with PinActivity directly.
            entries.Add(new BridgeActivityEntry(
                DateTimeOffset.UtcNow, $"cmd_{i}", true, "ok", 1));
        }

        for (var i = 0; i < 50; i++)
        {
            adapter.PinActivity(entries[i]).Should().BeTrue($"pinning entry {i}");
        }
        var capHit = adapter.PinActivity(entries[50]);

        capHit.Should().BeFalse(
            "51st pin returns false — operator must unpin something first");
        adapter.PinnedCalls.Should().HaveCount(50,
            "cap respected: pinned list maxes at 50");
    }

    [Fact]
    public async Task ClearPinnedActivity_DropsEveryEntry()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);
        foreach (var e in adapter.RecentCalls) adapter.PinActivity(e);
        adapter.PinnedCalls.Should().HaveCount(2);

        adapter.ClearPinnedActivity();

        adapter.PinnedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearActivityLog_DoesNotTouchPinnedEntries()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        var entry = adapter.RecentCalls.Single();
        adapter.PinActivity(entry);

        adapter.ClearActivityLog();

        adapter.RecentCalls.Should().BeEmpty();
        adapter.PinnedCalls.Should().HaveCount(1,
            "clearing the rolling buffer must not drop bookmarks — that's why pinning exists");
    }

    [Fact]
    public void PinnedCalls_ReturnsSnapshotCopy()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        var snap1 = adapter.PinnedCalls;
        adapter.PinActivity(new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "X", true, "Y", 1));
        var snap2 = adapter.PinnedCalls;

        snap1.Should().BeEmpty();
        snap2.Should().HaveCount(1,
            "snapshot semantics: the earlier snap stays empty even after a pin");
    }
}
