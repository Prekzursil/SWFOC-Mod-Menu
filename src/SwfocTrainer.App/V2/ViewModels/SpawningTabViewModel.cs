using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Assets;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Spawning tab) — INPC wrapper around SpawningTabState.
/// The available-types catalogue is sourced from the modder via SetAvailableTypes
/// (typically loaded from a mod's GameObjects.xml at app startup); the VM
/// re-filters whenever SearchQuery changes and surfaces the result through
/// FilteredTypes for ListBox binding.
/// </summary>
public sealed class SpawningTabViewModel : ObservableBase
{
    // Sentinel for the "no filter" entry in the faceted filter ComboBoxes.
    // We surface it as a user-visible option so the operator can clearly
    // see "All factions" / "All domains" rather than an empty entry.
    private const string AllFilterValue = "All";

    private readonly SpawningTabState _state;
    private readonly RecordingFeedbackSink _sink;
    private readonly ObservableCollection<string> _filteredTypes = new();
    // 2026-05-07 (iter 308, Thread D arc FINALE): parallel row collection
    // bound by the ListBox ItemTemplate to render an in-game icon next to
    // each unit-type name. Stays in lock-step with `_filteredTypes` via
    // RefreshFilteredTypes — the resolver provides the IconPath (or null
    // if the operator hasn't extracted/cached the icon yet).
    private readonly ObservableCollection<UnitTypeRow> _filteredTypeRows = new();
    // 2026-05-07 (iter 312, Thread D arc post-finale 2/2): mutable so the
    // composition root can hot-swap the resolver when Settings.IconsRoot
    // changes — drops the iter-310 "restart editor" requirement.
    private UnitIconResolver? _iconResolver;
    private readonly ObservableCollection<string> _availableFactions = new() { AllFilterValue };
    private readonly ObservableCollection<string> _availableDomains = new()
    {
        AllFilterValue, "Space", "Ground", "Unknown",
    };

    private string _selectedTypeId = string.Empty;
    private int _factionSlot = -1;
    private float _posX;
    private float _posY;
    private float _posZ;
    private int _count = 1;
    private string _searchQuery = string.Empty;
    private string _selectedFactionFilter = AllFilterValue;
    private string _selectedDomainFilter = AllFilterValue;
    private string _lastStatus = "(idle)";

    private readonly V2BridgeAdapter _bridge;

    // 2026-05-07 (iter 308): optional resolver lets MainViewModelV2 wire in
    // an UnitIconResolver pointing at the operator's extracted-DDS root.
    // Default null = no icons (graceful — null IconPath hides the Image
    // control). Mirrors the iter-301 SettingsTabViewModel optional-bridge
    // pattern so existing constructor callers stay source-compatible.
    public SpawningTabViewModel(V2BridgeAdapter bridge,
        UnitIconResolver? iconResolver = null)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
        _iconResolver = iconResolver;
        _sink = new RecordingFeedbackSink();
        var dispatcher = new BridgeSpawningDispatcher(bridge);
        _state = new SpawningTabState(dispatcher, _sink);

        SpawnCommand = new AsyncRelayCommand(SpawnCore, CanSpawnSelectedType, HandleError);
        RefreshFromLiveGameCommand = new AsyncRelayCommand(
            RefreshFromLiveGameAsync, onError: HandleError);
        // 2026-04-29 (iter 119): SpawnUnitLua LIVE-wire button.
        SpawnUnitLuaCommand = new AsyncRelayCommand(SpawnUnitLuaCore, onError: HandleError);

        // 2026-05-05 (iter 195): iter-185 spawn variants. Each takes the same
        // 3 fields as iter-119 but composes a different SWFOC_* wire. Operator
        // can pick any of 4 spawn entrypoints from the same input panel.
        ReinforceUnitLuaCommand = new AsyncRelayCommand(ReinforceUnitLuaCore, onError: HandleError);
        SpawnFromReinforcementPoolLuaCommand = new AsyncRelayCommand(
            SpawnFromReinforcementPoolLuaCore, onError: HandleError);
        CreateGenericObjectLuaCommand = new AsyncRelayCommand(
            CreateGenericObjectLuaCore, onError: HandleError);

