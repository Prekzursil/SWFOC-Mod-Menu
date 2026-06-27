using System.Collections.ObjectModel;
using System.Globalization;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.V2.ViewModels;

// ============================================================================
// Tab 2 — Player State
//
// Every button routes through ILuaBridgeExecutor via either:
//   (a) a feature service (IEconomyService / IHeroRespawnService / IFactionSwitchService)
//   (b) the V2BridgeAdapter directly, for helpers without a service wrapper.
//
// All results (success + error) are appended to the Output collection so
// the operator sees exactly what the bridge said.
// ============================================================================

/// <summary>
/// One row in the player-slot dropdown. <see cref="DisplayLabel"/> is what
/// the operator sees in the ComboBox ("Slot 0 — REBEL", "Slot 6 — UNDERWORLD"
/// once the live faction map is loaded; "Slot 0" before that). The
/// <see cref="Slot"/> int is the value passed to every per-slot bridge
/// helper.
/// </summary>
public sealed class PlayerSlotEntry : ObservableBase
{
    private string _factionName = string.Empty;
    private string? _iconPath;

    public PlayerSlotEntry(int slot, string factionName = "")
    {
        Slot = slot;
        _factionName = factionName ?? string.Empty;
    }

    public int Slot { get; }

    public string FactionName
    {
        get => _factionName;
        set
        {
            if (SetField(ref _factionName, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayLabel));
            }
        }
    }

    /// <summary>"Slot {N}" until refreshed; "Slot {N} — {Faction}" once a live read populates the name.</summary>
    public string DisplayLabel => string.IsNullOrEmpty(_factionName)
        ? $"Slot {Slot}"
        : $"Slot {Slot} — {_factionName}";

    /// <summary>
    /// 2026-05-07 (iter 319, third UI consumer of iter-313/314/315 resolver
    /// extensions; extends the iter-317/iter-318 parallel-collection pattern
    /// to fit the existing ComboBox shape via in-place INPC update).
    ///
    /// Resolved via iter-314 UnitIconResolver.ResolveFactionEmblem (default
    /// size 48). Null when no resolver is wired OR the operator hasn't
    /// extracted the faction emblem DDS yet — null binding hides the
    /// ComboBox.ItemTemplate Image control gracefully (no broken-image
    /// placeholder).
    /// </summary>
    public string? IconPath
    {
        get => _iconPath;
        set => SetField(ref _iconPath, value);
    }
}

public sealed class PlayerStateTabViewModel : ObservableBase
{
    private readonly V2BridgeAdapter _bridge;
    private readonly V2Settings _settings;
    private readonly IEconomyService _economy;
    private readonly IHeroRespawnService _heroRespawn;
    private readonly IFactionSwitchService _factionSwitch;
    private readonly V2UnitMutationDispatcher _unitMutator;
    private readonly V2FactionRegistry _factionRegistry;

    private readonly ObservableCollection<string> _output = new();
    private PlayerSlotEntry? _selectedSlotEntry;
    private string _creditsInput = "10000000";
    private string _techInput = "5";
    private string _respawnSecondsInput = "10";
    private string _selectedFaction = "EMPIRE";

    // 2026-05-07 (iter 319, third UI consumer of iter-313/314/315 resolver
    // extensions): mutable iconResolver field. Composition root
    // (MainViewModelV2) hot-swaps via SetIconResolver when operator changes
    // Settings.IconsRoot. Mirrors iter-312 + iter-317 + iter-318 pattern but
    // updates each PlayerSlotEntry in-place via INPC instead of rebuilding
    // a parallel collection (PlayerSlotEntry is an INPC class, not a record).
    private SwfocTrainer.Core.Assets.UnitIconResolver? _iconResolver;

    public PlayerStateTabViewModel(
        V2BridgeAdapter bridge,
        V2Settings settings,
        IEconomyService economy,
        IHeroRespawnService heroRespawn,
        IFactionSwitchService factionSwitch,
        V2UnitMutationDispatcher unitMutator,
        V2FactionRegistry factionRegistry,
        SwfocTrainer.Core.Assets.UnitIconResolver? iconResolver = null)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(heroRespawn);
        ArgumentNullException.ThrowIfNull(factionSwitch);
        ArgumentNullException.ThrowIfNull(unitMutator);
        ArgumentNullException.ThrowIfNull(factionRegistry);

        _bridge = bridge;
        _settings = settings;
        _economy = economy;
        _heroRespawn = heroRespawn;
        _factionSwitch = factionSwitch;
        _unitMutator = unitMutator;
        _factionRegistry = factionRegistry;
        // iter-319: optional resolver — null is the no-emblem default. Existing
        // callers that pass only the 7 deps keep working unchanged via the
        // optional-default-null ctor extension pattern (iter-301/308/311
        // codified). 8th constructor parameter, fully qualified namespace
        // to avoid adding a `using` for a single 1-line touch.
        _iconResolver = iconResolver;

