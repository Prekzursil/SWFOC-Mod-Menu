using System.IO;
using FluentAssertions;
using Xunit;

namespace SwfocTrainer.Tests.App.V2;

/// <summary>
/// 2026-04-27 (iter 52) — regression guard for the activity-log DataGrid
/// XAML attributes that affect operator-facing usability:
/// <list type="bullet">
///   <item><c>CanUserSortColumns="True"</c> — click-to-sort headers.</item>
///   <item><c>ClipboardCopyMode="IncludeHeader"</c> — Ctrl+C exports TSV
///     including column names.</item>
///   <item><c>AlternatingRowBackground="{DynamicResource AltRowBackground}"</c>
///     — improves readability of dense grids.</item>
/// </list>
/// Same pattern as <c>PhaseTwoPendingBadgeAuditTests</c> — parses the
/// source XAML directly so the test runs without launching WPF.
/// </summary>
public sealed class ActivityLogXamlAuditTests
{
    private static string ReadMainWindowXaml()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir,
                "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("MainWindowV2.xaml not found in any parent directory.");
    }

    [Fact]
    public void ActivityDataGrid_HasSortableColumns()
    {
        var xaml = ReadMainWindowXaml();
        // Sortable headers — operator can click "ms" to find slowest calls.
        xaml.Should().Contain("CanUserSortColumns=\"True\"",
            "the activity DataGrid must allow column-header sort for operator-driven analysis");
    }

    [Fact]
    public void ActivityDataGrid_HasClipboardCopyMode()
    {
        var xaml = ReadMainWindowXaml();
        // Ctrl+C copies selection including column headers — TSV format.
        xaml.Should().Contain("ClipboardCopyMode=\"IncludeHeader\"",
            "DataGrid Ctrl+C must capture column headers so pasted output is self-describing");
    }

    [Fact]
    public void ActivityDataGrid_HasAlternatingRowBackground()
    {
        var xaml = ReadMainWindowXaml();
        // Visual readability for dense grids.
        xaml.Should().Contain("AlternatingRowBackground=\"{DynamicResource AltRowBackground}\"",
            "alternating row background must reference the theme dictionary for live theme swap");
    }
}