        // Primary Spawn is routed through the LIVE Lua engine API. The old
        // SWFOC_SpawnUnit mirror stays in the lower-level state object for
        // regression coverage, but the operator-facing button must not call
        // a Phase-1-only helper.
        Spawn = new CapabilityAwareAction("Spawn", "SWFOC_SpawnUnitLua");
        RefreshFromLiveGame = new CapabilityAwareAction("Refresh from live game",
            "SWFOC_BatchTypeExists");
        // 2026-04-29 (iter 119): the LIVE alternative to the PHASE 2 PENDING
        // Spawn button. Uses three Lua expressions (player / type / position)
        // composed via the iter 109 SWFOC_SpawnUnitLua engine wire.
        SpawnUnitLua = new CapabilityAwareAction("Spawn (Lua, LIVE)",
            "SWFOC_SpawnUnitLua");

        // 2026-05-05 (iter 195): iter-185 spawn variants. All LIVE since iter 185.
        ReinforceUnitLua = new CapabilityAwareAction("Reinforce unit (Lua)",
            "SWFOC_ReinforceUnitLua");
        SpawnFromReinforcementPoolLua = new CapabilityAwareAction(
            "Spawn from reinforcement pool (Lua)", "SWFOC_SpawnFromReinforcementPoolLua");
        CreateGenericObjectLua = new CapabilityAwareAction(
            "Create generic object (Lua)", "SWFOC_CreateGenericObjectLua");

        // 2026-05-05 (iter 203): Discovery helpers — return engine handles
        // operators can pipe into the spawn buttons above. iter-177 trio
        // (1-arg getters) + iter-186 Find_Nearest (3-arg getter — first
        // wire to use the new generic 3-arg builder). Captured value lands
        // in LastStatus so operators can see the returned handle.
        FindObjectTypeLuaCommand = new AsyncRelayCommand(FindObjectTypeLuaCore, onError: HandleError);
        FindPlanetLuaCommand = new AsyncRelayCommand(FindPlanetLuaCore, onError: HandleError);
        FindFirstObjectLuaCommand = new AsyncRelayCommand(FindFirstObjectLuaCore, onError: HandleError);
        FindNearestLuaCommand = new AsyncRelayCommand(FindNearestLuaCore, onError: HandleError);
        // 2026-05-05 (iter 206): iter-179 Find_All_Objects_Of_Type discovery
        // helper. Completes the "first / nearest / all" trio alongside iter-203.
        FindAllObjectsOfTypeLuaCommand = new AsyncRelayCommand(
            FindAllObjectsOfTypeLuaCore, onError: HandleError);

