using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Core.Diagnostics;

/// <summary>
/// 2026-04-28 (iter 68) — pins the embedding of the iter-67 trend line
/// inside the iter-61 capability surface markdown report.
/// </summary>
public sealed class Iter68SurfaceReportTrendEmbedTests
{
    private static readonly (string TabName, IReadOnlyList<CapabilityAwareAction> Actions)[] SampleTabs = new[]
    {
        ("Sample", (IReadOnlyList<CapabilityAwareAction>)new[]
        {
            new CapabilityAwareAction("Live action", "SWFOC_GodMode"),
            new CapabilityAwareAction("Pending", "SWFOC_FreezeAI"),
        }),
    };

    [Fact]
    public void Generate_NoHistory_OmitsTrendLine()
    {
        var report = CapabilitySurfaceReport.GenerateMarkdownReport(SampleTabs);
        report.Should().NotContain("**Trend:**",
            "no history → no trend line section");
    }

    [Fact]
    public void Generate_EmptyHistory_OmitsTrendLine()
    {
        var report = CapabilitySurfaceReport.GenerateMarkdownReport(
            SampleTabs,
            Array.Empty<CapabilitySurfaceHistory.HistoryEntry>());
        report.Should().NotContain("**Trend:**");
    }

    [Fact]
    public void Generate_OneHistoryEntry_OmitsTrendLine()
    {
        var history = new[]
        {
            new CapabilitySurfaceHistory.HistoryEntry("2026-04-28", 96, 53, 3, 37, 3, 0, 58),
        };
        var report = CapabilitySurfaceReport.GenerateMarkdownReport(SampleTabs, history);
        report.Should().NotContain("**Trend:**",
            "single-entry history can't form a trend");
    }

    [Fact]
    public void Generate_TwoHistoryEntries_EmbedsTrendUnderHeadline()
    {
        var history = new[]
        {
            new CapabilitySurfaceHistory.HistoryEntry("2026-04-26", 90, 50, 2, 38, 0, 0, 56),
            new CapabilitySurfaceHistory.HistoryEntry("2026-04-28", 96, 53, 3, 37, 3, 0, 58),
        };
        var report = CapabilitySurfaceReport.GenerateMarkdownReport(SampleTabs, history);
        report.Should().Contain("**Trend:**");
        report.Should().Contain("58%");
        report.Should().Contain("was 56%");
        report.Should().Contain("+2pp");
        // Trend should appear before the Roll-up section so operators
        // see it as a headline metric.
        var trendIdx = report.IndexOf("**Trend:**");
        var rollupIdx = report.IndexOf("## Roll-up by badge");
        trendIdx.Should().BeLessThan(rollupIdx,
            "trend line is part of the headline, not the body");
    }

    [Fact]
    public void Generate_FallingTrend_RendersNegativeDelta()
    {
        var history = new[]
        {
            new CapabilitySurfaceHistory.HistoryEntry("2026-04-26", 90, 60, 0, 30, 0, 0, 67),
            new CapabilitySurfaceHistory.HistoryEntry("2026-04-28", 96, 53, 3, 37, 3, 0, 58),
        };
        var report = CapabilitySurfaceReport.GenerateMarkdownReport(SampleTabs, history);
        report.Should().Contain("-9pp");
    }
}