        // 2026-04-25: was 0..4. Operator reports their UNDERWORLD lives at
        // slot 6 in their current map; some skirmishes go up to 8 players.
        // Pre-fill 0..7 so the ComboBox covers every realistic case; the
        // RefreshSlotMap command labels each entry with the live faction
        // name read from SWFOC_GetAllPlayers.
        Slots = new ObservableCollection<PlayerSlotEntry>();
        for (var i = 0; i <= 7; i++) Slots.Add(new PlayerSlotEntry(i));
        _selectedSlotEntry = Slots[0];
        // iter-319: resolve emblems for any pre-seeded factions (none at default
        // construction since FactionName is empty until the bridge fills them).
        // Idempotent.
        ResolveEmblemsForAllSlots();

        // 2026-04-27 (final): faction list lives on V2FactionRegistry —
        // the same singleton that UnitControl / WorldState / Galactic /
        // Economy bind to. We expose it through `Factions` here so the
        // existing XAML binding still resolves; PlayerState owns the
        // merge logic via RefreshSlotMapAsync, but every other tab sees
        // updates the moment they happen.

        GetCreditsCommand = Async(GetCreditsAsync);
        SetCreditsCommand = Async(SetCreditsAsync);
        UncapCreditsCommand = Async(UncapCreditsAsync);
        DrainEnemyCreditsCommand = Async(DrainEnemyCreditsAsync);
        GetTechCommand = Async(GetTechAsync);
        SetTechCommand = Async(SetTechAsync);
        SetRespawnCommand = Async(SetRespawnAsync);
        InstantRespawnCommand = Async(HeroInstantRespawnAsync);
        SwitchFactionCommand = Async(SwitchFactionAsync);
        SwitchToSlotCommand = Async(SwitchToSlotAsync);
        DetectLocalPlayerCommand = Async(DetectLocalPlayerAsync);
        RefreshSlotMapCommand = Async(RefreshSlotMapAsync);
        NullAiBrainCommand = Async(NullAiBrainAsync);
        AttachAiBrainCommand = Async(AttachAiBrainAsync);

        // 2026-05-05 (iter 189): read-side native UX for iter-169 player wires.
        ReadPlayerCreditsLuaCommand = Async(GetPlayerCreditsLuaAsync);
        ReadPlayerTechLevelLuaCommand = Async(GetPlayerTechLevelLuaAsync);
        ReadPlayerFactionLuaCommand = Async(GetPlayerFactionLuaAsync);

        ReadPlayerCreditsLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Read player credits (Lua)", "SWFOC_GetCreditsLua");
        ReadPlayerTechLevelLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Read player tech level (Lua)", "SWFOC_GetTechLevelLua");
        ReadPlayerFactionLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Read player faction (Lua)", "SWFOC_GetFactionLua");

        // 2026-05-05 (iter 199): read-side extension for iter-170 GetName + iter-179 Is_Enemy/Is_Ally.
        // GetName is no-arg; Is_Enemy/Is_Ally take other-player Lua expression.
        ReadPlayerNameLuaCommand = Async(GetPlayerNameLuaAsync);
        IsEnemyLuaCommand = Async(IsEnemyLuaAsync);
        IsAllyLuaCommand = Async(IsAllyLuaAsync);
        ReadPlayerNameLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Read player name (Lua)", "SWFOC_GetNameLua");
        IsEnemyLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Is enemy of? (Lua)", "SWFOC_IsEnemyLua");
        IsAllyLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Is ally of? (Lua)", "SWFOC_IsAllyLua");

        // 2026-05-06 (iter 209): diplomacy write-side batch (iter-161 wires).
        // Lock_Tech complements iter-155 Unlock_Tech (now exposed via the
        // PlayerUnlockTech preset). Make_Ally/Make_Enemy share OtherPlayerLuaExpr
        // with iter-199 Is_Enemy/Is_Ally — operator types 'Find_Player("EMPIRE")'
        // once and can ask "are we enemies?" + "ally with them" interchangeably.
        // Lock_Tech needs its own field (TechTypeLuaExpr) for the tech-name arg.
        LockTechLuaCommand = Async(LockTechLuaAsync);
        MakeAllyLuaCommand = Async(MakeAllyLuaAsync);
        MakeEnemyLuaCommand = Async(MakeEnemyLuaAsync);
        LockTechLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Lock tech (Lua)", "SWFOC_LockTechLua");
        MakeAllyLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Make ally (Lua)", "SWFOC_MakeAllyLua");
        MakeEnemyLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Make enemy (Lua)", "SWFOC_MakeEnemyLua");

        // 2026-05-06 (iter 210): PlayerWrapper Other extension batch (iter-164
        // wires). Enable_As_Actor (no-arg) enables AI actor mode for the
        // player. Release_Credits_For_Tactical (2-arg) releases banked credits
        // during galactic→tactical transition. Select_Object (2-arg) selects
        // a unit in the player's UI. Shares PlayerLuaExpr with iter-189/199
        // read-side + iter-209 write-side. Two new fields: ReleaseCreditsAmount
        // (numeric Lua expr for amount) + SelectObjectLuaExpr (object handle).
        EnableAsActorLuaCommand = Async(EnableAsActorLuaAsync);
        ReleaseCreditsForTacticalLuaCommand = Async(ReleaseCreditsForTacticalLuaAsync);
        SelectObjectLuaCommand = Async(SelectObjectLuaAsync);
        EnableAsActorLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Enable as actor (Lua)", "SWFOC_EnableAsActorLua");
        ReleaseCreditsForTacticalLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Release credits for tactical (Lua)", "SWFOC_ReleaseCreditsForTacticalLua");
        SelectObjectLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Select object (Lua)", "SWFOC_SelectObjectLua");

