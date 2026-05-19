using System.IO;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Core.Diagnostics;

/// <summary>
/// 2026-04-28 (iter 67) — pins the
/// <see cref="CapabilitySurfaceHistory"/> JSON-lines append-only
/// history file. Operators read this back to see engine-effectiveness
/// trends as Phase 2 hooks land.
/// </summary>
public sealed class Iter67CapabilitySurfaceHistoryTests
{
    private static string TempPath() => Path.Combine(
        Path.GetTempPath(),
        $"iter67_history_{Guid.NewGuid():N}.jsonl");

    private static CapabilitySurfaceReport.SurfaceRollup MakeRollup(
        int total = 96, int live = 53, int liveOnly = 3, int phase2 = 37,
        int mixed = 3, int other = 0) =>
        new(total, live, liveOnly, phase2, mixed, other);

    [Fact]
    public void LoadAll_NoFile_ReturnsEmpty()
    {
        var path = TempPath();
        File.Exists(path).Should().BeFalse();
        CapabilitySurfaceHistory.LoadAll(path).Should().BeEmpty(
            "no file = no history; safe to call unconditionally");
    }

    [Fact]
    public void Record_FirstEntry_CreatesFileWithOneLine()
    {
        var path = TempPath();
        try
        {
            CapabilitySurfaceHistory.Record(MakeRollup(), path, DateTimeOffset.UtcNow);
            var lines = File.ReadAllLines(path);
            lines.Should().HaveCount(1);
            var entry = CapabilitySurfaceHistory.LoadAll(path).Single();
            entry.TotalActions.Should().Be(96);
            entry.LiveCount.Should().Be(53);
            entry.LivePercent.Should().Be(58); // (53+3)/96 = 58.3 → 58
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Record_SameDateTwice_Deduplicates()
    {
        var path = TempPath();
        try
        {
            var date = new DateTimeOffset(2026, 4, 28, 12, 0, 0, TimeSpan.Zero);
            CapabilitySurfaceHistory.Record(MakeRollup(live: 50), path, date);
            CapabilitySurfaceHistory.Record(MakeRollup(live: 53), path, date);

            var entries = CapabilitySurfaceHistory.LoadAll(path);
            entries.Should().HaveCount(1,
                "same-date entries dedup so the file stays small (one snapshot per day)");
            entries.Single().LiveCount.Should().Be(53,
                "later record wins — captures the most recent rollup for that day");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Record_DifferentDates_AppendsBothEntries()
    {
        var path = TempPath();
        try
        {
            var day1 = new DateTimeOffset(2026, 4, 27, 12, 0, 0, TimeSpan.Zero);
            var day2 = new DateTimeOffset(2026, 4, 28, 12, 0, 0, TimeSpan.Zero);
            CapabilitySurfaceHistory.Record(MakeRollup(live: 52), path, day1);
            CapabilitySurfaceHistory.Record(MakeRollup(live: 53), path, day2);

            var entries = CapabilitySurfaceHistory.LoadAll(path);
            entries.Should().HaveCount(2);
            entries[0].Date.Should().Be("2026-04-27");
            entries[1].Date.Should().Be("2026-04-28");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Record_PreservesChronologicalOrder()
    {
        var path = TempPath();
        try
        {
            // Insert out of order — Record sorts before writing.
            var d1 = new DateTimeOffset(2026, 4, 28, 0, 0, 0, TimeSpan.Zero);
            var d2 = new DateTimeOffset(2026, 4, 26, 0, 0, 0, TimeSpan.Zero);
            var d3 = new DateTimeOffset(2026, 4, 27, 0, 0, 0, TimeSpan.Zero);
            CapabilitySurfaceHistory.Record(MakeRollup(live: 53), path, d1);
            CapabilitySurfaceHistory.Record(MakeRollup(live: 50), path, d2);
            CapabilitySurfaceHistory.Record(MakeRollup(live: 52), path, d3);

            var entries = CapabilitySurfaceHistory.LoadAll(path);
            entries.Select(e => e.Date).Should().BeInAscendingOrder();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LoadAll_TolerantOfCorruptLines()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path,
                "{\"Date\":\"2026-04-27\",\"TotalActions\":94,\"LiveCount\":52,\"LiveOnlyCount\":3,\"Phase2PendingCount\":37,\"MixedCount\":2,\"OtherCount\":0,\"LivePercent\":58}\n"
                + "this is not json and should be skipped\n"
                + "{\"Date\":\"2026-04-28\",\"TotalActions\":96,\"LiveCount\":53,\"LiveOnlyCount\":3,\"Phase2PendingCount\":37,\"MixedCount\":3,\"OtherCount\":0,\"LivePercent\":58}\n");

            var entries = CapabilitySurfaceHistory.LoadAll(path);
            entries.Should().HaveCount(2,
                "corrupt lines are tolerated — operator hand-edits don't brick the report");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void BuildTrendLine_NoHistory_ReturnsEmpty()
    {
        CapabilitySurfaceHistory.BuildTrendLine(Array.Empty<CapabilitySurfaceHistory.HistoryEntry>())
            .Should().BeEmpty();
    }

    [Fact]
    public void BuildTrendLine_OneEntry_ReturnsEmpty()
    {
        var single = new[]
        {
            new CapabilitySurfaceHistory.HistoryEntry(
                "2026-04-28", 96, 53, 3, 37, 3, 0, 58),
        };
        CapabilitySurfaceHistory.BuildTrendLine(single).Should().BeEmpty(
            "trend needs at least two data points");
    }

    [Fact]
    public void BuildTrendLine_RisingPercent_ShowsPlusDelta()
    {
        var entries = new[]
        {
            new CapabilitySurfaceHistory.HistoryEntry("2026-04-26", 90, 50, 2, 38, 0, 0, 56),
            new CapabilitySurfaceHistory.HistoryEntry("2026-04-28", 96, 53, 3, 37, 3, 0, 58),
        };
        var trend = CapabilitySurfaceHistory.BuildTrendLine(entries);
        trend.Should().Contain("58%");
        trend.Should().Contain("was 56%");
        trend.Should().Contain("+2pp");
        trend.Should().Contain("2 entries");
    }

    [Fact]
    public void BuildTrendLine_FallingPercent_ShowsNegativeDelta()
    {
        var entries = new[]
        {
            new CapabilitySurfaceHistory.HistoryEntry("2026-04-26", 90, 60, 0, 30, 0, 0, 67),
            new CapabilitySurfaceHistory.HistoryEntry("2026-04-28", 96, 53, 3, 37, 3, 0, 58),
        };
        var trend = CapabilitySurfaceHistory.BuildTrendLine(entries);
        trend.Should().Contain("58%");
        trend.Should().Contain("-9pp",
            "negative deltas are reported with explicit minus sign");
    }
}
