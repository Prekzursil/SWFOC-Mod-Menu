using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Assets;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Hero Lab tab) — INPC wrapper around HeroLabTabState.
/// Five hero-targeted commands (refresh / set-respawn / toggle-permadeath /
/// kill / revive / edit-stat). Permadeath is toggled per-hero through the
/// FeatureToggleCoordinator with a per-hero key so simultaneous heroes
/// don't share a single on/off state.
/// </summary>
public sealed class HeroLabTabViewModel : ObservableBase
{
    private readonly HeroLabTabState _state;
    private readonly RecordingFeedbackSink _sink;
    private readonly FeatureToggleCoordinator _toggles;
    private readonly ObservableCollection<HeroRow> _heroes = new();

    // 2026-05-07 (iter 318, second UI consumer of iter-313 ResolvePortrait):
    // parallel collection bound by the Hero Lab tab DataGrid ItemsSource.
    // Mirrors iter-317 PlanetRowWithIcon pattern verbatim. _iconResolver is
    // mutable (not readonly) so MainViewModelV2 can hot-swap it via
    // SetIconResolver when operator changes Settings.IconsRoot.
    private readonly ObservableCollection<HeroRowWithPortrait> _heroRows = new();
    private UnitIconResolver? _iconResolver;

    private long _selectedHeroAddr;
    private int _customRespawnMs = 5000;
    private string _editField = "hull";
    private float _editValue;
    private string _lastStatus = "(idle)";

    public HeroLabTabViewModel(V2BridgeAdapter bridge, UnitIconResolver? iconResolver = null)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _sink = new RecordingFeedbackSink();
        _toggles = new FeatureToggleCoordinator(_sink);
        var dispatcher = new BridgeHeroLabDispatcher(bridge);
        _state = new HeroLabTabState(dispatcher, _sink, _toggles);
        // iter-318: optional resolver — null is the no-icons default (existing
        // callers that pass only `bridge` keep working unchanged via the
        // optional-default-null ctor extension pattern, iter-301/308/311).
        _iconResolver = iconResolver;

        RefreshHeroesCommand = new AsyncRelayCommand(RefreshHeroesCore, onError: HandleError);
        SetRespawnCommand = new AsyncRelayCommand(SetRespawnCore, onError: HandleError);
        TogglePermadeathCommand = new AsyncRelayCommand(TogglePermadeathCore, () => false, HandleError);
        KillHeroCommand = new AsyncRelayCommand(KillHeroCore, onError: HandleError);
        ReviveHeroCommand = new AsyncRelayCommand(ReviveHeroCore, onError: HandleError);
        EditStatCommand = new AsyncRelayCommand(EditStatCore, onError: HandleError);
        // 2026-04-27: mass-respawn composite — walks the loaded Heroes
        // list and fires SWFOC_ReviveUnit on each addr. Roll-up status.
        ReviveAllHeroesCommand = new AsyncRelayCommand(ReviveAllHeroesCore, onError: HandleError);

        // 2026-04-28 (iter 78): respawn-time presets. Completes the
        // scalar-preset trifecta started in iter 76 (Combat) and iter 77
        // (Speed). Each preset sets CustomRespawnMs then fires the
        // existing SetRespawnCore so the operator gets identical status
        // feedback to a manual click.
        // Values:
        //   Quick    =  2500 ms (arena/practice — fast hero rotation)
        //   Normal   =  5000 ms (default value already in the VM)
        //   Slow     = 15000 ms (punishment-flavored)
        //   Glacial  = 60000 ms (near-permadeath feel without flag)
        // Respawn presets route through the global live SWFOC_SetHeroRespawn
        // helper. Per-hero respawn timers remain Phase 2 and are not used here.
        ApplyQuickRespawnCommand = new AsyncRelayCommand(() => ApplyRespawnPresetAsync(2500), onError: HandleError);
        ApplyNormalRespawnCommand = new AsyncRelayCommand(() => ApplyRespawnPresetAsync(5000), onError: HandleError);
        ApplySlowRespawnCommand = new AsyncRelayCommand(() => ApplyRespawnPresetAsync(15000), onError: HandleError);
        ApplyGlacialRespawnCommand = new AsyncRelayCommand(() => ApplyRespawnPresetAsync(60000), onError: HandleError);

