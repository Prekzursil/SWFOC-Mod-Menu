using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.V2Vm;
using SwfocTrainer.Tests.Simulator;
using SwfocTrainer.Tests.Simulator.E2E;
using SwfocTrainer.Tests.V2;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 61) — walks every V2 tab view-model in the editor,
/// gathers <c>AllActions</c>, and feeds the aggregate to
/// <see cref="CapabilitySurfaceReport.GenerateMarkdownReport"/>. Two
/// purposes:
/// <list type="number">
///   <item>Forward-orphan check at the VM level: every VM must expose
///         <c>AllActions</c> with at least one entry.</item>
///   <item>Drift-protection for the on-disk report at
///         <c>knowledge-base/capability_surface_2026-04-27.md</c> —
///         the test asserts the on-disk file matches the generated
///         output, so adding a button without regenerating the report
///         fails CI.</item>
/// </list>
/// </summary>
public sealed class CapabilitySurfaceReportIntegrationTests
{
    [Fact]
    public void GenerateReport_AcrossEvery21Tabs_MatchesOnDiskMarkdown()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var settings = new V2Settings();

        // Construct every bridge-using V2 VM with the minimum services it
        // needs. Order matches MainViewModelV2's tab order so the report
        // walks the editor as the operator sees it. Diagnostics + Settings
        // tabs aren't bridge-action surfaces (Diagnostics has its own
        // probe inventory; Settings has no bridge calls) so they're
        // excluded from the surface count.
        var economy = new EconomyService(adapter, NullLogger<EconomyService>.Instance);
        var heroRespawn = new HeroRespawnService(adapter, NullLogger<HeroRespawnService>.Instance);
        var factionSwitch = new FactionSwitchService(adapter, NullLogger<FactionSwitchService>.Instance);
        var unitMutator = new V2UnitMutationDispatcher(adapter);
        var factionRegistry = new V2FactionRegistry();
        var playerState = new PlayerStateTabViewModel(
            adapter, settings, economy, heroRespawn, factionSwitch, unitMutator, factionRegistry);

        var godMode = new GodModeService(adapter, NullLogger<GodModeService>.Instance);
        var oneHitKill = new OneHitKillService(adapter, NullLogger<OneHitKillService>.Instance);
        var unitInspector = new UnitInspectorService(adapter, NullLogger<UnitInspectorService>.Instance);
        var hardpoints = new HardpointService(adapter, NullLogger<HardpointService>.Instance);
        var enhancedSpawn = new EnhancedSpawnService(adapter, NullLogger<EnhancedSpawnService>.Instance);
        var unitControl = new UnitControlTabViewModel(
            adapter, settings, godMode, oneHitKill, unitInspector, hardpoints,
            enhancedSpawn, unitMutator, factionRegistry);

        var corruption = new CorruptionService(adapter, NullLogger<CorruptionService>.Instance);
        var diplomacy = new DiplomacyService(adapter, NullLogger<DiplomacyService>.Instance);
        var storyEventsSvc = new StoryEventService(
            new NullCatalogService(), adapter, NullLogger<StoryEventService>.Instance);
        var maphack = new MaphackService(adapter, NullLogger<MaphackService>.Instance);
        var crashAnalyzer = new CrashAnalyzerService(adapter, NullLogger<CrashAnalyzerService>.Instance);
        var worldState = new WorldStateTabViewModel(
            settings, corruption, diplomacy, storyEventsSvc, maphack, crashAnalyzer, factionRegistry, unitMutator);

