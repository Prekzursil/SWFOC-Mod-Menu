using System.IO;
using FluentAssertions;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-07 (iter 339): pin tests for Combat tab Hardpoint Inspector XAML
/// wire-up. Validates that iter-338 VM surface (Hardpoints + RefreshHardpointsCommand
/// + RefreshHardpoints + HardpointInspectAddrText) is bound by MainWindowV2.xaml.
/// Closes the iter-338 deferred XAML layer; mirrors iter-149 Camera tab XAML
/// pin tests (which followed iter-148 Camera VM layer).
/// </summary>
public sealed class Iter339HardpointInspectorXamlTests
{
    private static string LoadXamlSource()
    {
        var asmDir = Path.GetDirectoryName(typeof(Iter339HardpointInspectorXamlTests).Assembly.Location)!;
        var dir = new DirectoryInfo(asmDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("test must locate MainWindowV2.xaml");
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml"));
    }

    [Fact]
    public void XamlPin_HardpointInspectorGroupBoxPresent()
    {
        var xaml = LoadXamlSource();
        xaml.Should().Contain("Hardpoint Inspector",
            "iter-339 Combat tab GroupBox header must contain 'Hardpoint Inspector'");
        xaml.Should().Contain("SWFOC_GetHardpoints",
            "iter-339 GroupBox header must reference the iter-281 LIVE bridge wire");
        xaml.Should().Contain("RequiresLiveSwfoc",
            "iter-339 GroupBox header must surface the catalog status badge for operator-trust");
    }

    [Fact]
    public void XamlPin_HardpointInspectAddrTextBoxBound()
    {
        var xaml = LoadXamlSource();
        xaml.Should().Contain("HardpointInspectAddrText",
            "iter-339 TextBox must bind to iter-338 VM property HardpointInspectAddrText");
    }

    [Fact]
    public void XamlPin_RefreshHardpointsCommandBound()
    {
        var xaml = LoadXamlSource();
        xaml.Should().Contain("{Binding RefreshHardpointsCommand}",
            "iter-339 Refresh button must bind to iter-338 VM RefreshHardpointsCommand");
    }

    [Fact]
    public void XamlPin_RefreshHardpointsBadgeAndTooltipBound()
    {
        var xaml = LoadXamlSource();
        xaml.Should().Contain("RefreshHardpoints.Badge",
            "iter-339 must bind capability badge per iter-308/iter-311 codified pattern");
        xaml.Should().Contain("RefreshHardpoints.Tooltip",
            "iter-339 must bind capability tooltip for operator-trust");
    }

    [Fact]
    public void XamlPin_HardpointsListBoxItemsSourceBound()
    {
        var xaml = LoadXamlSource();
        xaml.Should().Contain("ItemsSource=\"{Binding Hardpoints}\"",
            "iter-339 ListBox must bind to iter-338 VM ObservableCollection<HardpointEntry> Hardpoints");
    }

    [Fact]
    public void XamlPin_HardpointEntryDataTemplateShowsIndexAddrHp()
    {
        var xaml = LoadXamlSource();
        // ItemTemplate must surface Index + ChildAddr + Hp fields per iter-338 record contract.
        xaml.Should().Contain("Binding Index",
            "iter-339 ItemTemplate must show HardpointEntry.Index");
        xaml.Should().Contain("Binding ChildAddr",
            "iter-339 ItemTemplate must show HardpointEntry.ChildAddr");
        xaml.Should().Contain("Binding Hp",
            "iter-339 ItemTemplate must show HardpointEntry.Hp");
    }
}
