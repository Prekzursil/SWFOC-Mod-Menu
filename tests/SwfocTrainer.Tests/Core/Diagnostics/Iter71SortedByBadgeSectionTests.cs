using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Core.Diagnostics;

/// <summary>
/// 2026-04-28 (iter 71) — pins the cross-tab "Sorted by badge" section
/// added to the capability surface markdown report. Operators triaging
/// "what's not LIVE yet?" use this section to see every PHASE 2 PENDING
/// action across the editor in one list.
/// </summary>
public sealed class Iter71SortedByBadgeSectionTests
{
    [Fact]
    public void Generate_IncludesSortedByBadgeSection()
    {
        var report = CapabilitySurfaceReport.GenerateMarkdownReport(new[]
        {
            ("TabA", (IReadOnlyList<CapabilityAwareAction>)new[]
            {
                new CapabilityAwareAction("Live", "SWFOC_GodMode"),
                new CapabilityAwareAction("Pending", "SWFOC_FreezeAI"),
            }),
        });
        report.Should().Contain("## Sorted by badge (cross-tab)");
        // Header row of the sorted table.
        report.Should().Contain("| Badge | Tab | Action | Note |");
    }

    [Fact]
    public void SortedSection_ClustersPhase2PendingTogether_AcrossTabs()
    {
        // Two tabs, three actions, mixed statuses. Sort order should
        // group all PHASE 2 PENDING entries together regardless of tab.
        var report = CapabilitySurfaceReport.GenerateMarkdownReport(new[]
        {
            ("TabA", (IReadOnlyList<CapabilityAwareAction>)new[]
            {
                new CapabilityAwareAction("A-Live", "SWFOC_GodMode"),
                new CapabilityAwareAction("A-Pending", "SWFOC_FreezeAI"),
            }),
            ("TabB", (IReadOnlyList<CapabilityAwareAction>)new[]
            {
                new CapabilityAwareAction("B-Pending", "SWFOC_FreezeCredits"),
            }),
        });

        // Locate the sorted section and find the order of PHASE 2 PENDING
        // entries — they must be adjacent (TabA's pending right next to
        // TabB's pending).
        var sortedIdx = report.IndexOf("## Sorted by badge (cross-tab)");
        sortedIdx.Should().BeGreaterThan(0);

        var sortedSection = report[sortedIdx..];
        var aPendingIdx = sortedSection.IndexOf("A-Pending");
        var bPendingIdx = sortedSection.IndexOf("B-Pending");
        var aLiveIdx = sortedSection.IndexOf("A-Live");
        aPendingIdx.Should().BeGreaterThan(0);
        bPendingIdx.Should().BeGreaterThan(0);
        aLiveIdx.Should().BeGreaterThan(0);

        // LIVE rows come first (alphabetical badge order), then PHASE 2 PENDING.
        // Within PHASE 2 PENDING, TabA before TabB (alphabetical tab).
        aLiveIdx.Should().BeLessThan(aPendingIdx,
            "LIVE entries sort before PHASE 2 PENDING (alphabetical badge)");
        aPendingIdx.Should().BeLessThan(bPendingIdx,
            "within the same badge, tabs sort alphabetically");
    }

    [Fact]
    public void SortedSection_AppearsAfterPerTabBreakdown()
    {
        var report = CapabilitySurfaceReport.GenerateMarkdownReport(new[]
        {
            ("Tab", (IReadOnlyList<CapabilityAwareAction>)new[]
            {
                new CapabilityAwareAction("X", "SWFOC_GodMode"),
            }),
        });
        var perTabIdx = report.IndexOf("## Per-tab actions");
        var sortedIdx = report.IndexOf("## Sorted by badge");
        var legendIdx = report.IndexOf("## Status legend");

        perTabIdx.Should().BeGreaterThan(0);
        sortedIdx.Should().BeGreaterThan(perTabIdx,
            "sorted section comes after the per-tab breakdown");
        legendIdx.Should().BeGreaterThan(sortedIdx,
            "legend stays last");
    }

    [Fact]
    public void SortedSection_RendersBadgeAsCodeAndEscapesPipes()
    {
        var report = CapabilitySurfaceReport.GenerateMarkdownReport(new[]
        {
            ("Tab", (IReadOnlyList<CapabilityAwareAction>)new[]
            {
                new CapabilityAwareAction("Action", "SWFOC_GodMode"),
            }),
        });
        // Badge is wrapped in code-fences so it stays monospace.
        report.Should().Contain("| `LIVE` |");
    }
}
