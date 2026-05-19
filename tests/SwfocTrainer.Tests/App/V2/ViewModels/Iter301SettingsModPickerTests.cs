using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-05-07 (iter 301): pin tests for SettingsTabViewModel mod-picker UI
/// consumer. Validates RefreshModsAsync correctly populates the bound
/// ObservableCollection, cross-references iter-299 GetCurrentMod for the
/// IsCurrentlyLoaded flag, and surfaces sentinel responses gracefully.
/// </summary>
public sealed class Iter301SettingsModPickerTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession(FakeGameState state)
    {
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        return (sim, new V2BridgeAdapter(pipe));
    }

    [Fact]
    public void Constructor_WithoutBridge_DoesNotThrow_AndModsIsEmpty()
    {
        var settings = V2Settings.Load();
        var vm = new SettingsTabViewModel(settings);
        vm.Mods.Should().BeEmpty(because: "no Refresh has been called and no bridge was provided");
        vm.ActiveMod.Should().Be("(unknown)");
    }

    [Fact]
    public async Task RefreshModsCore_PopulatesMods_AndFlagsCurrentlyLoaded()
    {
        var fakeState = new FakeGameState
        {
            ActiveModName = "AOTR",
            ActiveModVersion = "2.7",
            ActiveModPath = @"C:\Games\SWFOC\Mods\AOTR",
        };
        fakeState.AvailableMods.Add(("AOTR", @"C:\Games\SWFOC\Mods\AOTR"));
        fakeState.AvailableMods.Add(("Vanilla_Plus", @"C:\Games\SWFOC\Mods\Vanilla_Plus"));
        fakeState.AvailableMods.Add(("ROTM", @"C:\Games\SWFOC\Mods\ROTM"));

        var (sim, adapter) = NewSession(fakeState);
        using var _ = sim;

        var settings = V2Settings.Load();
        var vm = new SettingsTabViewModel(settings, adapter);

        await vm.RefreshModsCore();

        vm.Mods.Should().HaveCount(3, because: "3 mods seeded in fake state");
        vm.Mods.Should().Contain(m => m.Name == "AOTR" && m.IsCurrentlyLoaded,
            because: "AOTR is the active mod and must be flagged");
        vm.Mods.Should().Contain(m => m.Name == "Vanilla_Plus" && !m.IsCurrentlyLoaded);
        vm.Mods.Should().Contain(m => m.Name == "ROTM" && !m.IsCurrentlyLoaded);
        vm.ActiveMod.Should().Be("AOTR");
        vm.ModPickerStatus.Should().Contain("3 mod");
        vm.ModPickerStatus.Should().Contain("AOTR");
    }

    [Fact]
    public async Task RefreshModsCore_NoModsSentinel_PopulatesEmptyAndShowsHelpfulMessage()
    {
        var fakeState = new FakeGameState();
        // No AvailableMods seeded → simulator returns "(no_mods)" sentinel.

        var (sim, adapter) = NewSession(fakeState);
        using var _ = sim;

        var settings = V2Settings.Load();
        var vm = new SettingsTabViewModel(settings, adapter);

        await vm.RefreshModsCore();

        vm.Mods.Should().BeEmpty();
        vm.ActiveMod.Should().Be("vanilla", because: "no ActiveModName -> bridge returns 'vanilla'");
        vm.ModPickerStatus.ToLowerInvariant().Should().Contain("no mods");
    }

    [Fact]
    public async Task RefreshModsCore_VanillaActiveMod_NoModRowFlaggedLoaded()
    {
        // Operator may have mods installed but be running vanilla; in that
        // case all rows show IsCurrentlyLoaded=false.
        var fakeState = new FakeGameState();
        fakeState.AvailableMods.Add(("AOTR", @"C:\Games\SWFOC\Mods\AOTR"));

        var (sim, adapter) = NewSession(fakeState);
        using var _ = sim;

        var settings = V2Settings.Load();
        var vm = new SettingsTabViewModel(settings, adapter);

        await vm.RefreshModsCore();

        vm.ActiveMod.Should().Be("vanilla");
        vm.Mods.Should().HaveCount(1);
        vm.Mods.Single().IsCurrentlyLoaded.Should().BeFalse(
            because: "running vanilla means no installed mod is currently loaded");
    }

    [Fact]
    public void OpenModsFolderCommand_WithMissingGamePath_DoesNotThrow()
    {
        // Operator hasn't configured GamePath yet; OpenModsFolder should
        // surface a helpful message instead of crashing.
        var settings = V2Settings.Load();
        settings.GamePath = string.Empty;
        var vm = new SettingsTabViewModel(settings);

        // Execute should not throw.
        vm.OpenModsFolderCommand.Execute(null);

        vm.ModPickerStatus.ToLowerInvariant().Should().Contain("gamepath");
    }
}
