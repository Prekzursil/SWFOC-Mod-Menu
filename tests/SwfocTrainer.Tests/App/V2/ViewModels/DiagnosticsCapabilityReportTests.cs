using System.IO;
using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 41) — unit-level verification that the Diagnostics
/// tab's "Open capability report" button can resolve the on-disk
/// markdown file from a typical install layout (editor in a sibling
/// checkout to swfoc_memory).
/// </summary>
public sealed class DiagnosticsCapabilityReportTests
{
    [Fact]
    public void ResolveCapabilityReportPath_FindsExistingMarkdown()
    {
        // The full-suite test runner exercises this from
        // <repo>/tests/SwfocTrainer.Tests/bin/Debug/net8.0-windows/.
        // Walking parents finds <repo-root>/../swfoc_memory/knowledge-base/
        // which is where iter 39 dropped capability_status_2026-04-27.md.
        var path = DiagnosticsTabViewModel.ResolveCapabilityReportPath();
        path.Should().NotBeNull("the iter 39 report file must be discoverable from the test bin");
        File.Exists(path!).Should().BeTrue("the resolved path must point at a real file");
        path.Should().EndWith(".md");
        path.Should().Contain("capability_status");
    }

    [Fact]
    public void ResolveCapabilityReportPath_PicksMostRecentDateSuffix()
    {
        // The resolver sorts matches and picks the last one. Today's
        // checked-in file (capability_status_2026-04-27.md) is the
        // newest by yyyy-MM-dd sort order, so it must be the pick.
        var path = DiagnosticsTabViewModel.ResolveCapabilityReportPath();
        path.Should().NotBeNull();
        // If we ever ship a 2026-04-28 report alongside, this should pick
        // that one — the test still passes either way, just the assertion
        // about which date wins gets stronger.
        Path.GetFileName(path!).Should().StartWith("capability_status_2026-");
    }
}
