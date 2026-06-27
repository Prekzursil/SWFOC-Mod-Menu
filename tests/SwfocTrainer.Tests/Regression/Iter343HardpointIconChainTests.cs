using System.IO;
using FluentAssertions;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-07 (iter 343): pin tests for Hardpoint icon-resolution chain
/// Phase 1 (Approach A optimistic chain). Validates HardpointEntry record's
/// new IconPath field + XAML wires Image to it. Empirical verification of
/// `tostring(GameObjectType_handle)` semantics deferred to operator session
/// (live SWFOC required to know if Approach A's optimistic assumption holds).
/// </summary>
public sealed class Iter343HardpointIconChainTests
{
    private static string LoadXamlSource()
    {
        var asmDir = Path.GetDirectoryName(typeof(Iter343HardpointIconChainTests).Assembly.Location)!;
        var dir = new DirectoryInfo(asmDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("test must locate MainWindowV2.xaml");
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml"));
    }

    [Fact]
    public void HardpointEntry_IconPath_DefaultsToNull()
    {
        var entry = new HardpointEntry(Index: 0, ChildAddr: 0x140012340, Hp: 750.0f);
        entry.IconPath.Should().BeNull(
            "iter-343 IconPath defaults to null per optional record param");
    }

    [Fact]
    public void HardpointEntry_IconPath_PreservedThroughWithMutation()
    {
        var entry = new HardpointEntry(Index: 0, ChildAddr: 0x140012340, Hp: 750.0f);
        var withIcon = entry with { IconPath = "/cache/atat_laser_32.png" };

        withIcon.Index.Should().Be(0);
        withIcon.ChildAddr.Should().Be(0x140012340);
        withIcon.Hp.Should().Be(750.0f);
        withIcon.IconPath.Should().Be("/cache/atat_laser_32.png",
            "iter-343 record `with` mutation must preserve IconPath updates");
    }

    [Fact]
    public void HardpointEntry_IconPath_CanBeExplicitlyNull()
    {
        var entry = new HardpointEntry(Index: 1, ChildAddr: 0x140012358, Hp: 420.5f, IconPath: null);
        entry.IconPath.Should().BeNull();
    }

    [Fact]
    public void Parser_ParseListFromBridgeReply_LeavesIconPathNull_ByDefault()
    {
        var raw = "count=2 child0=0x0000000140012340 hp0=750.000 child1=0x0000000140012358 hp1=420.500";
        var entries = HardpointEntry.ParseListFromBridgeReply(raw);

        entries.Should().HaveCount(2);
        entries.Should().AllSatisfy(e => e.IconPath.Should().BeNull(
            "parser doesn't resolve icons; VM enriches in a second pass after parsing"));
    }

    [Fact]
    public void XamlPin_HardpointInspectorImageElementPresent()
    {
        var xaml = LoadXamlSource();
        xaml.Should().Contain("Source=\"{Binding IconPath}\"",
            "iter-343 ItemTemplate must bind Image.Source to HardpointEntry.IconPath");
    }

    [Fact]
    public void XamlPin_HardpointImageWidth32MatchesWeaponIconDefault()
    {
        var xaml = LoadXamlSource();
        xaml.Should().Contain("Width=\"32\" Height=\"32\"",
            "iter-343 Image dimensions must match iter-331 ResolveWeaponIcon default size 32");
    }

    [Fact]
    public void XamlPin_HardpointImageStackedHorizontalWithText()
    {
        var xaml = LoadXamlSource();
        xaml.Should().Contain("StackPanel Orientation=\"Horizontal\"",
            "iter-343 ItemTemplate must use horizontal StackPanel for icon-text layout");
    }

    [Fact]
    public void XamlPin_HardpointInspectorTextBlockPreserved()
    {
        var xaml = LoadXamlSource();
        xaml.Should().Contain("[Index ");
        xaml.Should().Contain(" hp=");
        xaml.Should().Contain("Binding Index");
        xaml.Should().Contain("Binding ChildAddr");
        xaml.Should().Contain("Binding Hp");
    }
}