        // 2026-05-06 (iter 217): PlayerState final extension batch (iter-160
        // Disable_Orbital_Bombardment + iter-182 GLOBAL Make_Ally/Make_Enemy).
        // Disable_Orbital_Bombardment is a player-method bool toggle surfaced as
        // an on/off pair (iter-204 hardcoded-bool lineage now 7 iters deep).
        // GlobalMakeAlly/GlobalMakeEnemy are alternative diplomacy forms — both
        // forms work in the engine; operator preference dictates which. Same
        // mode-change-reset caveat as iter-209 obj-receiver forms. All three
        // wires reuse PlayerLuaExpr (player1) and OtherPlayerLuaExpr (player2)
        // already used by iter-189/199/209/210 — zero new fields, pure reuse.
        DisableOrbitalBombardmentOnLuaCommand = Async(() => DisableOrbitalBombardmentLuaAsync("1"));
        DisableOrbitalBombardmentOffLuaCommand = Async(() => DisableOrbitalBombardmentLuaAsync("0"));
        GlobalMakeAllyLuaCommand = Async(GlobalMakeAllyLuaAsync);
        GlobalMakeEnemyLuaCommand = Async(GlobalMakeEnemyLuaAsync);
        DisableOrbitalBombardmentOnLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Disable orbital bombardment: on (Lua)", "SWFOC_DisableOrbitalBombardmentLua");
        DisableOrbitalBombardmentOffLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Disable orbital bombardment: off (Lua)", "SWFOC_DisableOrbitalBombardmentLua");
        GlobalMakeAllyLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Make ally (GLOBAL form, Lua)", "SWFOC_GlobalMakeAllyLua");
        GlobalMakeEnemyLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Make enemy (GLOBAL form, Lua)", "SWFOC_GlobalMakeEnemyLua");

