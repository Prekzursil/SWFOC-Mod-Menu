using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// Guards the 2026-04-27 fixes to the Player State tab faction list and
/// auto-refresh of slot↔faction labels.
/// </summary>
/// <remarks>
/// <para>
/// Background: the operator reported the Player slot dropdown showed bare
/// "Slot 0 / 1 / 2" labels until they manually clicked "Refresh slot map".
/// The Faction dropdown was hardcoded to a fixed list which couldn't keep up
/// with mods (AOTR, ROE, ROTR, Thrawn's Revenge etc.) that introduce their
/// own faction strings.
/// </para>
/// <para>
/// Fixes:
/// (1) <c>PlayerStateTabViewModel.RefreshSlotMapAsync</c> now MERGES every
///     live faction name from <c>SWFOC_GetAllPlayers</c> into the
///     <c>Factions</c> ObservableCollection. The dropdown grows organically
///     to match whatever the running game reports, vanilla or modded.
/// (2) <c>RefreshSlotMapAsync</c> promoted from private to public so
///     <c>MainViewModelV2</c> can call it automatically after auto-connect
///     succeeds.
/// (3) <c>MainViewModelV2.OnWindowLoadedAsync</c> awaits
///     <c>PlayerState.RefreshSlotMapAsync()</c> after
///     <c>Diagnostics.InitializeAsync()</c> when auto-connect is enabled.
/// </para>
/// <para>
/// We KEEP a small vanilla seed list (EMPIRE / REBEL / UNDERWORLD) so the
/// dropdown isn't empty when the operator hasn't connected yet. That's a
/// fallback, not the source of truth — the live merge is.
/// </para>
/// </remarks>
public sealed class PlayerStateFactionListRegressionTests
{
    private static string LoadSource(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            $"{relativePath} not found by walking up from {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Factions_HasVanilla_SeedList_ButIsNot_TheSourceOfTruth()
    {
        // 2026-04-27: the seed list moved from PlayerStateTabViewModel into
        // V2FactionRegistry so multiple tabs (UnitControl, WorldState,
        // Galactic, Economy) all share one ObservableCollection. The seed
        // contract still applies — it just lives one file over.
        var src = LoadSource(Path.Combine(
            "src", "SwfocTrainer.App", "V2", "Infrastructure", "V2FactionRegistry.cs"));

        src.Should().Contain("\"EMPIRE\"",
            "EMPIRE must be in the seed Factions list as a fallback for not-yet-connected operators.");
        src.Should().Contain("\"REBEL\"",
            "REBEL must be in the seed Factions list.");
        src.Should().Contain("\"UNDERWORLD\"",
            "UNDERWORLD must be in the seed Factions list.");
    }

    [Fact]
    public void RefreshSlotMap_Merges_LiveFactionNames_Into_FactionsCollection()
    {
        // 2026-04-27: PlayerState delegates the merge to V2FactionRegistry
        // so all tabs see the same updated dropdown. Both files cooperate:
        // PlayerState reads the live response, registry does the actual
        // Add() with case-insensitive deduplication.
        var vmSrc = LoadSource(Path.Combine(
            "src", "SwfocTrainer.App", "V2", "ViewModels", "PlayerStateTabViewModel.cs"));
        var registrySrc = LoadSource(Path.Combine(
            "src", "SwfocTrainer.App", "V2", "Infrastructure", "V2FactionRegistry.cs"));

        vmSrc.Should().Contain("liveFactions",
            "RefreshSlotMapAsync must collect live faction names into a HashSet.");
        vmSrc.Should().Contain("_factionRegistry.MergeFactions",
            "RefreshSlotMapAsync must hand the live names to the shared V2FactionRegistry " +
            "so all tabs see the merged dropdown.");
        registrySrc.Should().Contain("Factions.Add(",
            "V2FactionRegistry.MergeFactions must Add() new live factions into its " +
            "ObservableCollection — that's how the WPF dropdowns refresh live.");
        registrySrc.Should().Contain("StringComparison.OrdinalIgnoreCase",
            "Faction-name comparison must be case-insensitive — engine sometimes emits 'Empire' " +
            "vs 'EMPIRE' inconsistently across snapshots.");
    }

    [Fact]
    public void RefreshSlotMapAsync_IsPublic_SoMainViewModelCanCallIt()
    {
        var src = LoadSource(Path.Combine(
            "src", "SwfocTrainer.App", "V2", "ViewModels", "PlayerStateTabViewModel.cs"));

        // Asserts the visibility modifier of RefreshSlotMapAsync is public —
        // a future "tighten visibility" pass that flips it back to private
        // will break the auto-refresh hook in MainViewModelV2 silently.
        src.Should().Contain("public async Task RefreshSlotMapAsync(",
            "RefreshSlotMapAsync must remain public so MainViewModelV2.OnWindowLoadedAsync " +
            "can call it after auto-connect — that's what makes the slot dropdown show " +
            "'Slot 6 — UNDERWORLD' without a manual click.");
        src.Should().NotContain("private async Task RefreshSlotMapAsync(",
            "Reverting RefreshSlotMapAsync to private would break auto-refresh.");
    }

    [Fact]
    public void MainViewModel_OnWindowLoaded_AutoFires_RefreshSlotMap()
    {
        var src = LoadSource(Path.Combine(
            "src", "SwfocTrainer.App", "V2", "ViewModels", "MainViewModelV2.cs"));

        // Auto-connect path must call RefreshSlotMapAsync after Diagnostics.
        // InitializeAsync. If a future refactor splits or renames this hook,
        // this test fires.
        src.Should().Contain("PlayerState.RefreshSlotMapAsync()",
            "MainViewModelV2.OnWindowLoadedAsync must call PlayerState.RefreshSlotMapAsync " +
            "so the slot dropdown gets labelled automatically when the bridge is reachable.");
        src.Should().Contain("Diagnostics.InitializeAsync()",
            "Diagnostics initialization must happen first so the bridge probe path is warm " +
            "before we issue SWFOC_GetAllPlayers.");
    }

    [Fact]
    public void Redundant_SwitchFaction_ButtonAndComboBox_RemovedFromMainWindow()
    {
        // 2026-04-27: removed the standalone Faction ComboBox + "Switch
        // faction" button from the Player State tab. Both that button and
        // the slot-based "Switch to selected slot (v3 + AI swap)" emit the
        // identical Lua command (SWFOC_SetHumanPlayer_v3). The slot path
        // is authoritative because it also swaps PlayerObject+0x360 to
        // prevent the dual-control bug. The faction-name path was a
        // wrapper that added no engine behaviour, just a name->slot
        // mapping layer. Reverting this would re-introduce the redundancy
        // that confused operators ("which button do I click?").
        var src = LoadSource(Path.Combine(
            "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml"));

        src.Should().NotContain("Content=\"Switch faction\"",
            "The redundant 'Switch faction' button must stay removed — both that and " +
            "'Switch to selected slot (v3 + AI swap)' emit identical Lua, and the slot " +
            "path is authoritative.");
        src.Should().NotContain("Command=\"{Binding SwitchFactionCommand}\"",
            "Reverting the SwitchFactionCommand binding would resurrect the redundant button.");
        // The slot-based authoritative button must remain.
        src.Should().Contain("Switch to selected slot",
            "The slot-based 'Switch to selected slot' button is the authoritative faction " +
            "switch (handles AI brain swap at PlayerObject+0x360). Removing it would leave " +
            "the user with no faction-switching control at all.");
    }
}
