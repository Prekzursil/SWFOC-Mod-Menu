using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Savegame;

/// <summary>
/// Source-level binding-contract pin for the iter-289c <c>SavegameEditorTab</c>
/// WPF view (spec iter-289). WPF data-binding failures are runtime-only — a
/// renamed view-model member or a mistyped <c>{Binding}</c> silently produces
/// an empty control rather than a build error. These tests parse
/// <c>SavegameEditorTab.xaml</c> from disk and assert every <c>{Binding}</c>
/// path resolves to a real public member on the view-model (or one of its row
/// types), so view-model ↔ XAML drift fails the suite.
/// </summary>
[Trait("Category", "Savegame")]
public sealed class SavegameEditorTabViewBindingTests
{
    /// <summary>The types a savegame-editor-view binding can legitimately target.</summary>
    private static readonly Type[] BindingTargets =
    {
        typeof(SavegameEditorTabViewModel),
        typeof(SavegameChunkNode),
        typeof(SavegameMicroChunkRow),
    };

    private static string XamlPath() => Path.Combine(
        TestPaths.FindRepoRoot(), "src", "SwfocTrainer.App", "Tabs",
        "SavegameEditorTab.xaml");

    private static string ReadXaml()
    {
        var path = XamlPath();
        File.Exists(path).Should().BeTrue(
            because: $"the iter-289c savegame editor view must exist at {path}");
        return File.ReadAllText(path);
    }

    /// <summary>Distinct leaf names of every <c>{Binding ...}</c> path in the XAML.</summary>
    private static IReadOnlyCollection<string> ExtractBindingLeaves(string xaml)
    {
        var leaves = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(xaml, @"\{Binding\s+([^},]+)"))
        {
            var token = match.Groups[1].Value.Trim();
            if (token.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
            {
                token = token[5..].Trim();
            }

            // Skip keyword-first bindings (RelativeSource=, ElementName=, Source=).
            if (token.Length == 0 || token.Contains('='))
            {
                continue;
            }

            var leaf = token.Split('.')[0].Trim();
            if (leaf.Length != 0)
            {
                leaves.Add(leaf);
            }
        }

        return leaves;
    }

    private static bool ResolvesOnAnyTarget(string member) =>
        BindingTargets.Any(t => t.GetProperty(
            member, BindingFlags.Public | BindingFlags.Instance) is not null);

    [Fact]
    public void SavegameEditorTabXaml_IsAUserControlWithTheExpectedClass()
    {
        var xaml = ReadXaml();
        xaml.Should().Contain("<UserControl",
            because: "the savegame editor view is a self-contained UserControl");
        xaml.Should().Contain("x:Class=\"SwfocTrainer.App.Tabs.SavegameEditorTab\"",
            because: "the x:Class must match the Tabs/SavegameEditorTab.xaml.cs code-behind");
    }

    [Fact]
    public void EveryBindingInTheView_ResolvesToARealViewModelMember()
    {
        var leaves = ExtractBindingLeaves(ReadXaml());

        leaves.Should().NotBeEmpty(
            because: "the savegame editor view binds to its view-model");

        var unresolved = leaves.Where(leaf => !ResolvesOnAnyTarget(leaf)).ToArray();
        unresolved.Should().BeEmpty(
            because: "every {Binding X} in SavegameEditorTab.xaml must resolve to a public " +
                     "member on SavegameEditorTabViewModel / SavegameChunkNode / " +
                     "SavegameMicroChunkRow — unresolved: " + string.Join(", ", unresolved));
    }

    [Theory]
    [InlineData("LoadCommand")]
    [InlineData("DiagnoseCommand")]
    [InlineData("FixCommand")]
    [InlineData("SaveCommand")]
    [InlineData("ValidateModHashCommand")]
    [InlineData("ReAnchorModHashCommand")]
    [InlineData("ApplyEditCommand")]
    [InlineData("DeleteMicroChunkCommand")]
    public void EveryViewModelCommand_IsBoundByAButtonInTheView(string command)
    {
        // The eight commands are the operator's whole action surface — pin
        // that the view wires a Button.Command to each one. Catches a future
        // XAML edit that drops an action without dropping the command.
        ReadXaml().Should().Contain($"Command=\"{{Binding {command}}}\"",
            because: $"the savegame editor view must expose {command} as a button");
    }

    [Fact]
    public void View_BindsTheChunkTreeAndMicroChunkCollectionsAndSelections()
    {
        var xaml = ReadXaml();
        xaml.Should().Contain("ItemsSource=\"{Binding Chunks}\"",
            because: "the chunk-tree list is bound to the VM's Chunks collection");
        xaml.Should().Contain("ItemsSource=\"{Binding MicroChunks}\"",
            because: "the micro-chunk grid is bound to the VM's MicroChunks collection");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedChunk, Mode=TwoWay}\"",
            because: "selecting a chunk must drive the VM's SelectedChunk");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedMicroChunk, Mode=TwoWay}\"",
            because: "selecting a micro-chunk must drive the VM's SelectedMicroChunk");
    }
}
