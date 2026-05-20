using System.Windows;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Assets;
using SwfocTrainer.Core.Contracts;

namespace SwfocTrainer.App.V2.ViewModels;

// ============================================================================
// MainViewModelV2
//
// Single non-partial top-level view-model owning six tab view-models. No
// inheritance on a base that mixes in 20 helpers. No dependency struct. No
// factory. Dependencies come in through the constructor; tabs are built in
// the constructor body.
//
// The brief allows extracting per-tab view-models — which we have done. This
// root is intentionally small.
// ============================================================================

public sealed class MainViewModelV2 : ObservableBase, IDisposable
{
    private readonly V2BridgeAdapter _bridge;
    private readonly V2Settings _settings;
    private bool _diagnosticsInitialized;

    public MainViewModelV2(
        V2BridgeAdapter bridge,
        V2Settings settings,
        IEconomyService economy,
        IHeroRespawnService heroRespawn,
        IFactionSwitchService factionSwitch,
        IGodModeService godMode,
        IOneHitKillService oneHitKill,
        IUnitInspectorService unitInspector,
        IHardpointService hardpoints,
        IEnhancedSpawnService enhancedSpawn,
        ICorruptionService corruption,
        IDiplomacyService diplomacy,
        IStoryEventService storyEvents,
        IMaphackService maphack,
        ICrashAnalyzerService crashAnalyzer,
        V2UnitMutationDispatcher unitMutator,
        V2FactionRegistry factions)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(heroRespawn);
        ArgumentNullException.ThrowIfNull(factionSwitch);
        ArgumentNullException.ThrowIfNull(godMode);
        ArgumentNullException.ThrowIfNull(oneHitKill);
        ArgumentNullException.ThrowIfNull(unitInspector);
        ArgumentNullException.ThrowIfNull(hardpoints);
        ArgumentNullException.ThrowIfNull(enhancedSpawn);
        ArgumentNullException.ThrowIfNull(corruption);
        ArgumentNullException.ThrowIfNull(diplomacy);
        ArgumentNullException.ThrowIfNull(storyEvents);
        ArgumentNullException.ThrowIfNull(maphack);
        ArgumentNullException.ThrowIfNull(crashAnalyzer);
        ArgumentNullException.ThrowIfNull(unitMutator);
        ArgumentNullException.ThrowIfNull(factions);

        _bridge = bridge;
        _settings = settings;