        // Single-dep VMs.
        var tacticalUnits = new TacticalUnitsFilterTabViewModel(adapter);
        var economyTab = new EconomyTabViewModel(adapter);
        var combat = new CombatTabViewModel(adapter, new V2UnitMutationDispatcher(adapter));
        var inspector = new InspectorTabViewModel(adapter, new V2UnitMutationDispatcher(adapter));
        var speed = new SpeedTabViewModel(adapter);
        var spawning = new SpawningTabViewModel(adapter);
        var galactic = new GalacticTabViewModel(adapter, new V2UnitMutationDispatcher(adapter));
        var heroLab = new HeroLabTabViewModel(adapter);
        var battleControl = new BattleControlTabViewModel(adapter);
        var storyEvents = new StoryEventsTabViewModel(adapter);
        var cameraDebug = new CameraDebugTabViewModel(adapter);
        var luaPlayground = new LuaPlaygroundTabViewModel(adapter);
        var eventStream = new EventStreamViewModel(adapter);
        var director = new DirectorModeTabViewModel(adapter);
        var crossFaction = new CrossFactionRecruitmentTabViewModel(adapter);
        var unitStatEditor = new UnitStatEditorTabViewModel(adapter);
        var quickActions = new QuickActionsTabViewModel(adapter);
        var probes = new ProbesTabViewModel(adapter);

        var tabs = new (string, IReadOnlyList<CapabilityAwareAction>)[]
        {
            ("Tactical Units", tacticalUnits.AllActions),
            ("Player State", playerState.AllActions),
            ("Economy", economyTab.AllActions),
            ("Combat", combat.AllActions),
            ("Inspector", inspector.AllActions),
            ("Speed", speed.AllActions),
            ("Spawning", spawning.AllActions),
            ("Galactic", galactic.AllActions),
            ("Hero Lab", heroLab.AllActions),
            ("Battle Control", battleControl.AllActions),
            ("Story Events", storyEvents.AllActions),
            ("Camera & Debug", cameraDebug.AllActions),
            ("Lua Playground", luaPlayground.AllActions),
            ("Event Stream", eventStream.AllActions),
            ("Director Mode", director.AllActions),
            ("Cross-Faction", crossFaction.AllActions),
            ("Unit Stat Editor", unitStatEditor.AllActions),
            ("Quick Actions", quickActions.AllActions),
            ("Unit Control", unitControl.AllActions),
            ("World State", worldState.AllActions),
            ("Probes & Scripts", probes.AllActions),
        };

        // Forward-orphan check: every VM exposes a non-empty action list.
        foreach (var (tabName, actions) in tabs)
        {
            actions.Should().NotBeEmpty($"{tabName} must expose at least one CapabilityAwareAction");
        }
        tabs.Should().HaveCount(21,
            "21 bridge-using V2 tabs as of iter 60");

        // iter 68: pull history (if any) so the regenerated report
        // embeds the trend line under the headline.
        var historyPath = LocateOnDiskHistoryFile();
        var history = historyPath is null
            ? Array.Empty<CapabilitySurfaceHistory.HistoryEntry>()
            : CapabilitySurfaceHistory.LoadAll(historyPath);
        var report = CapabilitySurfaceReport.GenerateMarkdownReport(tabs, history);

        // Drift-protection: regenerate and compare to the on-disk file.
        var reportPath = LocateOnDiskReport();
        if (reportPath is null)
        {
            // Fallback for CI that hasn't synced the knowledge-base sibling.
            // Just assert the report is well-formed instead.
            report.Should().Contain("# SWFOC Editor Capability Surface");
            report.Should().Contain("## Per-tab actions");
            return;
        }

        var onDisk = File.ReadAllText(reportPath).Replace("\r\n", "\n");