        // 2026-04-27 (iter 60): per-button capability metadata. PlayerState
        // is uniformly LIVE — all economy + slot + AI-brain + global hero
        // respawn actions route through engine-verified primitives.
        GetCredits = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Get credits", "SWFOC_GetCreditsForSlot");
        SetCredits = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set credits", "SWFOC_SetCreditsForSlot");
        UncapCredits = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Uncap credits", "SWFOC_UncapCredits");
        DrainEnemyCredits = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Drain enemy credits", "SWFOC_DrainEnemyCredits");
        GetTech = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Get tech level", "SWFOC_GetTechForSlot");
        SetTech = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set tech level", "SWFOC_SetTechForSlot");
        SetRespawn = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set hero respawn", "SWFOC_SetHeroRespawn");
        InstantRespawn = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Hero instant respawn", "SWFOC_HeroInstantRespawn");
        SwitchToSlot = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Switch to slot", "SWFOC_SetHumanPlayer_v3");
        DetectLocalPlayer = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Detect local player", "SWFOC_GetLocalPlayer");
        RefreshSlotMap = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Refresh slot map", "SWFOC_GetAllPlayers");
        NullAiBrain = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Null AI brain", "SWFOC_NullAiBrain");
        AttachAiBrain = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Attach AI brain", "SWFOC_AttachAiBrain");
    }

    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction GetCredits { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetCredits { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction UncapCredits { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction DrainEnemyCredits { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction GetTech { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetTech { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetRespawn { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction InstantRespawn { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SwitchToSlot { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction DetectLocalPlayer { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction RefreshSlotMap { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction NullAiBrain { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction AttachAiBrain { get; }

    public IReadOnlyList<SwfocTrainer.Core.Diagnostics.CapabilityAwareAction> AllActions => new[]
    {
        GetCredits, SetCredits, UncapCredits, DrainEnemyCredits,
        GetTech, SetTech, SetRespawn, InstantRespawn,
        SwitchToSlot, DetectLocalPlayer, RefreshSlotMap,
        NullAiBrain, AttachAiBrain,
    };

    public bool HasPhase2PendingAction => AllActions.Any(a => !a.IsAllLive);

    public string Phase2PendingWarning
    {
        get
        {
            var pending = AllActions.Where(a => !a.IsAllLive).ToList();
            if (pending.Count == 0) return string.Empty;
            var parts = pending.Select(a => $"{a.Name} ({a.Badge})");
            return "Some actions on this tab are PHASE 2 PENDING; their buttons are disabled "
                + "until a live engine hook exists. Affected: "
                + string.Join("; ", parts);
        }
    }

    public ObservableCollection<PlayerSlotEntry> Slots { get; }

    public ObservableCollection<string> Factions => _factionRegistry.Factions;

    public PlayerSlotEntry? SelectedSlotEntry
    {
        get => _selectedSlotEntry;
        set
        {
            if (SetField(ref _selectedSlotEntry, value))
            {
                OnPropertyChanged(nameof(SelectedSlot));
            }
        }
    }

    /// <summary>Computed convenience for legacy callers that want just the int.</summary>
    public int SelectedSlot
    {
        get => _selectedSlotEntry?.Slot ?? 0;
        set
        {
            var entry = Slots.FirstOrDefault(s => s.Slot == value) ?? Slots[0];
            SelectedSlotEntry = entry;
        }
    }

    public string CreditsInput
    {
        get => _creditsInput;
        set => SetField(ref _creditsInput, value);
    }

    public string TechInput
    {
        get => _techInput;
        set => SetField(ref _techInput, value);
    }

    public string RespawnSecondsInput
    {
        get => _respawnSecondsInput;
        set => SetField(ref _respawnSecondsInput, value);
    }

    public string SelectedFaction
    {
        get => _selectedFaction;
        set => SetField(ref _selectedFaction, value);
    }

    public ObservableCollection<string> Output => _output;

    public AsyncRelayCommand GetCreditsCommand { get; }
    public AsyncRelayCommand SetCreditsCommand { get; }
    public AsyncRelayCommand UncapCreditsCommand { get; }
    public AsyncRelayCommand DrainEnemyCreditsCommand { get; }
    public AsyncRelayCommand GetTechCommand { get; }
    public AsyncRelayCommand SetTechCommand { get; }
    public AsyncRelayCommand SetRespawnCommand { get; }
    public AsyncRelayCommand InstantRespawnCommand { get; }

    // 2026-05-05 (iter 189): read-side native UX for iter-169 player-receiver wires.
    public AsyncRelayCommand ReadPlayerCreditsLuaCommand { get; }
    public AsyncRelayCommand ReadPlayerTechLevelLuaCommand { get; }
    public AsyncRelayCommand ReadPlayerFactionLuaCommand { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ReadPlayerCreditsLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ReadPlayerTechLevelLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ReadPlayerFactionLuaAction { get; }

    // 2026-05-05 (iter 199): read-side extension commands + capability actions.
    public AsyncRelayCommand ReadPlayerNameLuaCommand { get; }
    public AsyncRelayCommand IsEnemyLuaCommand { get; }
    public AsyncRelayCommand IsAllyLuaCommand { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ReadPlayerNameLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction IsEnemyLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction IsAllyLuaAction { get; }

    // 2026-05-06 (iter 209): diplomacy write-side commands + capability actions.
    public AsyncRelayCommand LockTechLuaCommand { get; }
    public AsyncRelayCommand MakeAllyLuaCommand { get; }
    public AsyncRelayCommand MakeEnemyLuaCommand { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction LockTechLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction MakeAllyLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction MakeEnemyLuaAction { get; }

    /// <summary>
    /// 2026-05-06 (iter 209): Lua tech-name argument for the iter-161 Lock_Tech
    /// button. Operator types e.g. <c>"Tech_X-Wing_Garrison"</c>. Default empty
    /// so the operator sees the "(no tech-name)" feedback when they click without
    /// typing.
    /// </summary>
    private string _techTypeLuaExpr = string.Empty;
    public string TechTypeLuaExpr
    {
        get => _techTypeLuaExpr;
        set => SetField(ref _techTypeLuaExpr, value ?? string.Empty);
    }

    // 2026-05-06 (iter 210): player-extension write-side commands + capability actions.
    public AsyncRelayCommand EnableAsActorLuaCommand { get; }
    public AsyncRelayCommand ReleaseCreditsForTacticalLuaCommand { get; }
    public AsyncRelayCommand SelectObjectLuaCommand { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction EnableAsActorLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ReleaseCreditsForTacticalLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SelectObjectLuaAction { get; }

    // 2026-05-06 (iter 217): PlayerState final extension commands + capability actions.
    public AsyncRelayCommand DisableOrbitalBombardmentOnLuaCommand { get; }
    public AsyncRelayCommand DisableOrbitalBombardmentOffLuaCommand { get; }
    public AsyncRelayCommand GlobalMakeAllyLuaCommand { get; }
    public AsyncRelayCommand GlobalMakeEnemyLuaCommand { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction DisableOrbitalBombardmentOnLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction DisableOrbitalBombardmentOffLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction GlobalMakeAllyLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction GlobalMakeEnemyLuaAction { get; }

    /// <summary>
    /// 2026-05-06 (iter 210): numeric amount for Release_Credits_For_Tactical.
    /// Operator types e.g. <c>"50000"</c> or <c>"100000"</c>. Default empty so
    /// the operator sees the "(no amount)" feedback when clicking without
    /// typing.
    /// </summary>
    private string _releaseCreditsAmount = string.Empty;
    public string ReleaseCreditsAmount
    {
        get => _releaseCreditsAmount;
        set => SetField(ref _releaseCreditsAmount, value ?? string.Empty);
    }

    /// <summary>
    /// 2026-05-06 (iter 210): object Lua expression for Select_Object. Operator
    /// types e.g. <c>"Find_First_Object('AT_AT')"</c> or any obj_addr handle.
    /// Default empty so the operator sees the "(no object)" feedback when
    /// clicking without typing.
    /// </summary>
    private string _selectObjectLuaExpr = string.Empty;
    public string SelectObjectLuaExpr
    {
        get => _selectObjectLuaExpr;
        set => SetField(ref _selectObjectLuaExpr, value ?? string.Empty);
    }

    /// <summary>
    /// 2026-05-05 (iter 199): second player Lua expression used by Is_Enemy/Is_Ally
    /// predicates. Operator types e.g. <c>Find_Player("EMPIRE")</c> for "is REBEL
    /// my enemy?" workflow. Default empty so the operator sees the "(no other-
    /// player)" feedback when they click without typing.
    /// </summary>
    private string _otherPlayerLuaExpr = string.Empty;
    public string OtherPlayerLuaExpr
    {
        get => _otherPlayerLuaExpr;
        set => SetField(ref _otherPlayerLuaExpr, value ?? string.Empty);
    }
    public AsyncRelayCommand SwitchFactionCommand { get; }
    public AsyncRelayCommand SwitchToSlotCommand { get; }
    public AsyncRelayCommand DetectLocalPlayerCommand { get; }
    public AsyncRelayCommand RefreshSlotMapCommand { get; }
    public AsyncRelayCommand NullAiBrainCommand { get; }
    public AsyncRelayCommand AttachAiBrainCommand { get; }

    private AsyncRelayCommand Async(Func<Task> run) =>
        new(run, onError: ex => Append($"[error] {ex.Message}", error: true));

    private async Task GetCreditsAsync()
    {
        var result = await _economy
            .GetCreditsAsync(_settings.ProfileId, SelectedSlot, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("GetCredits", result);
    }

    private async Task SetCreditsAsync()
    {
        if (!double.TryParse(_creditsInput, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
        {
            Append($"[error] Credits must be a number, got '{_creditsInput}'.", error: true);
            return;
        }

        var result = await _economy
            .SetCreditsAsync(_settings.ProfileId, SelectedSlot, amount, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("SetCredits", result);
    }

    private async Task UncapCreditsAsync()
    {
        var result = await _economy
            .UncapCreditsAsync(_settings.ProfileId, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("UncapCredits", result);
    }

    private async Task DrainEnemyCreditsAsync()
    {
        var result = await _economy
            .DrainEnemyCreditsAsync(_settings.ProfileId, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("DrainEnemyCredits", result);
    }

    private async Task GetTechAsync()
    {
        var result = await _economy
            .GetTechAsync(_settings.ProfileId, SelectedSlot, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("GetTech", result);
    }

    private async Task SetTechAsync()
    {
        if (!int.TryParse(_techInput, NumberStyles.Any, CultureInfo.InvariantCulture, out var level))
        {
            Append($"[error] Tech level must be an integer, got '{_techInput}'.", error: true);
            return;
        }

        var result = await _economy
            .SetTechAsync(_settings.ProfileId, SelectedSlot, level, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("SetTech", result);
    }

    private async Task SetRespawnAsync()
    {
        if (!double.TryParse(_respawnSecondsInput, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds))
        {
            Append($"[error] Respawn seconds must be a number, got '{_respawnSecondsInput}'.", error: true);
            return;
        }

        var result = await _heroRespawn
            .SetCustomRespawnAsync(_settings.ProfileId, seconds, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("SetCustomRespawn", result);
    }

    private async Task HeroInstantRespawnAsync()
    {
        var result = await _heroRespawn
            .SetInstantRespawnAsync(_settings.ProfileId, enable: true, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("HeroInstantRespawn", result);
    }

    private async Task SwitchFactionAsync()
    {
        var request = new FactionSwitchRequest(_selectedFaction);
        var result = await _factionSwitch
            .SwitchFactionAsync(_settings.ProfileId, request, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("SwitchFaction", result);
    }

    private async Task DetectLocalPlayerAsync()
    {
        var round = await _bridge
            .SendRawAsync(
                "local s,f = SWFOC_GetLocalPlayer() return tostring(s)..'|'..tostring(f)",
                CancellationToken.None)
            .ConfigureAwait(true);
        if (round.Succeeded)
        {
            Append($"[ok] GetLocalPlayer -> {round.Response}", error: false);
            var pipeIndex = round.Response.IndexOf('|');
            if (pipeIndex > 0 && int.TryParse(
                    round.Response.AsSpan(0, pipeIndex),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var slot))
            {
                SelectedSlot = slot;
            }
        }
        else
        {
            Append($"[err] GetLocalPlayer -> {round.ErrorMessage}", error: true);
        }
    }

    /// <summary>
    /// 2026-04-25: switch by SLOT INDEX with AI-controller swap. Uses
    /// <c>SWFOC_SetHumanPlayer_v3</c> which extends v2 by swapping the
    /// AIPlayerClass pointers at <c>PlayerObject+0x360</c> between the
    /// old and new slot. This fixes the "AI still drives the swapped-to
    /// faction" dual-control bug confirmed during the 2026-04-25 live
    /// test (matches the prediction in
    /// <c>knowledge-base/faction_switch_full_anatomy_2026-04-11.md</c>).
    ///
    /// v3 falls back to v2's exact behaviour when no AI brain is
    /// attached, so this path is safe for tactical-mode skirmishes
    /// where the operator's old slot was already controlled by them.
    /// </summary>
    private async Task SwitchToSlotAsync()
    {
        var slot = SelectedSlot;
        var lua = string.Format(CultureInfo.InvariantCulture,
            "return SWFOC_SetHumanPlayer_v3({0})", slot);
        var round = await _bridge.SendRawAsync(lua, CancellationToken.None)
            .ConfigureAwait(true);
        if (round.Succeeded)
        {
            Append($"[ok] SwitchToSlot({slot}) v3 -> {round.Response}", error: false);
        }
        else
        {
            Append($"[err] SwitchToSlot({slot}) v3 -> {round.ErrorMessage}", error: true);
        }
    }

    /// <summary>
    /// 2026-04-25: read the live faction name at every slot via
    /// <c>SWFOC_GetAllPlayers</c> and re-label the Slots dropdown so the
    /// operator sees "Slot 6 — UNDERWORLD" instead of bare integers.
    /// Wire format from the bridge: <c>"slot,faction,unit_count|..."</c>.
    /// 2026-04-27: promoted to <c>public</c> so MainViewModelV2 can fire the
    /// same probe automatically on auto-connect (instead of forcing the
    /// operator to click the Refresh button manually).
    /// </summary>
    public async Task RefreshSlotMapAsync()
    {
        var round = await _bridge.SendRawAsync(
            "return SWFOC_GetAllPlayers()", CancellationToken.None)
            .ConfigureAwait(true);
        if (!round.Succeeded)
        {
            Append($"[err] RefreshSlotMap -> {round.ErrorMessage}", error: true);
            return;
        }
        var payload = round.Response ?? string.Empty;
        var rows = payload.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var labelled = 0;
        var liveFactions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            // 2026-04-25 fix: bridge emits semicolon-separated fields per row
            // (e.g. "0;REBEL;6424.924;1;0;0;747"), not comma. The earlier
            // split-by-comma silently produced 0 labelled slots in the live
            // game; verified via direct probe of SWFOC_GetAllPlayers().
            var parts = row.Split(';');
            if (parts.Length < 2) continue;
            if (!int.TryParse(parts[0], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var slot)) continue;
            var faction = parts[1].Trim();
            var entry = Slots.FirstOrDefault(s => s.Slot == slot);
            if (entry is null)
            {
                // The live game has more slots than the static 0..7 — extend.
                entry = new PlayerSlotEntry(slot, faction);
                Slots.Add(entry);
            }
            else
            {
                entry.FactionName = faction;
            }
            // iter-319: resolve faction emblem path on every faction update.
            // Bridge-driven refresh path; mirrors what the static-init seeding
            // does at ctor time. Null result silently hides the Image control.
            entry.IconPath = _iconResolver?.ResolveFactionEmblem(entry.FactionName);
            if (!string.IsNullOrEmpty(faction))
            {
                liveFactions.Add(faction);
            }
            labelled++;
        }

        // 2026-04-27: merge live faction names into the SHARED registry
        // so PlayerState + UnitControl + WorldState + Galactic + Economy
        // all see the same dropdown values. The engine is the source of
        // truth: vanilla returns 3-4 strings, AOTR / ROE / ROTR /
        // Thrawn's Revenge each return their own modded set. Append-only
        // (handled inside MergeFactions).
        var added = _factionRegistry.MergeFactions(liveFactions);
        var summary = added > 0
            ? $"labelled {labelled} slot(s); +{added} live faction(s) merged into shared dropdown"
            : $"labelled {labelled} slot(s)";
        Append($"[ok] RefreshSlotMap -> {summary}", error: false);
    }

    private void AppendResult(string label, ActionExecutionResult result)
    {
        var prefix = result.Succeeded ? "[ok]" : "[err]";
        Append($"{prefix} {label} -> {result.Message}", error: !result.Succeeded);
    }

    private void Append(string line, bool error)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _output.Add($"{timestamp} {line}");
        while (_output.Count > 200)
        {
            _output.RemoveAt(0);
        }
        _ = error; // reserved for future color binding; currently encoded in prefix
    }

    /// <summary>
    /// 2026-04-25: send <c>SWFOC_NullAiBrain(slot)</c> for the selected
    /// slot — clears <c>PlayerObject+0x360</c> on that player so the AI
    /// scheduler stops issuing orders to it. Recovery path for "I switched
    /// factions via the old v2 helper and EMPIRE has dual control".
    /// 2026-04-27: routed through <see cref="V2UnitMutationDispatcher"/>
    /// instead of inline Lua so a bridge signature change fails visibly.
    /// </summary>
    private async Task NullAiBrainAsync()
    {
        var slot = SelectedSlot;
        var round = await _unitMutator.NullAiBrainAsync(slot, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" NullAiBrain({slot}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}",
            error: !round.Succeeded);
    }

    /// <summary>
    /// 2026-04-25: send <c>SWFOC_AttachAiBrain(slot)</c> for the selected
    /// slot — constructs a fresh <c>AIPlayerClass</c> via the engine ctor
    /// at RVA 0x4AF810 and writes it to <c>PlayerObject+0x360</c>. Useful
    /// for "my old slot is dead-seat after I switched away".
    /// 2026-04-27: routed through <see cref="V2UnitMutationDispatcher"/>.
    /// </summary>
    private async Task AttachAiBrainAsync()
    {
        var slot = SelectedSlot;
        var round = await _unitMutator.AttachAiBrainAsync(slot, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" AttachAiBrain({slot}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}",
            error: !round.Succeeded);
    }

    // 2026-05-05 (iter 189): read-side native UX for iter-169 player-receiver
    // wires. Operator types a player Lua expression (e.g. Find_Player("REBEL")
    // or Get_Local_Player()) into PlayerLuaExpr then clicks any of these to
    // read engine state. Result lands in the Output ListBox.
    private string _playerLuaExpr = "Find_Player(\"REBEL\")";
    public string PlayerLuaExpr
    {
        get => _playerLuaExpr;
        set => SetField(ref _playerLuaExpr, value ?? string.Empty);
    }

    private async Task GetPlayerCreditsLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.GetPlayerCreditsLuaAsync(
            _playerLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" GetCreditsLua({_playerLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}",
            error: !round.Succeeded);
    }

    private async Task GetPlayerTechLevelLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.GetPlayerTechLevelLuaAsync(
            _playerLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" GetTechLevelLua({_playerLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}",
            error: !round.Succeeded);
    }

    private async Task GetPlayerFactionLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.GetPlayerFactionLuaAsync(
            _playerLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" GetFactionLua({_playerLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}",
            error: !round.Succeeded);
    }

    // 2026-05-05 (iter 199): read-side extension async handlers. GetName is
    // no-arg; Is_Enemy/Is_Ally take other-player arg. Each validates non-empty
    // inputs and dispatches via V2UnitMutationDispatcher.
    private async Task GetPlayerNameLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.GetPlayerNameLuaAsync(
            _playerLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" GetNameLua({_playerLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}",
            error: !round.Succeeded);
    }

    private async Task IsEnemyLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_otherPlayerLuaExpr))
        {
            Append("[error] Type an other-player Lua expression " +
                "(e.g. Find_Player(\"EMPIRE\")) into the second field first.", error: true);
            return;
        }
        var round = await _unitMutator.IsEnemyLuaAsync(
            _playerLuaExpr, _otherPlayerLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" IsEnemyLua({_playerLuaExpr}, {_otherPlayerLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}",
            error: !round.Succeeded);
    }

    private async Task IsAllyLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_otherPlayerLuaExpr))
        {
            Append("[error] Type an other-player Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.IsAllyLuaAsync(
            _playerLuaExpr, _otherPlayerLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" IsAllyLua({_playerLuaExpr}, {_otherPlayerLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}",
            error: !round.Succeeded);
    }

    // 2026-05-06 (iter 209): diplomacy write-side handlers (iter-161 wires).
    // Lock_Tech: 2-arg (player + tech-name); shares PlayerLuaExpr with read-side
    // wires + uses TechTypeLuaExpr for the tech-name arg. Make_Ally/Make_Enemy:
    // 2-arg (player + other-player); both share OtherPlayerLuaExpr with iter-199
    // Is_Enemy/Is_Ally — operator types other-player once and can ask "are we
    // enemies?" then "make them an ally" without re-typing.
    private async Task LockTechLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_techTypeLuaExpr))
        {
            Append("[error] Type a tech-name Lua expression " +
                "(e.g. \"Tech_X-Wing_Garrison\") into the tech field first.", error: true);
            return;
        }
        var round = await _unitMutator.LockTechLuaAsync(
            _playerLuaExpr, _techTypeLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" LockTechLua({_playerLuaExpr}, {_techTypeLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}",
            error: !round.Succeeded);
    }

    private async Task MakeAllyLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_otherPlayerLuaExpr))
        {
            Append("[error] Type an other-player Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.MakeAllyLuaAsync(
            _playerLuaExpr, _otherPlayerLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" MakeAllyLua({_playerLuaExpr}, {_otherPlayerLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}" +
            " (WARNING: state RESETS on Galactic↔Tactical mode change)",
            error: !round.Succeeded);
    }

    private async Task MakeEnemyLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_otherPlayerLuaExpr))
        {
            Append("[error] Type an other-player Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.MakeEnemyLuaAsync(
            _playerLuaExpr, _otherPlayerLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" MakeEnemyLua({_playerLuaExpr}, {_otherPlayerLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}" +
            " (WARNING: state RESETS on Galactic↔Tactical mode change)",
            error: !round.Succeeded);
    }

    // 2026-05-06 (iter 210): PlayerWrapper Other extension handlers (iter-164
    // wires). Enable_As_Actor: no-arg, just needs PlayerLuaExpr.
    // Release_Credits_For_Tactical: 2-arg (player + amount), uses
    // ReleaseCreditsAmount field. Select_Object: 2-arg (player + object handle),
    // uses SelectObjectLuaExpr field.
    private async Task EnableAsActorLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.EnableAsActorLuaAsync(
            _playerLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" EnableAsActorLua({_playerLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}",
            error: !round.Succeeded);
    }

    private async Task ReleaseCreditsForTacticalLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_releaseCreditsAmount))
        {
            Append("[error] Type an amount (e.g. 50000) into the amount field first.", error: true);
            return;
        }
        var round = await _unitMutator.ReleaseCreditsForTacticalLuaAsync(
            _playerLuaExpr, _releaseCreditsAmount, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" ReleaseCreditsForTacticalLua({_playerLuaExpr}, {_releaseCreditsAmount}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}",
            error: !round.Succeeded);
    }

    private async Task SelectObjectLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_selectObjectLuaExpr))
        {
            Append("[error] Type an object Lua expression " +
                "(e.g. Find_First_Object('AT_AT')) into the object field first.", error: true);
            return;
        }
        var round = await _unitMutator.SelectObjectLuaAsync(
            _playerLuaExpr, _selectObjectLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" SelectObjectLua({_playerLuaExpr}, {_selectObjectLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}",
            error: !round.Succeeded);
    }

    // 2026-05-06 (iter 217): PlayerState final extension handlers (iter-160 +
    // iter-182 wires). Disable_Orbital_Bombardment toggles a player-method
    // flag — surfaced as on/off pair with hardcoded bool-string args ("1"/"0")
    // continuing the iter-204 lineage. GlobalMakeAlly/GlobalMakeEnemy use
    // 2-arg dispatcher with player1=PlayerLuaExpr (shared with iter-189/199/209/210)
    // and player2=OtherPlayerLuaExpr (shared with iter-199/209). Mode-change
    // reset warning preserved verbatim from iter-209's Make_*/Make_Enemy
    // handlers.
    private async Task DisableOrbitalBombardmentLuaAsync(string boolStringArg)
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.DisableOrbitalBombardmentLuaAsync(
            _playerLuaExpr, boolStringArg, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" DisableOrbitalBombardmentLua({_playerLuaExpr}, {boolStringArg}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}",
            error: !round.Succeeded);
    }

    private async Task GlobalMakeAllyLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_otherPlayerLuaExpr))
        {
            Append("[error] Type an other-player Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.GlobalMakeAllyLuaAsync(
            _playerLuaExpr, _otherPlayerLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" GlobalMakeAllyLua({_playerLuaExpr}, {_otherPlayerLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}" +
            " (WARNING: state RESETS on Galactic↔Tactical mode change)",
            error: !round.Succeeded);
    }

    private async Task GlobalMakeEnemyLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerLuaExpr))
        {
            Append("[error] Type a player Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_otherPlayerLuaExpr))
        {
            Append("[error] Type an other-player Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.GlobalMakeEnemyLuaAsync(
            _playerLuaExpr, _otherPlayerLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        Append((round.Succeeded ? "[ok]" : "[err]") +
            $" GlobalMakeEnemyLua({_playerLuaExpr}, {_otherPlayerLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}" +
            " (WARNING: state RESETS on Galactic↔Tactical mode change)",
            error: !round.Succeeded);
    }

    /// <summary>
    /// 2026-05-07 (iter 319, third UI consumer of iter-313/314/315 resolver
    /// extensions): hot-swap the icon resolver. Composition root
    /// (MainViewModelV2) calls this when operator changes Settings.IconsRoot
    /// so PlayerState slot dropdown re-resolves emblems immediately — no
    /// editor restart required. Mirrors iter-312/iter-317/iter-318
    /// SetIconResolver but updates each PlayerSlotEntry in-place via INPC
    /// (ComboBox shape, not DataGrid). Pass null to disable emblems.
    /// </summary>
    public void SetIconResolver(SwfocTrainer.Core.Assets.UnitIconResolver? iconResolver)
    {
        _iconResolver = iconResolver;
        ResolveEmblemsForAllSlots();
    }

    /// <summary>
    /// 2026-05-07 (iter 319): walk the Slots collection and refresh each
    /// entry's IconPath from the current resolver. Called from ctor + every
    /// SetIconResolver hot-swap. Defensive `Slots.ToList()` snapshot before
    /// iteration prevents the iter-317 race condition (SWFOC_GetAllPlayers
    /// auto-refresh can mutate Slots mid-enumeration).
    /// </summary>
    private void ResolveEmblemsForAllSlots()
    {
        foreach (var entry in Slots.ToList())
        {
            entry.IconPath = _iconResolver?.ResolveFactionEmblem(entry.FactionName);
        }
    }
}
