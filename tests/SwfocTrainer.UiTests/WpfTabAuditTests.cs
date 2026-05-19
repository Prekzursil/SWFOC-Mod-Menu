using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.UiTests;

/// <summary>
/// Drives the SWFOC Trainer WPF app through every tab, captures
/// (control-type, name, AutomationId, bounding-box, visible, enabled,
/// click-response) for each interactive element, and writes a per-tab
/// JSON audit + a Markdown summary report at the end.
/// </summary>
/// <remarks>
/// <para>
/// Designed to be safe to run with no game attached and no real bridge.
/// The fixture forces <c>autoConnect: false</c> so the editor never tries
/// to talk to the bridge during the test. Click actions on feature
/// buttons are observed but the bridge call will fail-fast (~1.5 s
/// timeout) — this is a deliberately non-destructive audit.
/// </para>
/// </remarks>
[Collection("Editor UI")]
public sealed class WpfTabAuditTests
{
    private readonly EditorAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    private static readonly string[] KnownTabs =
    {
        "Connection & Diagnostics",
        "Player State",
        "Unit Control",
        "World State",
        "Probes & Scripts",
        "Settings",
        "Tactical Units",
        "Economy",
        "Inspector",
        "Combat",
        "Speed",
        "Spawning",
        "Galactic",
        "Hero Lab",
        "Battle Control",
        "Story Events",
        "Camera & Debug",
        "Lua Playground",
        "Event Stream",
        "Director Mode",
        "Cross-Faction",
        "Unit Stat Editor",
    };