        // 2026-04-27 (iter 57): per-button capability metadata. Hero Lab
        // mixes LIVE helpers with PHASE 2 PENDING roster/permadeath pieces.
        // Respawn uses the confirmed live global default helper instead of
        // the still-pending per-hero timer helper. ReviveAll uses the same
        // primitive as ReviveHero so its badge matches.
        RefreshHeroes = new CapabilityAwareAction("Refresh heroes", "SWFOC_ListHeroes");
        SetRespawn = new CapabilityAwareAction("Set global respawn timer", "SWFOC_SetHeroRespawn");
        TogglePermadeath = new CapabilityAwareAction("Toggle permadeath", "SWFOC_SetPermadeath");
        KillHero = new CapabilityAwareAction("Kill hero", "SWFOC_KillUnit");
        ReviveHero = new CapabilityAwareAction("Revive hero", "SWFOC_ReviveUnit");
        EditStat = new CapabilityAwareAction("Edit hero stat", "SWFOC_HeroStatEdit");
        ReviveAllHeroes = new CapabilityAwareAction("Revive all heroes", "SWFOC_ReviveUnit");

        // 2026-05-19: startup auto-refresh moved to MainViewModelV2.OnWindowLoadedAsync.
        // Constructors stay side-effect-free so simulator and XAML tests do not
        // race against overlapping roster refreshes.
    }

    /// <summary>
    /// 2026-04-28 (iter 78): respawn-time preset helper. Sets the bound
    /// CustomRespawnMs then fires the existing SetRespawnCore so the
    /// operator gets identical status feedback to a manual click.
    /// Operator must select a hero first; otherwise the dispatcher
    /// fires with SelectedHeroAddr=0 and the call is a no-op-flavored
    /// Lua command (caller's responsibility — same as a manual Apply).
    /// </summary>
    internal async Task ApplyRespawnPresetAsync(int respawnMs)
    {
        CustomRespawnMs = respawnMs;
        await SetRespawnCore().ConfigureAwait(true);
    }

    public CapabilityAwareAction RefreshHeroes { get; }
    public CapabilityAwareAction SetRespawn { get; }
    public CapabilityAwareAction TogglePermadeath { get; }
    public CapabilityAwareAction KillHero { get; }
    public CapabilityAwareAction ReviveHero { get; }
    public CapabilityAwareAction EditStat { get; }
    public CapabilityAwareAction ReviveAllHeroes { get; }

    public IReadOnlyList<CapabilityAwareAction> AllActions => new[]
    {
        RefreshHeroes, SetRespawn, TogglePermadeath, KillHero, ReviveHero, EditStat, ReviveAllHeroes,
    };

    public bool HasPhase2PendingAction => AllActions.Any(a => !a.IsAllLive);

    public string Phase2PendingWarning
    {
        get
        {
            var pending = AllActions.Where(a => !a.IsAllLive).ToList();
            if (pending.Count == 0) return string.Empty;
            var parts = pending.Select(a => $"{a.Name} ({a.Badge})");
            return "Some actions on this tab are PHASE 2 PENDING; mutating buttons without a live "
                + "engine hook are disabled. Affected: "
                + string.Join("; ", parts);
        }
    }

    private async Task ReviveAllHeroesCore() =>
        ApplyFeedback(await _state.ReviveAllHeroesAsync().ConfigureAwait(true));

    public ObservableCollection<HeroRow> Heroes => _heroes;

    /// <summary>
    /// 2026-05-07 (iter 318, second UI consumer of iter-313 ResolvePortrait):
    /// parallel collection bound by the Hero Lab tab DataGrid ItemsSource.
    /// Each row's IconPath is resolved via iter-307 ThumbnailCache + iter-313
    /// UnitIconResolver.ResolvePortrait (default size 64); null when no
    /// resolver is wired OR the operator hasn't extracted the hero portrait
    /// DDS yet. Mirrors iter-317 PlanetRows verbatim.
    /// </summary>
    public ObservableCollection<HeroRowWithPortrait> HeroRows => _heroRows;

    /// <summary>
    /// 2026-05-07 (iter 318): hot-swap the icon resolver. Composition root
    /// (MainViewModelV2) calls this when the operator changes
    /// Settings.IconsRoot so Hero Lab tab rows re-resolve immediately — no
    /// editor restart required. Mirrors iter-312 SpawningTabViewModel + iter-317
    /// GalacticTabViewModel SetIconResolver. Pass null to disable icons
    /// (clears all IconPaths to null on next refresh).
    /// </summary>
    public void SetIconResolver(UnitIconResolver? iconResolver)
    {
        _iconResolver = iconResolver;
        // Rebuild rows from the existing _heroes list so the operator sees
        // the change without waiting for the next bridge-driven RefreshHeroes.
        RebuildHeroRows();
    }

    public long SelectedHeroAddr
    {
        get => _selectedHeroAddr;
        set
        {
            if (SetField(ref _selectedHeroAddr, value))
            {
                _state.SelectedHeroAddr = value;
                OnPropertyChanged(nameof(IsPermadeathEnabled));
            }
        }
    }

    public int CustomRespawnMs
    {
        get => _customRespawnMs;
        set { if (SetField(ref _customRespawnMs, value)) _state.CustomRespawnMs = value; }
    }

    public string EditField
    {
        get => _editField;
        set { if (SetField(ref _editField, value ?? "hull")) _state.EditField = _editField; }
    }

    public float EditValue
    {
        get => _editValue;
        set { if (SetField(ref _editValue, value)) _state.EditValue = value; }
    }

    public IReadOnlyList<string> EditFieldOptions { get; } = new[]
    {
        "hull", "max_hull", "shield", "max_shield", "speed", "max_speed",
        "attack_power", "respawn_ms", "invuln_flag", "prevent_death",
    };

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge(
        "SWFOC_ListHeroes", "SWFOC_KillUnit", "SWFOC_ReviveUnit",
        "SWFOC_SetHeroRespawn", "SWFOC_SetPermadeath", "SWFOC_HeroStatEdit");

    public bool IsPermadeathEnabled =>
        _selectedHeroAddr != 0
        && _toggles.IsEnabled($"permadeath_0x{_selectedHeroAddr:X}");

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand RefreshHeroesCommand { get; }
    public ICommand SetRespawnCommand { get; }
    public ICommand TogglePermadeathCommand { get; }
    public ICommand KillHeroCommand { get; }
    public ICommand ReviveHeroCommand { get; }
    public ICommand EditStatCommand { get; }
    /// <summary>2026-04-27: revive every hero in the DataGrid in one click.</summary>
    public ICommand ReviveAllHeroesCommand { get; }

    /// <summary>2026-04-28 (iter 78): one-click "Quick" respawn (2500ms) — arena/practice fast hero rotation.</summary>
    public ICommand ApplyQuickRespawnCommand { get; }
    /// <summary>2026-04-28 (iter 78): one-click "Normal" respawn (5000ms) — canonical default.</summary>
    public ICommand ApplyNormalRespawnCommand { get; }
    /// <summary>2026-04-28 (iter 78): one-click "Slow" respawn (15000ms) — punishment-flavored.</summary>
    public ICommand ApplySlowRespawnCommand { get; }
    /// <summary>2026-04-28 (iter 78): one-click "Glacial" respawn (60000ms) — near-permadeath feel without flag.</summary>
    public ICommand ApplyGlacialRespawnCommand { get; }

    private async Task RefreshHeroesCore()
    {
        ApplyFeedback(await _state.RefreshHeroesAsync());
        _heroes.Clear();
        foreach (var h in _state.Heroes) _heroes.Add(h);
        // iter-318: populate the parallel icon-aware projection alongside
        // the existing string-keyed Heroes list. Order preserved 1:1 so
        // operator-visible row ordering doesn't drift.
        RebuildHeroRows();
    }

    public Task RefreshHeroesAsync() => RefreshHeroesCore();

    /// <summary>
    /// 2026-05-07 (iter 318): rebuild the icon-aware projection from the
    /// current <see cref="Heroes"/> list. Called both after RefreshHeroes
    /// (bridge-driven) and SetIconResolver (Settings-driven hot-swap).
    /// Resolver returns null gracefully when not wired OR the hero portrait
    /// DDS isn't extracted/cached yet. Defensive `_heroes.ToList()` snapshot
    /// before iteration prevents the iter-317 race condition (ctor's
    /// fire-and-forget RefreshHeroesCore can mutate `_heroes` mid-enumeration).
    /// </summary>
    private void RebuildHeroRows()
    {
        _heroRows.Clear();
        foreach (var h in _heroes.ToList())
        {
            // iter-313 ResolvePortrait keys on the type name (e.g. "Han_Solo");
            // hero portraits ship as i_portrait_<TypeName>.dds in extracted DDS roots.
            var iconPath = _iconResolver?.ResolvePortrait(h.TypeName);
            _heroRows.Add(new HeroRowWithPortrait(
                h.ObjAddr, h.TypeName, h.OwnerSlot, h.Alive,
                h.RespawnRemainingMs, h.RespawnEnabled, iconPath));
        }
    }

    private async Task SetRespawnCore() => ApplyFeedback(await _state.SetCustomRespawnAsync());

    private async Task TogglePermadeathCore()
    {
        var next = !IsPermadeathEnabled;
        ApplyFeedback(await _state.TogglePermadeathAsync(next));
        OnPropertyChanged(nameof(IsPermadeathEnabled));
    }

    private async Task KillHeroCore() => ApplyFeedback(await _state.KillHeroAsync());
    private async Task ReviveHeroCore() => ApplyFeedback(await _state.ReviveHeroAsync());
    private async Task EditStatCore() => ApplyFeedback(await _state.EditStatAsync());

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
