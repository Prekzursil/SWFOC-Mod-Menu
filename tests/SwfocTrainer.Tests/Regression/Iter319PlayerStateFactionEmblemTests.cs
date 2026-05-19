using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Assets;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-07 (iter 319, third UI consumer of iter-313/314/315 resolver
/// extensions; closes the 4-asset-class-per-tab milestone).
///
/// PlayerState tab uses ComboBox+ItemTemplate not DataGrid+row-record, so
/// the iter-317/iter-318 parallel-collection pattern shifts to in-place
/// INPC update via PlayerSlotEntry.IconPath property + SetIconResolver
/// hot-swap that walks Slots and updates each entry's IconPath.
///
/// Tests focus on the resolver-shape invariants directly (PlayerSlotEntry
/// construction, IconPath INPC, SetIconResolver method existence) plus
/// source-level wire guards on MainViewModelV2 + XAML. The full PlayerState
/// VM dep graph (7+ services) is not exercised here — that's covered by
/// existing PlayerStateTabViewModel tests.
/// </summary>
public sealed class Iter319PlayerStateFactionEmblemTests
{
    [Fact]
    public void PlayerSlotEntry_IconPath_DefaultIsNull()
    {
        // Pin: new PlayerSlotEntry without explicit IconPath = null IconPath.
        // Operator default (no resolver wired, no DDS extracted) renders no
        // emblem in the ComboBox dropdown.
        var entry = new PlayerSlotEntry(slot: 1, factionName: "EMPIRE");
        entry.IconPath.Should().BeNull(
            because: "freshly-constructed PlayerSlotEntry has no resolver wired; IconPath defaults to null");
    }

    [Fact]
    public void PlayerSlotEntry_IconPath_AcceptsValueAndFiresPropertyChanged()
    {
        // Pin: IconPath setter wires through SetField (INPC). WPF binding to
        // the ComboBox.ItemTemplate Image relies on this firing
        // PropertyChanged so the icon updates when SetIconResolver hot-swaps.
        var entry = new PlayerSlotEntry(slot: 0, factionName: "REBEL");
        var raised = new List<string?>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        entry.IconPath = "/icons/rebel.png";

        entry.IconPath.Should().Be("/icons/rebel.png");
        raised.Should().Contain(nameof(PlayerSlotEntry.IconPath),
            because: "INPC must fire so the WPF ComboBox.ItemTemplate Image rebinds");
    }

    [Fact]
    public void PlayerSlotEntry_IconPath_AcceptsNullClear()
    {
        // Pin: setting IconPath back to null (e.g. when SetIconResolver(null)
        // walks Slots) is a clean clear. WPF Image control hides on null
        // Source binding.
        var entry = new PlayerSlotEntry(slot: 2, factionName: "UNDERWORLD");
        entry.IconPath = "/icons/under.png";
        entry.IconPath = null;
        entry.IconPath.Should().BeNull();
    }

    [Fact]
    public void PlayerStateTabViewModel_SetIconResolver_PublicMethodExists()
    {
        // Source-level pin: composition root (MainViewModelV2) calls this on
        // Settings.IconsRoot change. Method must be public + accept nullable
        // UnitIconResolver.
        var t = typeof(PlayerStateTabViewModel);
        var method = t.GetMethod("SetIconResolver", new[] { typeof(UnitIconResolver) });
        method.Should().NotBeNull(
            because: "MainViewModelV2.OnSettingsPropertyChanged needs PlayerState.SetIconResolver");
        method!.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public void PlayerStateTabViewModel_OptionalIconResolverCtor_AcceptsExtraParam()
    {
        // Source-level pin: ctor accepts an 8th optional parameter
        // `UnitIconResolver? iconResolver = null`. Validates back-compat:
        // existing 7-dep callers continue to compile.
        var t = typeof(PlayerStateTabViewModel);
        var ctor = t.GetConstructors().FirstOrDefault();
        ctor.Should().NotBeNull("PlayerStateTabViewModel must have a public ctor");
        var pars = ctor!.GetParameters();
        pars.Should().HaveCount(8,
            because: "iter-319 extends 7-dep ctor with iconResolver = null (optional default-null pattern)");
        var iconParam = pars[7];
        iconParam.ParameterType.Should().Be(typeof(UnitIconResolver),
            because: "8th param is the iter-313/314/315 UnitIconResolver");
        iconParam.HasDefaultValue.Should().BeTrue(
            because: "optional-default-null pattern keeps iter-301/308/311/318 callers working unchanged");
        iconParam.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void MainViewModelV2_WiresResolverThroughToPlayerState()
    {
        var src = File.ReadAllText(MainViewModelV2SourcePath());
        src.Should().Contain("new PlayerStateTabViewModel(bridge, settings, economy, heroRespawn, factionSwitch, unitMutator, factions, iconResolver)",
            because: "iter-319 wires the same iconResolver instance to PlayerState (4th tab in the chain)");
    }

    [Fact]
    public void MainViewModelV2_SettingsHotSwap_AlsoUpdatesPlayerState()
    {
        var src = File.ReadAllText(MainViewModelV2SourcePath());
        src.Should().Contain("PlayerState.SetIconResolver(",
            because: "iter-319 extends iter-312/iter-317/iter-318 hot-swap to cover PlayerState — all 4 tabs flip together");
    }

    [Fact]
    public void PlayerStateXaml_ComboBoxItemTemplate_HasFactionEmblemImage()
    {
        var xamlPath = Path.Combine(EditorRoot(), "src", "SwfocTrainer.App",
            "V2", "MainWindowV2.xaml");
        var xaml = File.ReadAllText(xamlPath);
        // iter-319 added a 3rd `Source="{Binding IconPath}"` Image to MainWindowV2.xaml
        // (iter-317 Galactic + iter-318 HeroLab + iter-319 PlayerState).
        var occurrences = System.Text.RegularExpressions.Regex.Matches(xaml,
            "Source=\"\\{Binding IconPath\\}\"").Count;
        occurrences.Should().BeGreaterThanOrEqualTo(3,
            because: "iter-317/318/319 each added one Image bound to IconPath; all must exist");
    }

    [Fact]
    public void PlayerStateXaml_ComboBoxWidthBumpedForEmblem()
    {
        // Pin: iter-319 widened the Slot ComboBox from 240 to 280 to fit
        // the 24px emblem column. If a future XAML refactor narrows it back,
        // emblems would clip. Catch via a width-pin source check.
        var xamlPath = Path.Combine(EditorRoot(), "src", "SwfocTrainer.App",
            "V2", "MainWindowV2.xaml");
        var xaml = File.ReadAllText(xamlPath);
        xaml.Should().Contain("Width=\"280\"",
            because: "iter-319 widened the Slot ComboBox from 240 to 280 for the emblem column");
    }

    private static string MainViewModelV2SourcePath() =>
        Path.Combine(EditorRoot(), "src", "SwfocTrainer.App", "V2",
            "ViewModels", "MainViewModelV2.cs");

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