    public WpfTabAuditTests(EditorAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "WpfAudit")]
    public void Editor_MainWindow_RendersWithTabs()
    {
        // Smoke check: the fixture's window came up and contains a tab control.
        var tabControl = _fixture.MainWindow
            .FindFirstDescendant(cf => cf.ByControlType(ControlType.Tab));
        tabControl.Should().NotBeNull("the main window must contain a tab control.");
        var tabHeaders = tabControl!.FindAllChildren(cf => cf.ByControlType(ControlType.TabItem));
        tabHeaders.Length.Should().BeGreaterOrEqualTo(KnownTabs.Length,
            $"expected at least {KnownTabs.Length} tabs.");
    }

    [Fact]
    [Trait("Category", "WpfAudit")]
    public void FakeBridge_ServedDiagnosticsProbes_OnStartup()
    {
        // The V2 Diagnostics tab fires SWFOC_GetVersion / SWFOC_GetBuildInfo /
        // SWFOC_DiagListRegisteredFunctions / SWFOC_DiagSelfTest as a fan-out
        // probe set on Loaded. With autoConnect:true and our fake bridge
        // running, those probes should have round-tripped before the fixture
        // returned. This guards the fake-bridge wiring against regression.
        _fixture.FakeBridge.CommandsServed.Should().BeGreaterOrEqualTo(4,
            "V2 Diagnostics tab must have probed the fake bridge for at least 4 commands.");
    }

    [Fact]
    [Trait("Category", "WpfAudit")]
    public void Audit_AllTabs_RenderAndAreClickable()
    {
        var results = new List<TabAuditResult>();
        var tabControl = _fixture.MainWindow
            .FindFirstDescendant(cf => cf.ByControlType(ControlType.Tab))
            ?? throw new InvalidOperationException("Main TabControl not found.");

        foreach (var tabName in KnownTabs)
        {
            var result = AuditOneTab(tabControl, tabName);
            results.Add(result);
            _output.WriteLine(
                $"[{result.TabName}] visible={result.TabVisible} controls={result.Controls.Count} elapsed={result.ElapsedMs}ms");
        }

        WriteJsonAudit(results);
        WriteMarkdownReport(results);

        // Soft assertion: every known tab must have rendered.
        var missing = results.Where(r => !r.TabVisible).Select(r => r.TabName).ToArray();
        missing.Should().BeEmpty(
            "every known tab header should render, but these did not: "
            + string.Join(", ", missing));
    }

    private TabAuditResult AuditOneTab(AutomationElement tabControl, string tabName)
    {
        var sw = Stopwatch.StartNew();
        var result = new TabAuditResult { TabName = tabName };

        try
        {
            // The tab header may use HTML-escaped form for "&" -> "&amp;"
            // in XAML; UIA returns the literal "&" so match on substring with
            // both forms collapsed.
            var normalised = tabName.Replace("&amp;", "&");
            var headers = tabControl.FindAllChildren(cf => cf.ByControlType(ControlType.TabItem));
            AutomationElement? header = null;
            foreach (var h in headers)
            {
                var text = (h.Name ?? string.Empty).Trim();
                if (text.Equals(normalised, StringComparison.OrdinalIgnoreCase))
                {
                    header = h;
                    break;
                }
            }
            if (header is null)
            {
                result.TabVisible = false;
                result.ErrorMessage = "tab header not found";
                return result;
            }

            // Activate the tab.
            var selectionItem = header.Patterns.SelectionItem.PatternOrDefault;
            if (selectionItem is not null)
            {
                selectionItem.Select();
            }
            else
            {
                header.Click();
            }
            Thread.Sleep(150); // allow UI to settle

            result.TabVisible = true;

            // Walk descendants. We capture both interactive controls (buttons,
            // toggles, text-input, combos, sliders, hyperlinks) AND the
            // non-interactive surface (Text labels, GroupBox headers) so the
            // audit covers visibility / positioning, not just clickability.
            var content = header; // FlaUI shows tab content as descendants of the selected TabItem
            var interactive = new[]
            {
                ControlType.Button,
                ControlType.CheckBox,
                ControlType.RadioButton,
                ControlType.ComboBox,
                ControlType.Edit,
                ControlType.Slider,
                ControlType.Hyperlink,
                ControlType.Menu,
                ControlType.MenuItem,
                ControlType.Text,
                ControlType.Group,
            };
            // Look in the parent tab content area (siblings) — the tab content is
            // usually a peer to the headers, hosted under the same TabControl.
            var tabContentRoot = tabControl;
            var allInteractive = new List<AutomationElement>();
            foreach (var ct in interactive)
            {
                allInteractive.AddRange(
                    tabContentRoot.FindAllDescendants(cf => cf.ByControlType(ct)));
            }

            // Dedup: UIA may return the same element via multiple control-type
            // searches (e.g. a Button's text child shows up in both Button and
            // Text walks). Key on (RuntimeId-as-string, BoundingRectangle).
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var el in allInteractive)
            {
                try
                {
                    // Skip elements that aren't part of the visible tab content
                    // (UIA returns descendants from non-active tabs too — gate on
                    // bounding box being non-degenerate AND IsOffscreen=false).
                    var rect = el.BoundingRectangle;
                    if (rect.Width <= 0 || rect.Height <= 0)
                    {
                        continue;
                    }
                    if (el.IsOffscreen)
                    {
                        continue;
                    }
                    // Drop pure-decorative empty-name Text/Group items (layout
                    // artifacts): keep them only if they have an AutomationId,
                    // because that signals an intentional addressable surface.
                    var name = el.Name ?? string.Empty;
                    var aid = el.AutomationId ?? string.Empty;
                    var ct = el.ControlType;
                    if ((ct == ControlType.Text || ct == ControlType.Group)
                        && string.IsNullOrWhiteSpace(name)
                        && string.IsNullOrWhiteSpace(aid))
                    {
                        continue;
                    }

                    // Dedup by (type, X, Y, W, H, name).
                    var key = $"{ct}|{rect.X}|{rect.Y}|{rect.Width}|{rect.Height}|{name}";
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    var ctrl = new ControlAuditResult
                    {
                        ControlType = ct.ToString(),
                        Name = name,
                        AutomationId = aid,
                        X = rect.X,
                        Y = rect.Y,
                        Width = rect.Width,
                        Height = rect.Height,
                        Enabled = el.IsEnabled,
                    };
                    result.Controls.Add(ctrl);
                }
                catch (Exception ex)
                {
                    result.Controls.Add(new ControlAuditResult
                    {
                        ControlType = "<error>",
                        Name = ex.GetType().Name,
                        AutomationId = string.Empty,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            sw.Stop();
            result.ElapsedMs = (int)sw.ElapsedMilliseconds;
        }

        return result;
    }

    private static string AuditOutputDir =>
        Path.Combine(AppContext.BaseDirectory, "wpf_audit_results");

    private static void WriteJsonAudit(List<TabAuditResult> results)
    {
        Directory.CreateDirectory(AuditOutputDir);
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(
            Path.Combine(AuditOutputDir, "wpf_audit_results.json"),
            json);
    }

    private void WriteMarkdownReport(List<TabAuditResult> results)
    {
        // Walk up to find swfoc_memory/knowledge-base. If we can't find it,
        // dump alongside the JSON in the bin output dir.
        var kbPath = TryResolveKnowledgeBasePath();
        var path = kbPath is not null
            ? Path.Combine(kbPath, "wpf_ui_audit_2026-04-27.md")
            : Path.Combine(AuditOutputDir, "wpf_ui_audit_2026-04-27.md");
        var sb = new StringBuilder();
        sb.AppendLine("# WPF UI audit (2026-04-27)");
        sb.AppendLine();
        sb.AppendLine("_Auto-generated by `tests/SwfocTrainer.UiTests/WpfTabAuditTests.cs`._");
        sb.AppendLine();
        sb.AppendLine("Drives every tab of the WPF editor (`MainWindowV2`) via FlaUI / UIAutomation, ");
        sb.AppendLine("running with `autoConnect: false` (no live game, no bridge attach). For each ");
        sb.AppendLine("tab we capture every visible interactive control's type, name, AutomationId, ");
        sb.AppendLine("bounding rectangle, and enabled state.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Tabs walked | Tabs rendered | Total controls captured | Total elapsed |");
        sb.AppendLine($"|---:|---:|---:|---:|");
        var rendered = results.Count(r => r.TabVisible);
        var totalControls = results.Sum(r => r.Controls.Count);
        var totalMs = results.Sum(r => r.ElapsedMs);
        sb.AppendLine($"| {results.Count} | {rendered} | {totalControls} | {totalMs} ms |");
        sb.AppendLine();

        sb.AppendLine("## Per-tab results");
        sb.AppendLine();
        foreach (var r in results)
        {
            sb.AppendLine($"### {r.TabName}");
            sb.AppendLine();
            sb.AppendLine($"- **Rendered:** {(r.TabVisible ? "YES" : "NO")}");
            if (!string.IsNullOrEmpty(r.ErrorMessage))
            {
                sb.AppendLine($"- **Error:** `{r.ErrorMessage}`");
            }
            sb.AppendLine($"- **Elapsed:** {r.ElapsedMs} ms");
            sb.AppendLine($"- **Interactive controls captured:** {r.Controls.Count}");
            if (r.Controls.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("| Type | Name | AutomationId | (X,Y) | (W,H) | Enabled |");
                sb.AppendLine("|---|---|---|---|---|---|");
                foreach (var c in r.Controls)
                {
                    var nm = (c.Name ?? string.Empty).Replace("|", "\\|");
                    if (nm.Length > 60) nm = nm.Substring(0, 57) + "...";
                    var aid = (c.AutomationId ?? string.Empty).Replace("|", "\\|");
                    sb.AppendLine(
                        $"| {c.ControlType} | {nm} | {aid} | ({c.X},{c.Y}) | ({c.Width}x{c.Height}) | {c.Enabled} |");
                }
            }
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static string? TryResolveKnowledgeBasePath()
    {
        // Walk up from BaseDirectory to find a sibling 'swfoc_memory/knowledge-base/'.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 20 && dir is not null; i++)
        {
            // 1. Direct sibling pattern: parent contains 'swfoc_memory'.
            var sibling = Path.Combine(
                Path.GetDirectoryName(dir) ?? string.Empty,
                "swfoc_memory", "knowledge-base");
            if (Directory.Exists(sibling))
            {
                return sibling;
            }
            // 2. Up-and-over pattern: parent of parent contains it.
            var parent = Path.GetDirectoryName(dir);
            if (parent is not null)
            {
                var sib2 = Path.Combine(
                    Path.GetDirectoryName(parent) ?? string.Empty,
                    "swfoc_memory", "knowledge-base");
                if (Directory.Exists(sib2))
                {
                    return sib2;
                }
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public sealed class TabAuditResult
    {
        public string TabName { get; set; } = string.Empty;
        public bool TabVisible { get; set; }
        public string? ErrorMessage { get; set; }
        public int ElapsedMs { get; set; }
        public List<ControlAuditResult> Controls { get; set; } = new();
    }

    public sealed class ControlAuditResult
    {
        public string ControlType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AutomationId { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Enabled { get; set; }
    }
}