        Diagnostics = new DiagnosticsTabViewModel(bridge, settings);
        // 2026-05-07 (iter 309 originally; moved up by iter 319): create the
        // optional UnitIconResolver (iter-308) FIRST so all 4 consumer tabs
        // (Spawning iter-308 + Galactic iter-317 + HeroLab iter-318 +
        // PlayerState iter-319) can take it as a ctor arg. Resolution
        // priority handled by ResolveIconsRoot (extracted as static internal
        // helper for unit-testability).
        var iconResolver = new UnitIconResolver(ResolveIconsRoot(settings));
        // iter-319: pass the same iconResolver instance to PlayerState so
        // the Slot ComboBox renders faction emblems. 4th tab in the
        // hot-swap chain (Spawning iter-308 + Galactic iter-317 + HeroLab
        // iter-318 + PlayerState iter-319 — covers all 4 asset classes).
        PlayerState = new PlayerStateTabViewModel(bridge, settings, economy, heroRespawn, factionSwitch, unitMutator, factions, iconResolver);
        UnitControl = new UnitControlTabViewModel(bridge, settings, godMode, oneHitKill, unitInspector, hardpoints, enhancedSpawn, unitMutator, factions);
        WorldState = new WorldStateTabViewModel(settings, corruption, diplomacy, storyEvents, maphack, crashAnalyzer, factions, unitMutator);
        Probes = new ProbesTabViewModel(bridge);
        // 2026-05-07 (iter 301): pass bridge so the mod-picker can call
        // SWFOC_ListMods (iter-300) + SWFOC_GetCurrentMod (iter-299).
        Settings = new SettingsTabViewModel(settings, bridge);
        TacticalUnits = new TacticalUnitsFilterTabViewModel(bridge);
        Economy = new EconomyTabViewModel(bridge);
        // 2026-05-07 (iter 344): pass iconResolver to enable iter-343 Hardpoint
        // Inspector icon-resolution chain. Optional ctor param defaults to null
        // per iter-311 codified rule; explicit pass enables Approach A chain.
        Combat = new CombatTabViewModel(bridge, unitMutator, iconResolver);
        Inspector = new InspectorTabViewModel(bridge, unitMutator);
        Speed = new SpeedTabViewModel(bridge);
        // 2026-05-07 (iter 308, Thread D arc FINALE): the iconResolver above
        // also drives the Spawning tab ListBox unit-type icons.
        Spawning = new SpawningTabViewModel(bridge, iconResolver);
        // 2026-05-07 (iter 321): Asset Browser tab — surfaces ALL extracted
        // assets under IconsRoot in one DataGrid. 5th tab in the hot-swap
        // chain (closes the iter-313 honest defer; last UI consumer surface
        // in the Thread D arc).
        AssetBrowser = new AssetBrowserTabViewModel(iconResolver, ResolveIconsRoot(settings));
        // 2026-05-07 (iter 466): Savegame Rescue tab — operator surface for the
        // tools/savegame_rescue/ Python toolkit. LIVE; offline; works with no
        // SWFOC attach. No iconResolver dependency; no bridge dependency.
        SavegameRescue = new SavegameRescueTabViewModel();
        // 2026-05-07 (iter 467): Save Monitor tab — FileSystemWatcher over the
        // Save folder; logs every save event with size deltas; surfaces warning
        // when growth exceeds heuristic threshold (catches iter-466 soft-lock
        // pattern WHILE campaign is in progress).
        SaveMonitor = new SaveMonitorTabViewModel();
        // 2026-05-07 (iter 468): Save Auto-Tools tab — operator-driven custom
        // autosave (auto-copy on mutation OR every N minutes) with rotation.
        SaveAutoTools = new SaveAutoToolsTabViewModel();
        // 2026-05-07 (iter 471): Galaxy Visualizer tab — animated dashboard
        // with per-save health cards + corruption signals + mock galaxy
        // mini-map placeholder (real planet rendering needs 0x3EA RE).
        GalaxyVisualizer = new GalaxyVisualizerTabViewModel();

        // 2026-05-07 (iter 312, Thread D arc post-finale 2/2): live-update
        // the Spawning tab's resolver when operator changes Settings.IconsRoot.
        // Drops the iter-310 "restart editor for changes to take effect"
        // requirement. The Settings VM fires PropertyChanged for IconsRoot;
        // we re-resolve from settings (which may also pick up env-var fallback
        // changes between sessions) and hot-swap the Spawning resolver.
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        // iter-317: same iconResolver instance also drives the Galactic tab's
        // planet-icon column. Both tabs hot-swap together via
        // OnSettingsPropertyChanged below — single source of truth for
        // icon-root resolution, single point of mutation.
        Galactic = new GalacticTabViewModel(bridge, unitMutator, iconResolver);
        // iter-318: same iconResolver instance also drives the Hero Lab tab's
        // portrait column. All 3 tabs (Spawning + Galactic + HeroLab) hot-swap
        // together via OnSettingsPropertyChanged below — single source of
        // truth for icon-root resolution.
        HeroLab = new HeroLabTabViewModel(bridge, iconResolver);
        BattleControl = new BattleControlTabViewModel(bridge);
        StoryEvents = new StoryEventsTabViewModel(bridge);
        CameraDebug = new CameraDebugTabViewModel(bridge);
        LuaPlayground = new LuaPlaygroundTabViewModel(bridge);
        EventStream = new EventStreamViewModel(bridge);
        Director = new DirectorModeTabViewModel(bridge);
        CrossFaction = new CrossFactionRecruitmentTabViewModel(bridge);
        UnitStatEditor = new UnitStatEditorTabViewModel(bridge);
        // 2026-04-27 (iter 53): operator-facing composite quick-actions.
        QuickActions = new QuickActionsTabViewModel(bridge);

