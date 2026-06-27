using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Core.Diagnostics;

/// <summary>
/// 2026-04-28 (iter 72) — pins the structured JSON export of the
/// capability surface. Schema is stable; downstream consumers
/// (tools/scripts/CI) rely on field names + types.
/// </summary>
public sealed class Iter72CapabilitySurfaceJsonTests
{
    private static readonly (string TabName, IReadOnlyList<CapabilityAwareAction> Actions)[] SampleTabs = new[]
    {
        ("Combat", (IReadOnlyList<CapabilityAwareAction>)new[]
        {
            new CapabilityAwareAction("Toggle god mode", "SWFOC_GodMode"),
            new CapabilityAwareAction("Set damage", "SWFOC_SetDamageMultiplier"),
        }),
        ("Inspector", (IReadOnlyList<CapabilityAwareAction>)new[]
        {
            new CapabilityAwareAction("Refresh inspector", "SWFOC_InspectUnit"),
        }),
    };

    [Fact]
    public void Generate_ProducesValidJson()
    {
        var json = CapabilitySurfaceReport.GenerateJsonReport(SampleTabs);
        // Must round-trip through JsonDocument without throwing.
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void Generate_UsesCamelCasePropertyNames()
    {
        var json = CapabilitySurfaceReport.GenerateJsonReport(SampleTabs);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("generatedUtc", out _).Should().BeTrue();
        root.TryGetProperty("rollup", out _).Should().BeTrue();
        root.TryGetProperty("trend", out _).Should().BeTrue();
        root.TryGetProperty("tabs", out _).Should().BeTrue();
        // No PascalCase variants leak through.
        root.TryGetProperty("GeneratedUtc", out _).Should().BeFalse();
    }

    [Fact]
    public void Generate_RollupCountsMatchInput()
    {
        var json = CapabilitySurfaceReport.GenerateJsonReport(SampleTabs);
        using var doc = JsonDocument.Parse(json);
        var rollup = doc.RootElement.GetProperty("rollup");
        rollup.GetProperty("totalActions").GetInt32().Should().Be(3);
        // Combat has 1 LIVE (GodMode), Inspector has 1 LIVE ONLY,
        // SetDamage is PHASE 2 PENDING.
        rollup.GetProperty("liveCount").GetInt32().Should().Be(1);
        rollup.GetProperty("liveOnlyCount").GetInt32().Should().Be(1);
        rollup.GetProperty("phase2PendingCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Generate_TabsArrayPreservesInputOrder()
    {
        var json = CapabilitySurfaceReport.GenerateJsonReport(SampleTabs);
        using var doc = JsonDocument.Parse(json);
        var tabs = doc.RootElement.GetProperty("tabs");
        tabs.GetArrayLength().Should().Be(2);
        tabs[0].GetProperty("tabName").GetString().Should().Be("Combat");
        tabs[1].GetProperty("tabName").GetString().Should().Be("Inspector");
    }

    [Fact]
    public void Generate_ActionsCarryBadgeAndNote()
    {
        var json = CapabilitySurfaceReport.GenerateJsonReport(SampleTabs);
        using var doc = JsonDocument.Parse(json);
        var combat = doc.RootElement.GetProperty("tabs")[0];
        var actions = combat.GetProperty("actions");
        actions.GetArrayLength().Should().Be(2);
        var godMode = actions[0];
        godMode.GetProperty("actionName").GetString().Should().Be("Toggle god mode");
        godMode.GetProperty("badge").GetString().Should().Be("LIVE");
        godMode.GetProperty("note").GetString().Should().Contain("Hardpoint-behavior");
    }

    [Fact]
    public void Generate_NoHistory_TrendIsEmpty()
    {
        var json = CapabilitySurfaceReport.GenerateJsonReport(SampleTabs);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("trend").GetString().Should().BeEmpty();
    }

    [Fact]
    public void Generate_WithHistory_TrendPopulated()
    {
        var history = new[]
        {
            new CapabilitySurfaceHistory.HistoryEntry("2026-04-26", 90, 50, 2, 38, 0, 0, 56),
            new CapabilitySurfaceHistory.HistoryEntry("2026-04-28", 96, 53, 3, 37, 3, 0, 58),
        };
        var json = CapabilitySurfaceReport.GenerateJsonReport(SampleTabs, history);
        using var doc = JsonDocument.Parse(json);
        var trend = doc.RootElement.GetProperty("trend").GetString();
        trend.Should().Contain("58%");
        trend.Should().Contain("+2pp");
    }

    [Fact]
    public void Generate_FixedTimestamp_IsByteStable()
    {
        var fixedTs = new DateTimeOffset(2026, 4, 28, 12, 0, 0, TimeSpan.Zero);
        var json1 = CapabilitySurfaceReport.GenerateJsonReport(SampleTabs, generatedUtc: fixedTs);
        var json2 = CapabilitySurfaceReport.GenerateJsonReport(SampleTabs, generatedUtc: fixedTs);
        json1.Should().Be(json2,
            "same input + same timestamp = byte-stable output (drift-protection-friendly)");
    }

    [Fact]
    public void Generate_PrettyPrinted_IncludesNewlinesForReadability()
    {
        var json = CapabilitySurfaceReport.GenerateJsonReport(SampleTabs);
        json.Should().Contain("\n");
        json.Should().Contain("  ", "indented for diff-friendly on-disk file");
    }

    [Fact]
    public void Generate_TrailingNewline_PreventsLineEndingDrift()
    {
        var json = CapabilitySurfaceReport.GenerateJsonReport(SampleTabs);
        json.Should().EndWith("\n",
            "trailing newline keeps the file POSIX-clean and makes diffs end on a record");
    }
}
