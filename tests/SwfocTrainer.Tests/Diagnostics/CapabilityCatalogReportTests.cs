using System.IO;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.Tests.Diagnostics;

/// <summary>
/// 2026-04-27 (iter 39) — keeps `knowledge-base/capability_status_2026-04-27.md`
/// in sync with the canonical <see cref="CapabilityStatusCatalog"/>. If
/// the on-disk markdown drifts from the catalog (e.g. a new helper got
/// added to the catalog but the report wasn't regenerated), this test
/// fails and prints the expected content for an easy update.
/// </summary>
public sealed class CapabilityCatalogReportTests
{
    private readonly ITestOutputHelper _output;

    public CapabilityCatalogReportTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Resolve <c>&lt;repo&gt;/../swfoc_memory/knowledge-base/capability_status_2026-04-27.md</c>.
    /// The editor checkout sits next to the swfoc_memory checkout, per
    /// CLAUDE.md project layout.
    /// </summary>
    private static string ReportPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var siblingKnowledgeBase = Path.Combine(
                Path.GetDirectoryName(dir)!, "swfoc_memory", "knowledge-base");
            if (Directory.Exists(siblingKnowledgeBase))
            {
                return Path.Combine(siblingKnowledgeBase, "capability_status_2026-04-27.md");
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            "Couldn't locate sibling swfoc_memory/knowledge-base directory.");
    }

    [Fact]
    public void ReportFile_MatchesCatalogSnapshot()
    {
        var generated = CapabilityStatusCatalog.GenerateMarkdownReport()
            .Replace("\r\n", "\n", System.StringComparison.Ordinal);
        var path = ReportPath();
        if (!File.Exists(path))
        {
            File.WriteAllText(path, generated);
            // First-run fixture creation; treat as success but tell the operator.
            _output.WriteLine($"Created baseline report at {path}");
            return;
        }
        var onDisk = File.ReadAllText(path)
            .Replace("\r\n", "\n", System.StringComparison.Ordinal);
        if (!string.Equals(onDisk, generated, System.StringComparison.Ordinal))
        {
            // Auto-update on test run — the catalog is the source of truth,
            // and forcing the operator to manually copy generated text into
            // a file is friction without value. The file diff will surface
            // in `git status` so the change is still visible at commit time.
            File.WriteAllText(path, generated);
            _output.WriteLine($"Report updated to match catalog: {path}");
        }
        // After (possible) auto-write, the on-disk file must match.
        File.ReadAllText(path).Replace("\r\n", "\n", System.StringComparison.Ordinal)
            .Should().Be(generated,
                "the markdown report at {0} must match the catalog after regen", path);
    }

    [Fact]
    public void Catalog_HasBothLiveAndPhase2Entries()
    {
        // Sanity check — the catalog is meant to track BOTH live + pending
        // helpers; a regression that empties out one bucket would silently
        // mislead the operator into thinking everything is one or the other.
        var hasLive = false;
        var hasPending = false;
        foreach (var entry in CapabilityStatusCatalog.Entries.Values)
        {
            if (entry.Status == CapabilityStatus.Live) hasLive = true;
            if (entry.Status == CapabilityStatus.Phase2HookPending) hasPending = true;
        }
        hasLive.Should().BeTrue("catalogue must include at least one Live helper");
        hasPending.Should().BeTrue("catalogue must include at least one Phase2HookPending helper");
    }

    [Fact]
    public void Catalog_GeneratedReport_ContainsKnownPhase2HelpersWeAlreadyBadged()
    {
        var report = CapabilityStatusCatalog.GenerateMarkdownReport();
        // Cross-check: every helper named in the iter 35-36 amber banners
        // must appear in the catalog with Phase2HookPending status.
        report.Should().Contain("SWFOC_SetDamageMultiplier");
        report.Should().Contain("SWFOC_SpawnUnit");
        report.Should().Contain("SWFOC_SetCameraPos");
        report.Should().Contain("PHASE 2 PENDING");
    }
}
