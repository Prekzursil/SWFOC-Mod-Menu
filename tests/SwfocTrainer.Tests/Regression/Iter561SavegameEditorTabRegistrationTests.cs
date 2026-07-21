using System;
using System.IO;
using System.Windows.Controls;
using FluentAssertions;
using SwfocTrainer.App.Tabs;
using SwfocTrainer.App.V2.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-22 (iter-561, savegame-editor spec follow-up): regression guard for
/// the App-shell registration of the Savegame Editor tab.
///
/// <para>
/// The savegame hat built and tested <see cref="SavegameEditorTabViewModel"/>
/// (iter-289b) and the <see cref="SavegameEditorTab"/> WPF UserControl
/// (iter-289c) inside its owned <c>SwfocTrainer.Savegame</c> / <c>Tabs/</c>
/// scope, but deferred the host wiring — <c>MainViewModelV2</c> tab property +
/// <c>MainWindowV2.xaml</c> TabItem — to editor-polish, since those App-shell
/// files are outside the savegame hat's scope. Without that wiring the tab and
/// its view-model are unreachable dead code: an operator cannot open the
/// Savegame Editor. iter-561 closes that deferral.
/// </para>
///
/// <para>
/// Red-green discipline (guardrail 1005): every assertion in this file FAILS
/// on the pre-iter-561 tree (no <c>SavegameEditor</c> property, no
/// <c>xmlns:tabs</c>, no <c>tabs:SavegameEditorTab</c> TabItem) and PASSES once
/// the registration lands. If a future "simplification" drops the wiring, the
/// red-green pair fires here.
/// </para>
/// </summary>
public sealed class Iter561SavegameEditorTabRegistrationTests
{
    [Fact]
    public void SavegameEditorTabViewModel_ParameterlessCtor_Constructs()
    {
        // Sanity: the view-model the host registers takes no constructor args
        // (pure local-file work, no bridge) — so registration adds no
        // composition-root signature change.
        var vm = new SavegameEditorTabViewModel();
        vm.Chunks.Should().NotBeNull();
        vm.MicroChunks.Should().NotBeNull();
        vm.LoadCommand.Should().NotBeNull();
        vm.SaveCommand.Should().NotBeNull();
    }

    [Fact]
    public void MainViewModelV2_ExposesSavegameEditorProperty()
    {
        // Reflection — no need to stand up the full 17-service MainViewModelV2.
        var prop = typeof(MainViewModelV2).GetProperty("SavegameEditor");
        prop.Should().NotBeNull(
            because: "iter-561 registers the Savegame Editor tab as a MainViewModelV2 property");
        prop!.PropertyType.Should().Be<SavegameEditorTabViewModel>();
        prop.GetGetMethod().Should().NotBeNull(
            because: "the host XAML binds DataContext to this property — it needs a public getter");
    }

    [Fact]
    public void MainViewModelV2Source_ConstructsSavegameEditorInCtor()
    {
        var src = File.ReadAllText(MainViewModelV2SourcePath());
        src.Should().Contain("SavegameEditor = new SavegameEditorTabViewModel();",
            because: "iter-561 constructs the Savegame Editor tab view-model in the MainViewModelV2 ctor body");
    }

    [Fact]
    public void SavegameEditorTab_IsWpfUserControl()
    {
        // Pure reflection — no instantiation, so no STA-thread requirement.
        typeof(SavegameEditorTab).IsSubclassOf(typeof(UserControl)).Should().BeTrue(
            because: "the host TabItem drops SavegameEditorTab in as a UserControl content element");
    }

    [Fact]
    public void MainWindowV2Xaml_DeclaresTabsClrNamespace()
    {
        var xaml = File.ReadAllText(MainWindowV2XamlPath());
        xaml.Should().Contain("xmlns:tabs=\"clr-namespace:SwfocTrainer.App.Tabs\"",
            because: "iter-561 needs the tabs: namespace to reference the SavegameEditorTab UserControl");
    }

    [Fact]
    public void MainWindowV2Xaml_RegistersSavegameEditorTabItem()
    {
        var xaml = File.ReadAllText(MainWindowV2XamlPath());
        xaml.Should().Contain("Header=\"Savegame Editor\"",
            because: "iter-561 adds a TabItem with Header='Savegame Editor'");
        xaml.Should().Contain("<tabs:SavegameEditorTab",
            because: "the TabItem hosts the SavegameEditorTab UserControl");
        xaml.Should().Contain("DataContext=\"{Binding SavegameEditor}\"",
            because: "the UserControl binds against the MainViewModelV2.SavegameEditor property");
    }

    [Fact]
    public void MainWindowV2Xaml_SavegameEditorTabIsSavegameModeScoped()
    {
        // The Savegame Editor belongs to the savegame-mode tab strip — it must
        // carry Visibility="{Binding SavegameTabsVisibility}" exactly like the
        // sibling Savegame Rescue / Save Monitor / Galaxy Visualizer tabs, so
        // it is hidden while the trainer is in LIVE mode.
        var xaml = File.ReadAllText(MainWindowV2XamlPath());
        var headerIdx = xaml.IndexOf("Header=\"Savegame Editor\"", StringComparison.Ordinal);
        headerIdx.Should().BeGreaterThan(-1);
        var closeIdx = xaml.IndexOf("</TabItem>", headerIdx, StringComparison.Ordinal);
        closeIdx.Should().BeGreaterThan(headerIdx,
            because: "the Savegame Editor TabItem must be well-formed");
        var tabBlock = xaml.Substring(headerIdx, closeIdx - headerIdx);
        tabBlock.Should().Contain("Visibility=\"{Binding SavegameTabsVisibility}\"",
            because: "the Savegame Editor tab is savegame-mode-scoped, hidden in LIVE mode");
        tabBlock.Should().Contain("tabs:SavegameEditorTab",
            because: "the SavegameEditorTab UserControl is the content of this exact TabItem");
    }

    private static string MainViewModelV2SourcePath() =>
        Path.Combine(EditorRoot(), "src", "SwfocTrainer.App", "V2",
            "ViewModels", "MainViewModelV2.cs");

    private static string MainWindowV2XamlPath() =>
        Path.Combine(EditorRoot(), "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml");

    private static string EditorRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "SwfocTrainer.App")))
            {
                return dir;
            }
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        throw new DirectoryNotFoundException(
            "Could not locate editor root with src/SwfocTrainer.App/ from " + AppContext.BaseDirectory);
    }
}
