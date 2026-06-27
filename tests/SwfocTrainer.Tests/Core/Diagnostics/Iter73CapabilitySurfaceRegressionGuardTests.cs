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
using Xunit;

namespace SwfocTrainer.Tests.Core.Diagnostics;

/// <summary>
/// 2026-04-28 (iter 73) — CI guard against capability-surface
/// regressions. Builds the current rollup the same way iter-61's
/// integration test does, reads the iter-67 history, and asserts the
/// current LiveCount + (LiveCount + LiveOnlyCount) are at or above the
/// historic peaks.
///
/// Catches accidents like:
/// <list type="bullet">
///   <item>Someone removes a LIVE catalog entry</item>
///   <item>Someone flips a LIVE catalog entry to PHASE 2 PENDING</item>
///   <item>A refactor accidentally drops a tab's <c>AllActions</c> entry</item>
/// </list>
/// Does NOT catch <i>new tabs added with PHASE 2 PENDING entries</i> —
/// adding non-LIVE actions is legitimate growth and shouldn't fail CI.
/// </summary>
public sealed class Iter73CapabilitySurfaceRegressionGuardTests
{
    /// <summary>
    /// Locate the on-disk history file by walking parent directories
    /// from the test binary. Returns null when not reachable (CI
    /// without the swfoc_memory sibling) — the test then no-ops.
    /// </summary>
    private static string? LocateHistoryFile()
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

    private static CapabilitySurfaceReport.SurfaceRollup ComputeCurrentRollup()
    {
        // Mirror iter-61's CapabilitySurfaceReportIntegrationTests setup —
        // construct each VM with minimal dependencies, gather AllActions.
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var settings = new V2Settings();

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
            settings, corruption, diplomacy, storyEventsSvc, maphack, crashAnalyzer, factionRegistry, new V2UnitMutationDispatcher(adapter));

        var tabs = new (string, IReadOnlyList<CapabilityAwareAction>)[]
        {
            ("Tactical Units", new TacticalUnitsFilterTabViewModel(adapter).AllActions),
            ("Player State", playerState.AllActions),
            ("Economy", new EconomyTabViewModel(adapter).AllActions),
            ("Combat", new CombatTabViewModel(adapter, new V2UnitMutationDispatcher(adapter)).AllActions),
            ("Inspector", new InspectorTabViewModel(adapter, new V2UnitMutationDispatcher(adapter)).AllActions),
            ("Speed", new SpeedTabViewModel(adapter).AllActions),
            ("Spawning", new SpawningTabViewModel(adapter).AllActions),
            ("Galactic", new GalacticTabViewModel(adapter, new V2UnitMutationDispatcher(adapter)).AllActions),
            ("Hero Lab", new HeroLabTabViewModel(adapter).AllActions),
            ("Battle Control", new BattleControlTabViewModel(adapter).AllActions),
            ("Story Events", new StoryEventsTabViewModel(adapter).AllActions),
            ("Camera & Debug", new CameraDebugTabViewModel(adapter).AllActions),
            ("Lua Playground", new LuaPlaygroundTabViewModel(adapter).AllActions),
            ("Event Stream", new EventStreamViewModel(adapter).AllActions),
            ("Director Mode", new DirectorModeTabViewModel(adapter).AllActions),
            ("Cross-Faction", new CrossFactionRecruitmentTabViewModel(adapter).AllActions),
            ("Unit Stat Editor", new UnitStatEditorTabViewModel(adapter).AllActions),
            ("Quick Actions", new QuickActionsTabViewModel(adapter).AllActions),
            ("Unit Control", unitControl.AllActions),
            ("World State", worldState.AllActions),
            ("Probes & Scripts", new ProbesTabViewModel(adapter).AllActions),
        };
        return CapabilitySurfaceReport.ComputeRollup(tabs);
    }

    [Fact]
    public void LiveCount_DoesNotRegressBelowHistoricPeak()
    {
        var historyPath = LocateHistoryFile();
        if (historyPath is null)
        {
            // CI without the sibling — no-op (covered by other suites).
            return;
        }

        var history = CapabilitySurfaceHistory.LoadAll(historyPath);
        if (history.Count == 0)
        {
            // First run / pre-bootstrap state — nothing to compare against.
            return;
        }

        var historicPeakLive = history.Max(e => e.LiveCount);
        var current = ComputeCurrentRollup();

        current.LiveCount.Should().BeGreaterThanOrEqualTo(historicPeakLive,
            $"current LiveCount ({current.LiveCount}) regressed below the historic peak " +
            $"({historicPeakLive} from {history.OrderByDescending(e => e.LiveCount).First().Date}). " +
            "Did a LIVE catalog entry get removed or flipped to PHASE 2 PENDING by accident?");
    }

    [Fact]
    public void EngineEffectiveCount_DoesNotRegressBelowHistoricPeak()
    {
        var historyPath = LocateHistoryFile();
        if (historyPath is null) return;

        var history = CapabilitySurfaceHistory.LoadAll(historyPath);
        if (history.Count == 0) return;

        // Engine-effective = LIVE + LIVE ONLY (both have engine effect;
        // LIVE ONLY just needs a running game). Guard against this sum
        // separately so a LIVE → LIVE ONLY transition doesn't trip the
        // LiveCount guard but also can't sneak through here.
        var historicPeakEngine = history.Max(e => e.LiveCount + e.LiveOnlyCount);
        var current = ComputeCurrentRollup();
        var currentEngine = current.LiveCount + current.LiveOnlyCount;

        currentEngine.Should().BeGreaterThanOrEqualTo(historicPeakEngine,
            $"current engine-effective count ({currentEngine}) regressed below the " +
            $"historic peak ({historicPeakEngine}). LIVE + LIVE ONLY together must not shrink.");
    }

    [Fact]
    public void TotalActions_DoesNotRegressBelowHistoricPeak()
    {
        var historyPath = LocateHistoryFile();
        if (historyPath is null) return;

        var history = CapabilitySurfaceHistory.LoadAll(historyPath);
        if (history.Count == 0) return;

        // Total actions can grow (legitimate new buttons / tabs) but
        // shouldn't shrink — a shrink means a button was removed.
        // Tolerance of 1 to allow a single deliberate removal without
        // failing CI; bigger removals should fail and require a
        // history entry to acknowledge.
        var historicPeakTotal = history.Max(e => e.TotalActions);
        var current = ComputeCurrentRollup();

        current.TotalActions.Should().BeGreaterThanOrEqualTo(historicPeakTotal - 1,
            $"current TotalActions ({current.TotalActions}) regressed more than 1 below " +
            $"the historic peak ({historicPeakTotal}). " +
            "Buttons removed in bulk? Add a new history entry to acknowledge intentional removal.");
    }
}