        // 2026-04-27 (iter 63): editor-wide capability surface roll-up
        // for the bottom status bar. Walks every bridge-using V2 tab's
        // AllActions and aggregates the badge counts. Computed once at
        // construction since the catalog is static — the numbers don't
        // change at runtime.
        CapabilitySurface = SwfocTrainer.Core.Diagnostics.CapabilitySurfaceReport.ComputeRollup(
            CollectTabsForSurface());
        // 2026-04-28 (iter 68): pull history if the swfoc_memory sibling
        // is reachable so the bottom-bar tooltip can show "+Npp over M
        // entries". Best-effort — first-run editors with no history just
        // show an empty trend line.
        CapabilitySurfaceTrend = LoadCapabilitySurfaceTrend();

        // 2026-04-28 (iter 69): per-tab capability tooltips. Operators
        // hover any tab header and see "Combat · 2 LIVE · 6 PHASE 2 ·
        // 25% engine-effective" without clicking through. Computed
        // once at construction since AllActions is static per tab.
        TabTooltips = BuildPerTabTooltips();
        // 2026-04-28 (iter 88): top-3 most-PHASE-2-PENDING tabs surfaced
        // in the bottom-bar tooltip. Operators see at-a-glance which tabs
        // have the most engine-side work remaining without hovering each
        // tab header. Computed once at construction (catalog is static).
        TopPhase2PendingTabs = BuildTopPhase2PendingTabs();
    }

    /// <summary>
    /// 2026-04-28 (iter 88) — returns the top-3 tabs with the most
    /// PHASE 2 PENDING actions, formatted as a multiline string. Used in
    /// <see cref="CapabilitySurfaceTooltip"/> to consolidate "where's
    /// the engine work" into the bottom-bar hover.
    /// </summary>
    private string BuildTopPhase2PendingTabs() =>
        FormatTopPhase2PendingTabs(CollectTabsForSurface());

    /// <summary>
    /// 2026-04-28 (iter 88) — testable, side-effect-free formatter for
    /// the top-3 PHASE 2 PENDING tabs. Tests drive this directly with
    /// synthetic tab data; production passes through
    /// <see cref="CollectTabsForSurface"/>.
    /// </summary>
    internal static string FormatTopPhase2PendingTabs(
        IEnumerable<(string TabName, IReadOnlyList<SwfocTrainer.Core.Diagnostics.CapabilityAwareAction> Actions)> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        var ranked = tabs
            .Select(kvp => new
            {
                Name = kvp.TabName,
                Pending = kvp.Actions.Sum(a =>
                    a.HelperNames.Count(h =>
                        SwfocTrainer.Core.Diagnostics.CapabilityStatusCatalog
                            .Lookup(h).Status == SwfocTrainer.Core.Diagnostics.CapabilityStatus.Phase2HookPending)),
            })
            .Where(r => r.Pending > 0)
            .OrderByDescending(r => r.Pending)
            .Take(3)
            .ToList();
        if (ranked.Count == 0) return string.Empty;
        return string.Join("\n", ranked.Select(r =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  • {0}: {1} PHASE 2 PENDING", r.Name, r.Pending)));
    }

    /// <summary>
    /// 2026-04-28 (iter 88) — top-3 PHASE-2-PENDING tab list for the
    /// bottom-bar tooltip. Empty when nothing is pending (target end-state).
    /// </summary>
    public string TopPhase2PendingTabs { get; }

    private Dictionary<string, string> BuildPerTabTooltips()
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (tabName, actions) in CollectTabsForSurface())
        {
            var rollup = SwfocTrainer.Core.Diagnostics.CapabilitySurfaceReport.ComputeRollup(
                new[] { (tabName, actions) });
            dict[tabName] = FormatTabTooltip(tabName, rollup);
        }
        return dict;
    }

    /// <summary>
    /// 2026-04-28 (iter 69): pure formatter for per-tab capability
    /// tooltips. Internal so unit tests can pin the format without
    /// constructing the full <see cref="MainViewModelV2"/> (which needs
    /// 17 services). Format tuned for tab headers — short, no
    /// "Capability:" prefix since that's redundant on a tab tooltip.
    /// </summary>
    internal static string FormatTabTooltip(
        string tabName,
        SwfocTrainer.Core.Diagnostics.CapabilitySurfaceReport.SurfaceRollup rollup)
    {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0} · {1} LIVE · {2} PHASE 2 · {3} actions · {4}% engine-effective",
            tabName, rollup.LiveCount, rollup.Phase2PendingCount,
            rollup.TotalActions, rollup.LivePercent);
    }

    private static string LoadCapabilitySurfaceTrend()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (var i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            var sibling = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(dir) ?? string.Empty,
                "swfoc_memory", "knowledge-base", "capability_surface_history.jsonl");
            if (System.IO.File.Exists(sibling))
            {
                try
                {
                    var history = SwfocTrainer.Core.Diagnostics.CapabilitySurfaceHistory.LoadAll(sibling);
                    return SwfocTrainer.Core.Diagnostics.CapabilitySurfaceHistory.BuildTrendLine(history);
                }
                catch
                {
                    return string.Empty;
                }
            }
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        return string.Empty;
    }

    /// <summary>
    /// 2026-04-27 (iter 63): walks every bridge-using V2 tab and pairs
    /// each one's display name with its <c>AllActions</c> list. Order
    /// matches the editor's TabControl so the surface report (iter 61)
    /// and the runtime roll-up (iter 63) walk the editor in the same
    /// order.
    /// </summary>
    private IEnumerable<(string TabName, IReadOnlyList<SwfocTrainer.Core.Diagnostics.CapabilityAwareAction> Actions)>
        CollectTabsForSurface()
    {
        yield return ("Tactical Units", TacticalUnits.AllActions);
        yield return ("Player State", PlayerState.AllActions);
        yield return ("Economy", Economy.AllActions);
        yield return ("Combat", Combat.AllActions);
        yield return ("Inspector", Inspector.AllActions);
        yield return ("Speed", Speed.AllActions);
        yield return ("Spawning", Spawning.AllActions);
        yield return ("Galactic", Galactic.AllActions);
        yield return ("Hero Lab", HeroLab.AllActions);
        yield return ("Battle Control", BattleControl.AllActions);
        yield return ("Story Events", StoryEvents.AllActions);
        yield return ("Camera & Debug", CameraDebug.AllActions);
        yield return ("Lua Playground", LuaPlayground.AllActions);
        yield return ("Event Stream", EventStream.AllActions);
        yield return ("Director Mode", Director.AllActions);
        yield return ("Cross-Faction", CrossFaction.AllActions);
        yield return ("Unit Stat Editor", UnitStatEditor.AllActions);
        yield return ("Quick Actions", QuickActions.AllActions);
        yield return ("Unit Control", UnitControl.AllActions);
        yield return ("World State", WorldState.AllActions);
        yield return ("Probes & Scripts", Probes.AllActions);
    }

    /// <summary>
    /// 2026-04-27 (iter 63): editor-wide capability surface roll-up.
    /// Headline numbers visible in the bottom status bar so operators
    /// see the editor's engine-effectiveness ratio at a glance from
    /// every tab.
    /// </summary>
    public SwfocTrainer.Core.Diagnostics.CapabilitySurfaceReport.SurfaceRollup CapabilitySurface { get; }

    /// <summary>
    /// 2026-04-28 (iter 68): trend line built from
    /// <c>capability_surface_history.jsonl</c> when reachable. Empty
    /// when the history file isn't found or has fewer than 2 entries
    /// (first run). The bottom status bar tooltip surfaces this so
    /// operators see Phase 2 progress (or regression!) at hover time.
    /// </summary>
    public string CapabilitySurfaceTrend { get; }

    /// <summary>
    /// Composite tooltip for the bottom-bar capability indicator.
    /// Combines the iter-63 summary line with the iter-68 trend on a
    /// second line when available.
    /// </summary>
    public string CapabilitySurfaceTooltip
    {
        get
        {
            var sb = new System.Text.StringBuilder(512);
            sb.Append("Auto-aggregated from every V2 tab's CapabilityAwareAction list.");
            if (!string.IsNullOrEmpty(CapabilitySurfaceTrend))
            {
                sb.Append("\n\nTrend: ").Append(CapabilitySurfaceTrend);
            }
            // 2026-04-28 (iter 88): show the 3 tabs with the most engine
            // work remaining. Operators planning Phase 2 work see at a
            // glance where the largest impact would be.
            if (!string.IsNullOrEmpty(TopPhase2PendingTabs))
            {
                sb.Append("\n\nTop tabs with engine work remaining:\n").Append(TopPhase2PendingTabs);
            }
            sb.Append("\n\nSee knowledge-base/capability_surface_*.md for the full breakdown (Diagnostics → 'Open surface report').");
            return sb.ToString();
        }
    }

    /// <summary>
    /// 2026-04-28 (iter 69): per-tab capability tooltips. Keyed by the
    /// editor's display tab name. The XAML binds each TabItem.ToolTip to
    /// the entry for its tab so operators hover and see "TabName · X
    /// LIVE · Y PHASE 2 · Z% engine-effective" without clicking through.
    /// </summary>
    public IReadOnlyDictionary<string, string> TabTooltips { get; }

    /// <summary>
    /// Convenience accessor for XAML binding — returns the empty string
    /// when a tab name isn't found so binding never throws.
    /// </summary>
    public string GetTabTooltip(string tabName) =>
        TabTooltips.TryGetValue(tabName, out var t) ? t : string.Empty;

    // Per-tab tooltip exposers — XAML binds these directly so each
    // TabItem.ToolTip references the correct rollup line. Keys match
    // CollectTabsForSurface() exactly.
    public string TooltipTacticalUnits => GetTabTooltip("Tactical Units");
    public string TooltipPlayerState => GetTabTooltip("Player State");
    public string TooltipEconomy => GetTabTooltip("Economy");
    public string TooltipCombat => GetTabTooltip("Combat");
    public string TooltipInspector => GetTabTooltip("Inspector");
    public string TooltipSpeed => GetTabTooltip("Speed");
    public string TooltipSpawning => GetTabTooltip("Spawning");
    public string TooltipGalactic => GetTabTooltip("Galactic");
    public string TooltipHeroLab => GetTabTooltip("Hero Lab");
    public string TooltipBattleControl => GetTabTooltip("Battle Control");
    public string TooltipStoryEvents => GetTabTooltip("Story Events");
    public string TooltipCameraDebug => GetTabTooltip("Camera & Debug");
    public string TooltipLuaPlayground => GetTabTooltip("Lua Playground");
    public string TooltipEventStream => GetTabTooltip("Event Stream");
    public string TooltipDirector => GetTabTooltip("Director Mode");
    public string TooltipCrossFaction => GetTabTooltip("Cross-Faction");
    public string TooltipUnitStatEditor => GetTabTooltip("Unit Stat Editor");
    public string TooltipQuickActions => GetTabTooltip("Quick Actions");
    public string TooltipUnitControl => GetTabTooltip("Unit Control");
    public string TooltipWorldState => GetTabTooltip("World State");
    public string TooltipProbes => GetTabTooltip("Probes & Scripts");

    public DiagnosticsTabViewModel Diagnostics { get; }

    public PlayerStateTabViewModel PlayerState { get; }

    public UnitControlTabViewModel UnitControl { get; }

    public WorldStateTabViewModel WorldState { get; }

    public ProbesTabViewModel Probes { get; }

    public SettingsTabViewModel Settings { get; }

    /// <summary>Phase 1 thread A — DataGrid-backed filterable tactical unit list.</summary>
    public TacticalUnitsFilterTabViewModel TacticalUnits { get; }

    /// <summary>Phase 1 thread A — economy-control tab over Core.V2Vm.EconomyTabState.</summary>
    public EconomyTabViewModel Economy { get; }

    /// <summary>Unit D — combat helpers over Core.V2Vm.CombatTabState.</summary>
    public CombatTabViewModel Combat { get; }

    /// <summary>Unit D — live unit inspector over Core.V2Vm.InspectorTabState.</summary>
    public InspectorTabViewModel Inspector { get; }

    /// <summary>Unit D — game / faction / unit speed control.</summary>
    public SpeedTabViewModel Speed { get; }

    /// <summary>Unit D — searchable unit-type spawner.</summary>
    public SpawningTabViewModel Spawning { get; }

    /// <summary>
    /// 2026-05-07 (iter 321, Asset Browser tab kickoff): cross-asset-class
    /// browser. Walks IconsRoot for all i_*_*.dds prefixes (unit + hero +
    /// planet + faction) and surfaces them in one DataGrid.
    /// </summary>
    public AssetBrowserTabViewModel AssetBrowser { get; }

    /// <summary>
    /// 2026-05-07 (iter 466, Savegame Rescue tab kickoff): operator surface
    /// for the <c>tools/savegame_rescue/</c> Python toolkit. Backup / Diagnose
    /// / Diff / Repair workflows; offline; LIVE; no bridge dependency.
    /// </summary>
    public SavegameRescueTabViewModel SavegameRescue { get; }

    /// <summary>
    /// 2026-05-07 (iter 467, Save Monitor tab kickoff): FileSystemWatcher
    /// over the Save folder. Logs every save event + warns on anomalous growth.
    /// </summary>
    public SaveMonitorTabViewModel SaveMonitor { get; }

    /// <summary>
    /// 2026-05-07 (iter 468, Save Auto-Tools tab kickoff): custom-pattern
    /// autosave + interval-snapshot timer with rotation policy.
    /// </summary>
    public SaveAutoToolsTabViewModel SaveAutoTools { get; }

    /// <summary>
    /// 2026-05-07 (iter 471, Galaxy Visualizer tab kickoff): animated save
    /// dashboard with per-save health cards + corruption signals + galaxy
    /// mini-map placeholder.
    /// </summary>
    public GalaxyVisualizerTabViewModel GalaxyVisualizer { get; }

    /// <summary>
    /// 2026-05-07 (iter 469, Mode switcher): operator-visible mode flag.
    /// Live = trainer tabs visible; Savegame = savegame tabs visible.
    /// Wired to TabItem.Visibility via <see cref="LiveTabsVisibility"/> and
    /// <see cref="SavegameTabsVisibility"/>.
    /// </summary>
    public bool IsLiveMode
    {
        get => _isLiveMode;
        set
        {
            if (SetField(ref _isLiveMode, value))
            {
                OnPropertyChanged(nameof(LiveTabsVisibility));
                OnPropertyChanged(nameof(SavegameTabsVisibility));
                OnPropertyChanged(nameof(ModeBadge));
                OnPropertyChanged(nameof(WindowTitle)); // v1.0.2: title reflects mode
            }
        }
    }
    private bool _isLiveMode = true;

    public Visibility LiveTabsVisibility => _isLiveMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SavegameTabsVisibility => _isLiveMode ? Visibility.Collapsed : Visibility.Visible;
    public string ModeBadge => _isLiveMode ? "LIVE TRAINER" : "SAVEGAME EDITOR";

    public ICommand SetLiveModeCommand => _setLiveModeCommand ??= new RelayCommand(() => IsLiveMode = true);
    public ICommand SetSavegameModeCommand => _setSavegameModeCommand ??= new RelayCommand(() => IsLiveMode = false);
    private ICommand? _setLiveModeCommand;
    private ICommand? _setSavegameModeCommand;

    /// <summary>
    /// 2026-05-07 (iter 309, Thread D arc post-finale): icons-root resolution
    /// priority for the iter-308 UnitIconResolver:
    ///   1. <paramref name="settings"/>.IconsRoot (operator-explicit, persisted)
    ///   2. SWFOC_EXTRACTED_DDS_ROOT env var (operator-explicit, session-only)
    ///   3. null = no icons (graceful — null IconPath hides the Image control)
    /// Whitespace-only settings value falls through to the env var. Extracted
    /// as a static internal helper so the lookup precedence can be unit-tested
    /// without standing up the full MainViewModelV2 dependency graph.
    /// </summary>
    internal static string? ResolveIconsRoot(V2Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!string.IsNullOrWhiteSpace(settings.IconsRoot))
        {
            return settings.IconsRoot;
        }
        var fromEnv = Environment.GetEnvironmentVariable("SWFOC_EXTRACTED_DDS_ROOT");
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv;
    }

    /// <summary>
    /// 2026-05-07 (iter 312, Thread D arc post-finale 2/2): subscriber for
    /// Settings.PropertyChanged that hot-swaps the Spawning tab's icon
    /// resolver when operator changes IconsRoot. Filtered to the IconsRoot
    /// property name to avoid rebuilding rows on unrelated Settings changes
    /// (GamePath, theme, etc.).
    /// </summary>
    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsTabViewModel.IconsRoot))
        {
            // iter-317/318/319/321: hot-swap all 5 tabs that consume the resolver.
            // Build a single new instance and pass to all — they don't share
            // state beyond the settings root, so duplication is fine and
            // avoids accidental cross-tab coupling. Asset Browser also gets
            // the new root path for its file-system walker.
            var newRoot = ResolveIconsRoot(_settings);
            var newResolver = new UnitIconResolver(newRoot);
            Spawning.SetIconResolver(newResolver);
            Galactic.SetIconResolver(newResolver);
            HeroLab.SetIconResolver(newResolver);
            PlayerState.SetIconResolver(newResolver);
            AssetBrowser.SetIconResolver(newResolver);
            // 2026-05-07 (iter 344): hot-swap Combat tab Hardpoint Inspector
            // icon-resolution chain (iter-343). 6th consumer of the resolver
            // hot-swap chain (after Spawning/Galactic/HeroLab/PlayerState/Asset).
            Combat.SetIconResolver(newResolver);
            AssetBrowser.SetIconsRoot(newRoot);
        }
    }

    /// <summary>Unit D — galactic-map planet roster + diplomacy + reveal-all toggle.</summary>
    public GalacticTabViewModel Galactic { get; }

    /// <summary>Unit D — hero roster, respawn timers, permadeath, kill/revive, stat edits.</summary>
    public HeroLabTabViewModel HeroLab { get; }

    /// <summary>Unit D — content-creator one-click battle controls (freeze AI, kill all, heal all, unit cap).</summary>
    public BattleControlTabViewModel BattleControl { get; }

    /// <summary>Unit D — story event dispatcher + flag editor.</summary>
    public StoryEventsTabViewModel StoryEvents { get; }

    /// <summary>Unit D — free-cam, camera pose, camera zoom, raw-Lua escape hatch.</summary>
    public CameraDebugTabViewModel CameraDebug { get; }

    /// <summary>Unit D — free-form Lua editor + recipe library.</summary>
    public LuaPlaygroundTabViewModel LuaPlayground { get; }

    /// <summary>Unit D — engine #112 damage-event ring buffer drain + filter view.</summary>
    public EventStreamViewModel EventStream { get; }

    /// <summary>Unit D — camera path / hide-UI / time-scale director tooling.</summary>
    public DirectorModeTabViewModel Director { get; }

    /// <summary>Unit D — single-unit ownership transfer (local-only source).</summary>
    public CrossFactionRecruitmentTabViewModel CrossFaction { get; }

    /// <summary>Unit D — bulk stat edits across pasted obj_addr lists.</summary>
    public UnitStatEditorTabViewModel UnitStatEditor { get; }

    /// <summary>2026-04-27 (iter 53) — operator-facing composite quick-actions.</summary>
    public QuickActionsTabViewModel QuickActions { get; }

    // v1.0.2 hotfix: include the mode badge so operators can see at a glance
    // whether they're in LIVE TRAINER or SAVEGAME EDITOR mode without having
    // to look at the top-bar pill. Bound to IsLiveMode via OnPropertyChanged
    // below.
    public string WindowTitle =>
        $"SWFOC Trainer Editor — {ModeBadge} — pipe {_bridge.PipeName}";

    /// <summary>
    /// Called by MainWindowV2 when Loaded fires. Kicks off diagnostic probes
    /// exactly once (auto-connect respects the V2Settings toggle), then
    /// auto-refreshes the player slot↔faction map so the Player slot
    /// dropdown shows "Slot 6 — UNDERWORLD" instead of bare integers without
    /// requiring the operator to click Refresh slot map manually
    /// (2026-04-27).
    /// </summary>
    public async Task OnWindowLoadedAsync()
    {
        if (_diagnosticsInitialized)
        {
            return;
        }

        _diagnosticsInitialized = true;

        if (_settings.AutoConnectOnStartup)
        {
            await Diagnostics.InitializeAsync().ConfigureAwait(true);

            // Slot labels depend on a live SWFOC_GetAllPlayers probe. Fire
            // the same probe the Refresh button binds to — if the bridge
            // is down or no game is loaded the call no-ops with a logged
            // [err] line (operator can still click Refresh manually).
            await PlayerState.RefreshSlotMapAsync().ConfigureAwait(true);
            await Galactic.RefreshPlanetsAsync().ConfigureAwait(true);
            await HeroLab.RefreshHeroesAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 2026-04-27 (iter 12): one-click access to the go-live smoke
    /// checklist. Resolves <c>knowledge-base/go_live_smoke_checklist_*.md</c>
    /// in the swfoc_memory sibling directory and launches via the OS
    /// default association (markdown viewer / VSCode / etc.).
    /// </summary>
    public RelayCommand OpenHelpCommand =>
        _openHelpCommand ??= new RelayCommand(OpenSmokeChecklist);
    private RelayCommand? _openHelpCommand;

    private void OpenSmokeChecklist()
    {
        // Walk the working directory upward looking for swfoc_memory/
        // knowledge-base/. Operator's clone may live anywhere.
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            // Try sibling: <root>/swfoc_memory/knowledge-base/go_live_smoke_checklist_*.md
            var sibling = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(dir) ?? string.Empty,
                "swfoc_memory", "knowledge-base");
            if (System.IO.Directory.Exists(sibling))
            {
                var candidate = System.IO.Directory.EnumerateFiles(
                        sibling, "go_live_smoke_checklist_*.md")
                    .OrderByDescending(f => f) // most recent date wins
                    .FirstOrDefault();
                if (candidate is not null)
                {
                    LaunchPath(candidate);
                    return;
                }
            }
            // Try same-level: <root>/knowledge-base/...
            var same = System.IO.Path.Combine(dir, "knowledge-base");
            if (System.IO.Directory.Exists(same))
            {
                var candidate = System.IO.Directory.EnumerateFiles(
                        same, "go_live_smoke_checklist_*.md")
                    .OrderByDescending(f => f).FirstOrDefault();
                if (candidate is not null)
                {
                    LaunchPath(candidate);
                    return;
                }
            }
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        // Couldn't find the file — fall back to the project URL or a stub
        // (we don't ship a hosted copy yet, so just no-op silently).
    }

    private static void LaunchPath(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // Best-effort — no surface to log to from MainViewModel.
        }
    }

    public void Dispose()
    {
        Diagnostics.Dispose();
    }
}