        FindObjectTypeLua = new CapabilityAwareAction("Find object type (Lua)",
            "SWFOC_FindObjectTypeLua");
        FindPlanetLua = new CapabilityAwareAction("Find planet (Lua)",
            "SWFOC_FindPlanetLua");
        FindFirstObjectLua = new CapabilityAwareAction("Find first object (Lua)",
            "SWFOC_FindFirstObjectLua");
        FindNearestLua = new CapabilityAwareAction("Find nearest (Lua)",
            "SWFOC_FindNearestLua");
        // 2026-05-05 (iter 206): iter-179 capability action.
        FindAllObjectsOfTypeLua = new CapabilityAwareAction("Find all objects of type (Lua)",
            "SWFOC_FindAllObjectsOfTypeLua");
    }

    public CapabilityAwareAction Spawn { get; }
    public CapabilityAwareAction RefreshFromLiveGame { get; }
    public CapabilityAwareAction SpawnUnitLua { get; }

    // 2026-05-05 (iter 195) — iter-185 spawn variant capability actions.
    public CapabilityAwareAction ReinforceUnitLua { get; }
    public CapabilityAwareAction SpawnFromReinforcementPoolLua { get; }
    public CapabilityAwareAction CreateGenericObjectLua { get; }
    // 2026-05-05 (iter 203) — discovery helpers (iter-177 + iter-186).
    public CapabilityAwareAction FindObjectTypeLua { get; }
    public CapabilityAwareAction FindPlanetLua { get; }
    public CapabilityAwareAction FindFirstObjectLua { get; }
    public CapabilityAwareAction FindNearestLua { get; }
    // 2026-05-05 (iter 206) — iter-179 Find_All_Objects_Of_Type extension.
    public CapabilityAwareAction FindAllObjectsOfTypeLua { get; }

    public IReadOnlyList<CapabilityAwareAction> AllActions => new[]
    {
        Spawn, RefreshFromLiveGame, SpawnUnitLua,
        // iter 195: spawn variants
        ReinforceUnitLua, SpawnFromReinforcementPoolLua, CreateGenericObjectLua,
        // iter 203: discovery helpers
        FindObjectTypeLua, FindPlanetLua, FindFirstObjectLua, FindNearestLua,
        // iter 206: discovery extension
        FindAllObjectsOfTypeLua,
    };

    public bool HasPhase2PendingAction => AllActions.Any(a => !a.IsAllLive);

    public string Phase2PendingWarning
    {
        get
        {
            var pending = AllActions.Where(a => !a.IsAllLive).ToList();
            if (pending.Count == 0) return string.Empty;
            var parts = pending.Select(a => $"{a.Name} ({a.Badge})");
            return "Some actions on this tab are PHASE 2 PENDING; they are disabled "
                + "until a live engine hook exists. Affected: "
                + string.Join("; ", parts);
        }
    }

    public string SelectedTypeId
    {
        get => _selectedTypeId;
        set { if (SetField(ref _selectedTypeId, value ?? string.Empty)) _state.SelectedTypeId = _selectedTypeId; }
    }

    public int FactionSlot
    {
        get => _factionSlot;
        set { if (SetField(ref _factionSlot, value)) _state.FactionSlot = value; }
    }

    public float PosX
    {
        get => _posX;
        set { if (SetField(ref _posX, value)) _state.PosX = value; }
    }

    public float PosY
    {
        get => _posY;
        set { if (SetField(ref _posY, value)) _state.PosY = value; }
    }

    public float PosZ
    {
        get => _posZ;
        set { if (SetField(ref _posZ, value)) _state.PosZ = value; }
    }

    public int Count
    {
        get => _count;
        set { if (SetField(ref _count, value)) _state.Count = value; }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetField(ref _searchQuery, value ?? string.Empty))
            {
                _state.SearchQuery = _searchQuery;
                RefreshFilteredTypes();
            }
        }
    }

    /// <summary>
    /// Faceted faction filter. Selecting "All" clears the filter; any
    /// other entry narrows the type browser to ids that contain the
    /// selected faction substring (e.g. "EMPIRE", "AOTR_REBEL").
    /// </summary>
    public string SelectedFactionFilter
    {
        get => _selectedFactionFilter;
        set
        {
            var v = string.IsNullOrEmpty(value) ? AllFilterValue : value;
            if (SetField(ref _selectedFactionFilter, v))
            {
                _state.FactionFilter = string.Equals(v, AllFilterValue, StringComparison.Ordinal)
                    ? string.Empty : v;
                RefreshFilteredTypes();
            }
        }
    }

    /// <summary>
    /// Faceted domain filter. Selecting "All" clears the filter; "Space",
    /// "Ground", or "Unknown" narrows the browser via the heuristic
    /// classifier in <see cref="SpawningTabState.ClassifyDomain"/>.
    /// </summary>
    public string SelectedDomainFilter
    {
        get => _selectedDomainFilter;
        set
        {
            var v = string.IsNullOrEmpty(value) ? AllFilterValue : value;
            if (SetField(ref _selectedDomainFilter, v))
            {
                _state.DomainFilter = string.Equals(v, AllFilterValue, StringComparison.Ordinal)
                    ? string.Empty : v;
                RefreshFilteredTypes();
            }
        }
    }

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge("SWFOC_SpawnUnitLua");

    public ObservableCollection<string> FilteredTypes => _filteredTypes;

    /// <summary>
    /// 2026-05-07 (iter 308, Thread D arc FINALE): parallel row collection
    /// bound by the Spawning tab ListBox ItemTemplate to show an in-game
    /// icon next to each unit-type name. Each row's IconPath is resolved
    /// via iter-307 ThumbnailCache + iter-308 UnitIconResolver; null when
    /// no resolver is wired OR the operator hasn't extracted the DDS yet.
    /// </summary>
    public ObservableCollection<UnitTypeRow> FilteredTypeRows => _filteredTypeRows;

    /// <summary>
    /// 2026-05-07 (iter 312, Thread D arc post-finale 2/2): hot-swap the
    /// icon resolver. Composition root (MainViewModelV2) calls this when
    /// the operator changes Settings.IconsRoot so Spawning tab rows
    /// re-resolve immediately — no editor restart required. Pass null to
    /// disable icons (clears all IconPaths to null on next refresh).
    /// </summary>
    public void SetIconResolver(UnitIconResolver? iconResolver)
    {
        _iconResolver = iconResolver;
        // Rebuild rows so existing list re-resolves IconPaths via the new
        // resolver. Without this the operator wouldn't see the change until
        // the next filter/search/domain edit triggered a natural refresh.
        RefreshFilteredTypes();
    }

    /// <summary>
    /// Faction filter dropdown options. "All" is always first; the other
    /// entries are auto-derived from the available-types list (the
    /// faction prefix common to vanilla + every mod).
    /// </summary>
    public ObservableCollection<string> AvailableFactions => _availableFactions;

    /// <summary>
    /// Domain filter dropdown options. Fixed list: All / Space / Ground /
    /// Unknown. Driven by <see cref="SpawningTabState.ClassifyDomain"/>.
    /// </summary>
    public ObservableCollection<string> AvailableDomains => _availableDomains;

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand SpawnCommand { get; }
    public ICommand SpawnUnitLuaCommand { get; }

    // 2026-05-05 (iter 195) — iter-185 spawn variant commands.
    public ICommand ReinforceUnitLuaCommand { get; }
    public ICommand SpawnFromReinforcementPoolLuaCommand { get; }
    public ICommand CreateGenericObjectLuaCommand { get; }

    // 2026-05-05 (iter 203) — discovery helpers commands.
    public ICommand FindObjectTypeLuaCommand { get; }
    public ICommand FindPlanetLuaCommand { get; }
    public ICommand FindFirstObjectLuaCommand { get; }
    public ICommand FindNearestLuaCommand { get; }
    // 2026-05-05 (iter 206) — iter-179 Find_All_Objects_Of_Type command.
    public ICommand FindAllObjectsOfTypeLuaCommand { get; }

    // 2026-05-05 (iter 203): discovery-helpers shared input fields. Kept
    // distinct from the spawn-variant fields above so operators can run a
    // discovery query (e.g. find an enemy AT-AT) without disturbing their
    // composed spawn args.
    private string _findTypeNameLuaExpr = "\"Empire_AT_AT\"";
    private string _findPlanetNameLuaExpr = "\"YAVIN\"";
    private string _findNearestPositionLuaExpr = "Vector(0, 0, 0)";
    private string _findNearestPlayerLuaExpr = "Find_Player(\"REBEL\")";

    public string FindTypeNameLuaExpr
    {
        get => _findTypeNameLuaExpr;
        set => SetField(ref _findTypeNameLuaExpr, value ?? string.Empty);
    }

    public string FindPlanetNameLuaExpr
    {
        get => _findPlanetNameLuaExpr;
        set => SetField(ref _findPlanetNameLuaExpr, value ?? string.Empty);
    }

    public string FindNearestPositionLuaExpr
    {
        get => _findNearestPositionLuaExpr;
        set => SetField(ref _findNearestPositionLuaExpr, value ?? string.Empty);
    }

    public string FindNearestPlayerLuaExpr
    {
        get => _findNearestPlayerLuaExpr;
        set => SetField(ref _findNearestPlayerLuaExpr, value ?? string.Empty);
    }

    /// <summary>
    /// 2026-04-29 (iter 119): Lua expressions for the iter 109
    /// SWFOC_SpawnUnitLua wire. Operator types three expressions:
    /// <c>Find_Player(...)</c>, <c>Find_Object_Type(...)</c>,
    /// <c>Create_Position(x, y, z)</c>.
    /// </summary>
    private string _spawnPlayerLuaExpr = string.Empty;
    public string SpawnPlayerLuaExpr
    {
        get => _spawnPlayerLuaExpr;
        set => SetField(ref _spawnPlayerLuaExpr, value ?? string.Empty);
    }

    private string _spawnTypeLuaExpr = string.Empty;
    public string SpawnTypeLuaExpr
    {
        get => _spawnTypeLuaExpr;
        set => SetField(ref _spawnTypeLuaExpr, value ?? string.Empty);
    }

    private string _spawnPositionLuaExpr = string.Empty;
    public string SpawnPositionLuaExpr
    {
        get => _spawnPositionLuaExpr;
        set => SetField(ref _spawnPositionLuaExpr, value ?? string.Empty);
    }

    private async Task SpawnUnitLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_spawnPlayerLuaExpr) ||
            string.IsNullOrWhiteSpace(_spawnTypeLuaExpr) ||
            string.IsNullOrWhiteSpace(_spawnPositionLuaExpr))
        {
            LastStatus = "Spawn (Lua) skipped: all three Lua expressions are required.";
            return;
        }
        var dispatcher = new V2UnitMutationDispatcher(_bridge);
        var round = await dispatcher.SpawnUnitLuaAsync(
            _spawnPlayerLuaExpr, _spawnTypeLuaExpr, _spawnPositionLuaExpr,
            CancellationToken.None).ConfigureAwait(true);
        LastStatus = round.Succeeded
            ? $"Spawn (Lua) → bridge OK: {round.Response}"
            : $"Spawn (Lua) failed: {round.ErrorMessage}";
    }

    // 2026-05-05 (iter 195) — spawn variant async handlers. Reinforce + pool
    // share the (player, type, position) shape with iter-119 SpawnUnitLua.
    // CreateGenericObject has DIFFERENT param order (type, position, player)
    // — the dispatcher signature mirrors the engine API, NOT the operator-
    // friendly player-first order. Catalog rationale + iter-185 pin tests
    // already document this; iter 195 surfaces the gotcha in the button label.
    private async Task ReinforceUnitLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_spawnPlayerLuaExpr) ||
            string.IsNullOrWhiteSpace(_spawnTypeLuaExpr) ||
            string.IsNullOrWhiteSpace(_spawnPositionLuaExpr))
        {
            LastStatus = "Reinforce (Lua) skipped: all three Lua expressions are required.";
            return;
        }
        var dispatcher = new V2UnitMutationDispatcher(_bridge);
        var round = await dispatcher.ReinforceUnitLuaAsync(
            _spawnPlayerLuaExpr, _spawnTypeLuaExpr, _spawnPositionLuaExpr,
            CancellationToken.None).ConfigureAwait(true);
        LastStatus = round.Succeeded
            ? $"Reinforce (Lua) → bridge OK: {round.Response}"
            : $"Reinforce (Lua) failed: {round.ErrorMessage}";
    }

    private async Task SpawnFromReinforcementPoolLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_spawnPlayerLuaExpr) ||
            string.IsNullOrWhiteSpace(_spawnTypeLuaExpr) ||
            string.IsNullOrWhiteSpace(_spawnPositionLuaExpr))
        {
            LastStatus = "SpawnFromPool (Lua) skipped: all three Lua expressions are required.";
            return;
        }
        var dispatcher = new V2UnitMutationDispatcher(_bridge);
        var round = await dispatcher.SpawnFromReinforcementPoolLuaAsync(
            _spawnPlayerLuaExpr, _spawnTypeLuaExpr, _spawnPositionLuaExpr,
            CancellationToken.None).ConfigureAwait(true);
        LastStatus = round.Succeeded
            ? $"SpawnFromPool (Lua) → bridge OK: {round.Response}"
            : $"SpawnFromPool (Lua) failed: {round.ErrorMessage}";
    }

    private async Task CreateGenericObjectLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_spawnPlayerLuaExpr) ||
            string.IsNullOrWhiteSpace(_spawnTypeLuaExpr) ||
            string.IsNullOrWhiteSpace(_spawnPositionLuaExpr))
        {
            LastStatus = "CreateGeneric (Lua) skipped: all three Lua expressions are required.";
            return;
        }
        var dispatcher = new V2UnitMutationDispatcher(_bridge);
        // Param order: (type, position, player) — DIFFERENT from Spawn_Unit/Reinforce.
        var round = await dispatcher.CreateGenericObjectLuaAsync(
            _spawnTypeLuaExpr, _spawnPositionLuaExpr, _spawnPlayerLuaExpr,
            CancellationToken.None).ConfigureAwait(true);
        LastStatus = round.Succeeded
            ? $"CreateGeneric (Lua) → bridge OK: {round.Response}"
            : $"CreateGeneric (Lua) failed: {round.ErrorMessage}";
    }

    // 2026-05-05 (iter 203) — Discovery helpers handlers. Each returns an
    // engine handle (or "nil") that the operator can read in LastStatus
    // and then paste into the spawn fields above. Workflow:
    //   1. Type "Empire_AT_AT" in Find type-name field, click Find first.
    //   2. LastStatus shows "FindFirst → bridge OK: <handle>".
    //   3. Operator copies the handle into a Lua expression for the
    //      Selected unit / spawn position fields elsewhere.
    private async Task FindObjectTypeLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_findTypeNameLuaExpr))
        {
            LastStatus = "Find object type skipped: type-name Lua expression is required.";
            return;
        }
        var dispatcher = new V2UnitMutationDispatcher(_bridge);
        var round = await dispatcher.FindObjectTypeLuaAsync(
            _findTypeNameLuaExpr, CancellationToken.None).ConfigureAwait(true);
        LastStatus = round.Succeeded
            ? $"FindObjectType('{_findTypeNameLuaExpr}') → bridge OK: {round.Response}"
            : $"FindObjectType('{_findTypeNameLuaExpr}') failed: {round.ErrorMessage}";
    }

    private async Task FindPlanetLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_findPlanetNameLuaExpr))
        {
            LastStatus = "Find planet skipped: planet-name Lua expression is required.";
            return;
        }
        var dispatcher = new V2UnitMutationDispatcher(_bridge);
        var round = await dispatcher.FindPlanetLuaAsync(
            _findPlanetNameLuaExpr, CancellationToken.None).ConfigureAwait(true);
        LastStatus = round.Succeeded
            ? $"FindPlanet('{_findPlanetNameLuaExpr}') → bridge OK: {round.Response}"
            : $"FindPlanet('{_findPlanetNameLuaExpr}') failed: {round.ErrorMessage}";
    }

    private async Task FindFirstObjectLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_findTypeNameLuaExpr))
        {
            LastStatus = "Find first object skipped: type-name Lua expression is required.";
            return;
        }
        var dispatcher = new V2UnitMutationDispatcher(_bridge);
        var round = await dispatcher.FindFirstObjectLuaAsync(
            _findTypeNameLuaExpr, CancellationToken.None).ConfigureAwait(true);
        LastStatus = round.Succeeded
            ? $"FindFirstObject('{_findTypeNameLuaExpr}') → bridge OK: {round.Response}"
            : $"FindFirstObject('{_findTypeNameLuaExpr}') failed: {round.ErrorMessage}";
    }

    private async Task FindNearestLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_findTypeNameLuaExpr) ||
            string.IsNullOrWhiteSpace(_findNearestPositionLuaExpr) ||
            string.IsNullOrWhiteSpace(_findNearestPlayerLuaExpr))
        {
            LastStatus = "Find nearest skipped: type, position, and player Lua expressions are all required.";
            return;
        }
        var dispatcher = new V2UnitMutationDispatcher(_bridge);
        var round = await dispatcher.FindNearestLuaAsync(
            _findTypeNameLuaExpr, _findNearestPositionLuaExpr, _findNearestPlayerLuaExpr,
            CancellationToken.None).ConfigureAwait(true);
        LastStatus = round.Succeeded
            ? $"FindNearest({_findTypeNameLuaExpr}, {_findNearestPositionLuaExpr}, {_findNearestPlayerLuaExpr}) → bridge OK: {round.Response}"
            : $"FindNearest failed: {round.ErrorMessage}";
    }

    // 2026-05-05 (iter 206) — iter-179 Find_All_Objects_Of_Type. Returns
    // engine table-handle of every instance matching the type. Composes
    // with iter-177 FindFirst (single) and iter-186 FindNearest (closest)
    // to give the operator the full "first / nearest / all" trio.
    private async Task FindAllObjectsOfTypeLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_findTypeNameLuaExpr))
        {
            LastStatus = "Find all objects skipped: type-name Lua expression is required.";
            return;
        }
        var dispatcher = new V2UnitMutationDispatcher(_bridge);
        var round = await dispatcher.FindAllObjectsOfTypeLuaAsync(
            _findTypeNameLuaExpr, CancellationToken.None).ConfigureAwait(true);
        LastStatus = round.Succeeded
            ? $"FindAllObjectsOfType('{_findTypeNameLuaExpr}') → bridge OK: {round.Response}"
            : $"FindAllObjectsOfType('{_findTypeNameLuaExpr}') failed: {round.ErrorMessage}";
    }

    /// <summary>
    /// 2026-04-27: live-filter the catalog against the running game.
    /// Sends every currently-known type id to <c>SWFOC_BatchTypeExists</c>,
    /// reads back a per-name "1"/"0" flag, and replaces the available-types
    /// list with the subset the engine confirmed. This is what isolates a
    /// vanilla session from a mod session — the bridge probe asks the
    /// engine's GameObjectTypeManager directly, so we never see types the
    /// running game can't actually spawn.
    /// </summary>
    public ICommand RefreshFromLiveGameCommand { get; }

    private async Task RefreshFromLiveGameAsync()
    {
        var seed = _state.AvailableTypes;
        if (seed.Count == 0)
        {
            LastStatus = "Refresh skipped: catalog is empty (configure CatalogSources first).";
            return;
        }

        // The bridge probe accepts up to 512 names per call (kMaxNames in
        // lua_bridge.cpp). Batch by 256 to leave headroom for the pipe
        // protocol envelope and to bound per-call latency.
        const int BatchSize = 256;
        var keep = new List<string>();
        var batchIndex = 0;
        for (var i = 0; i < seed.Count; i += BatchSize, batchIndex++)
        {
            var slice = seed.Skip(i).Take(BatchSize).ToList();
            var payload = string.Join("|", slice);
            var lua = "return SWFOC_BatchTypeExists(\"" + EscapeLuaString(payload) + "\")";

            var round = await _bridge.SendRawAsync(lua, CancellationToken.None)
                .ConfigureAwait(true);
            if (!round.Succeeded)
            {
                LastStatus = $"Refresh batch {batchIndex} failed: {round.ErrorMessage}";
                return;
            }
            var flags = round.Response ?? string.Empty;
            if (flags.StartsWith("ERR:", StringComparison.Ordinal))
            {
                LastStatus = $"Refresh batch {batchIndex} bridge error: {flags}";
                return;
            }
            var flagParts = flags.Split('|');
            for (var k = 0; k < slice.Count && k < flagParts.Length; k++)
            {
                if (flagParts[k].Trim() == "1")
                {
                    keep.Add(slice[k]);
                }
            }
        }

        _state.SetAvailableTypes(keep);
        RebuildFactionFilterOptions();
        RefreshFilteredTypes();
        LastStatus = $"Refreshed from live game: kept {keep.Count} of {seed.Count} types " +
                     $"(those the running game's GameObjectTypeManager confirmed).";
    }

    /// <summary>
    /// Lua 5.0 string-literal escape — only needs to handle backslash and
    /// double-quote since type ids are upper-case ASCII identifiers in
    /// vanilla and every mod we know of. Mod authors who add weird names
    /// can still send through, the escape is just defence-in-depth.
    /// </summary>
    /// <summary>
    /// 2026-04-27 (iter 15): promoted from private to internal so tests
    /// can pin the escape rules without going through the bridge round
    /// trip. Backslash + double-quote are the only Lua 5.0 string-literal
    /// escapes we need today.
    /// </summary>
    internal static string EscapeLuaString(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
         .Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>
    /// Replace the available-types catalogue. Called once at startup from the
    /// mod's GameObjects.xml or a verified-facts cache. Also re-derives the
    /// faction-filter dropdown options from the type names.
    /// </summary>
    public void SetAvailableTypes(IEnumerable<string> types)
    {
        _state.SetAvailableTypes(types);
        RebuildFactionFilterOptions();
        RefreshFilteredTypes();
    }

    /// <summary>
    /// 2026-04-27: derive the faction-filter dropdown options from the
    /// available type names. We extract the leading underscore-delimited
    /// prefix that appears as the faction in vanilla + mod naming
    /// conventions ("EMPIRE_INFANTRY_STORMTROOPER" -> "EMPIRE", "AOTR_
    /// REBEL_*" -> "AOTR_REBEL", etc.). The first underscore typically
    /// separates faction from category; if the second token also looks
    /// like a faction qualifier ("AOTR_REBEL") we keep both.
    /// </summary>
    private void RebuildFactionFilterOptions()
    {
        // Always-present "All" sentinel; reset the rest from live data.
        var prefixes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in _state.AvailableTypes)
        {
            var prefix = ExtractFactionPrefix(t);
            if (!string.IsNullOrEmpty(prefix))
            {
                prefixes.Add(prefix);
            }
        }

        _availableFactions.Clear();
        _availableFactions.Add(AllFilterValue);
        foreach (var p in prefixes)
        {
            _availableFactions.Add(p);
        }

        // If the previously-selected filter is no longer in the new list,
        // reset to "All" so the dropdown doesn't show a stale value.
        if (!_availableFactions.Contains(_selectedFactionFilter, StringComparer.OrdinalIgnoreCase))
        {
            SelectedFactionFilter = AllFilterValue;
        }
    }

    /// <summary>
    /// Extract the faction prefix from a type id. Fully dynamic — no
    /// hardcoded faction whitelist, so AOTR / ROE / ROTR / Thrawn's
    /// Revenge / Republic at War / etc. all work without code changes.
    /// We take the FIRST underscore-delimited token as the faction
    /// (Petroglyph naming convention, also followed by every major mod).
    /// Modded names like "AOTR_REBEL_INFANTRY" group under "AOTR" — the
    /// operator can refine to "AOTR rebels only" by combining the
    /// faction filter with the search box (e.g. faction=AOTR + search=REBEL).
    /// </summary>
    /// <summary>
    /// 2026-04-27 (iter 10): promoted from private to internal so the
    /// test suite can pin the heuristic without instantiating the full
    /// ViewModel + bridge adapter graph.
    /// </summary>
    internal static string ExtractFactionPrefix(string typeId)
    {
        if (string.IsNullOrEmpty(typeId)) return string.Empty;
        var underscore = typeId.IndexOf('_');
        if (underscore <= 0) return typeId.ToUpperInvariant();
        return typeId.Substring(0, underscore).ToUpperInvariant();
    }

    private void RefreshFilteredTypes()
    {
        _filteredTypes.Clear();
        _filteredTypeRows.Clear();
        foreach (var t in _state.FilteredTypes())
        {
            _filteredTypes.Add(t);
            // iter-308: resolver returns null when not wired OR DDS not
            // extracted/cached yet. Null IconPath hides the Image control
            // gracefully — operator sees the type name with no icon, not
            // a broken-image placeholder.
            var iconPath = _iconResolver?.Resolve(t);
            _filteredTypeRows.Add(new UnitTypeRow(t, iconPath));
        }
    }

    private bool CanSpawnSelectedType()
        => !string.IsNullOrWhiteSpace(_selectedTypeId)
           && _factionSlot >= 0
           && _count > 0
           && ResolvePlayerLuaExprForSlot(_factionSlot) is not null;

    private async Task SpawnCore()
    {
        if (string.IsNullOrWhiteSpace(_selectedTypeId))
        {
            LastStatus = "Spawn skipped: select a unit type first.";
            return;
        }

        if (_factionSlot < 0)
        {
            LastStatus = "Spawn skipped: select a faction slot first.";
            return;
        }

        if (_count <= 0)
        {
            LastStatus = "Spawn skipped: count must be greater than zero.";
            return;
        }

        var playerLuaExpr = ResolvePlayerLuaExprForSlot(_factionSlot);
        if (playerLuaExpr is null)
        {
            LastStatus = $"Spawn skipped: slot {_factionSlot} cannot be mapped to a faction name. "
                + "Use the explicit Spawn (Lua) fields for custom mod slots.";
            return;
        }

        var typeLuaExpr = BuildFindObjectTypeLuaExpr(_selectedTypeId);
        var positionLuaExpr = BuildCreatePositionLuaExpr(_posX, _posY, _posZ);
        var dispatcher = new V2UnitMutationDispatcher(_bridge);

        BridgeRoundTripResult lastRound = default;
        for (var i = 0; i < _count; i++)
        {
            lastRound = await dispatcher.SpawnUnitLuaAsync(
                playerLuaExpr, typeLuaExpr, positionLuaExpr, CancellationToken.None)
                .ConfigureAwait(true);
            if (!lastRound.Succeeded)
            {
                LastStatus = $"Spawn failed after {i} of {_count}: {lastRound.ErrorMessage}";
                return;
            }
        }

        LastStatus = $"Spawn -> bridge OK: spawned {_count}x {_selectedTypeId} for slot {_factionSlot} "
            + $"at ({_posX.ToString("0.0", CultureInfo.InvariantCulture)},"
            + $"{_posY.ToString("0.0", CultureInfo.InvariantCulture)},"
            + $"{_posZ.ToString("0.0", CultureInfo.InvariantCulture)}). "
            + $"Last response: {lastRound.Response}";
    }

    internal static string? ResolvePlayerLuaExprForSlot(int factionSlot) => factionSlot switch
    {
        0 => "Find_Player(\"REBEL\")",
        1 => "Find_Player(\"EMPIRE\")",
        2 => "Find_Player(\"UNDERWORLD\")",
        7 => "Find_Player(\"NEUTRAL\")",
        _ => null,
    };

    internal static string BuildFindObjectTypeLuaExpr(string typeId)
        => $"Find_Object_Type(\"{EscapeLuaString(typeId.Trim())}\")";

    internal static string BuildCreatePositionLuaExpr(float x, float y, float z)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"Create_Position({x}, {y}, {z})");

    private void ApplyFeedback(UxFeedback fb)
    {
        LastStatus = string.Format(CultureInfo.InvariantCulture,
            "{0}: {1} — {2}", fb.Severity, fb.Title, fb.Message);
    }

    private void HandleError(Exception ex)
    {
        LastStatus = $"command failed: {ex.Message}";
    }
}