        // Auto-bootstrap on first run: if the file has the iter-61 placeholder
        // content, regenerate it. Operators force a refresh by manually
        // restoring the placeholder line and re-running this test.
        if (onDisk.StartsWith("PLACEHOLDER", StringComparison.Ordinal)
            || Environment.GetEnvironmentVariable("SWFOC_REGEN_CAPABILITY_SURFACE") == "1")
        {
            // 2026-04-28 (iter 67): also append the rollup to the
            // history file so operators see engine-effectiveness trends
            // over time. Same-date entries are deduplicated.
            // 2026-04-29 (iter 118): record history FIRST so the
            // regenerated report includes the new entry's trend line.
            // Otherwise the next non-regen run would mismatch because
            // it loads the now-updated history.
            var historyAppendPath = Path.Combine(
                Path.GetDirectoryName(reportPath)!,
                "capability_surface_history.jsonl");
            var rollup = CapabilitySurfaceReport.ComputeRollup(tabs);
            CapabilitySurfaceHistory.Record(rollup, historyAppendPath, DateTimeOffset.UtcNow);
            // Re-load history with the just-recorded entry, then regenerate.
            var freshHistory = CapabilitySurfaceHistory.LoadAll(historyAppendPath);
            var freshReport = CapabilitySurfaceReport.GenerateMarkdownReport(tabs, freshHistory);
            File.WriteAllText(reportPath, freshReport);
            // 2026-04-28 (iter 72): write the JSON sibling alongside
            // the markdown so tools/scripts can parse without scraping
            // tables. Fixed timestamp so the same-day regen is byte-stable.
            var jsonPath = Path.Combine(
                Path.GetDirectoryName(reportPath)!,
                "capability_surface_2026-04-27.json");
            var jsonReport = CapabilitySurfaceReport.GenerateJsonReport(
                tabs, freshHistory,
                generatedUtc: new DateTimeOffset(2026, 4, 28, 0, 0, 0, TimeSpan.Zero));
            File.WriteAllText(jsonPath, jsonReport);
            return;
        }

        onDisk.Should().Be(report,
            $"on-disk report at {reportPath} must match generated output. "
            + "Regenerate by overwriting with the PLACEHOLDER line + re-running this test, "
            + "or set SWFOC_REGEN_CAPABILITY_SURFACE=1");
    }

    [Fact]
    public void Report_RollupBadgeSection_ListsAllExpectedStatuses()
    {
        // Cheap pure-data test that doesn't need the simulator.
        var sample = new (string, IReadOnlyList<CapabilityAwareAction>)[]
        {
            ("TabA", new[]
            {
                new CapabilityAwareAction("Live action", "SWFOC_GodMode"),
                new CapabilityAwareAction("Pending", "SWFOC_FreezeAI"),
                new CapabilityAwareAction("Mixed",
                    "SWFOC_GodMode", "SWFOC_FreezeAI"),
            }),
        };

        var report = CapabilitySurfaceReport.GenerateMarkdownReport(sample);
        report.Should().Contain("`LIVE`");
        report.Should().Contain("`PHASE 2 PENDING`");
        report.Should().Contain("`MIXED (1/2 LIVE)`");
        report.Should().Contain("## Per-tab actions");
        report.Should().Contain("### TabA");
    }

    [Fact]
    public void Note_PropagatesFromCatalog()
    {
        // Iter 61: the new Note field on CapabilityAwareAction.
        var godMode = new CapabilityAwareAction("X", "SWFOC_GodMode");
        godMode.Note.Should().Contain("Hardpoint-behavior sweep");

        // v1.0.2: FreezeAI Note rewritten to lead with "USE LIVE ALTERNATIVE"
        // (SuspendAiLua) per operator-trust pattern. The deferred-work rationale
        // is now SECONDARY; the LIVE alternative pointer is PRIMARY.
        var freezeAi = new CapabilityAwareAction("X", "SWFOC_FreezeAI");
        freezeAi.Note.Should().Contain("USE LIVE ALTERNATIVE");
        freezeAi.Note.Should().Contain("SuspendAiLua");

        var unknown = new CapabilityAwareAction("X", "SWFOC_DoesNotExist");
        unknown.Note.Should().Contain("Not in catalogue");

        var multi = new CapabilityAwareAction("X", "SWFOC_GodMode", "SWFOC_FreezeAI");
        multi.Note.Should().Contain("·",
            "multi-helper actions join distinct notes with the bullet separator");
    }

    private static string? LocateOnDiskReport()
    {
        // Walk up looking for swfoc_memory/knowledge-base/.
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var sibling = Path.Combine(
                Path.GetDirectoryName(dir) ?? string.Empty,
                "swfoc_memory", "knowledge-base", "capability_surface_2026-04-27.md");
            if (File.Exists(sibling)) return sibling;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string? LocateOnDiskHistoryFile()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var sibling = Path.Combine(
                Path.GetDirectoryName(dir) ?? string.Empty,
                "swfoc_memory", "knowledge-base", "capability_surface_history.jsonl");
            if (File.Exists(sibling)) return sibling;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
