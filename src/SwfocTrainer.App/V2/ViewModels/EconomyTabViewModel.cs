using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

// ============================================================================
// Tab — Economy
//
// Thin INPC wrapper over Core.V2Vm.EconomyTabState. The Core state owns the
// FeatureToggleCoordinator + dispatcher contract; this layer adds INPC,
// AsyncRelayCommand, and a UI-friendly LastStatus line driven by the shared
// feedback sink.
//
// Slot=-1 routes to the local player (single-arg Lua variants); slot >= 0
// targets a specific player slot. Multiplier 1.0 is the no-op identity.
// ============================================================================

public sealed class EconomyTabViewModel : ObservableBase
{
    private readonly EconomyTabState _state;
    private readonly RecordingFeedbackSink _sink;
    private readonly FeatureToggleCoordinator _toggles;

    private int _slot = -1;
    private double _creditsAmount = 100000;
    private int _techLevel = 5;
    private float _incomeMultiplier = 1.0f;
    private float _buildSpeedMultiplier = 1.0f;
    private float _buildCostMultiplier = 1.0f;
    private double _freezeCreditsTarget = 99999;
    private string _lastStatus = "(idle)";
    // 2026-05-06 (iter 233): GLOBAL economy controls — bound to the new
    // "GLOBAL economy controls (LIVE)" GroupBox. Default mult=1.0 = identity.
    private float _globalCreditsMultiplier = 1.0f;

    public EconomyTabViewModel(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _sink = new RecordingFeedbackSink();
        _toggles = new FeatureToggleCoordinator(_sink);
        var dispatcher = new BridgeEconomyDispatcher(bridge);
        _state = new EconomyTabState(dispatcher, _sink, _toggles);

        SetCreditsCommand = new AsyncRelayCommand(SetCreditsCore, onError: HandleError);
        SetTechCommand = new AsyncRelayCommand(SetTechCore, onError: HandleError);
        DrainEnemyCommand = new AsyncRelayCommand(DrainEnemyCore, onError: HandleError);
        UncapCreditsCommand = new AsyncRelayCommand(UncapCreditsCore, onError: HandleError);
        SetIncomeMultCommand = new AsyncRelayCommand(SetIncomeMultCore, () => false, HandleError);
        SetBuildSpeedCommand = new AsyncRelayCommand(SetBuildSpeedCore, () => false, HandleError);
        SetBuildCostCommand = new AsyncRelayCommand(SetBuildCostCore, () => false, HandleError);
        ToggleFreezeCommand = new AsyncRelayCommand(ToggleFreezeCore, () => false, HandleError);
        ToggleInstantBuildCommand = new AsyncRelayCommand(ToggleInstantBuildCore, () => false, HandleError);
        ToggleFreeBuildCommand = new AsyncRelayCommand(ToggleFreeBuildCore, () => false, HandleError);
        // 2026-05-06 (iter 233): GLOBAL credits freeze + mult LIVE wires.
        SetCreditsFreezeOnCommand = new AsyncRelayCommand(SetCreditsFreezeOnCore, onError: HandleError);
        SetCreditsFreezeOffCommand = new AsyncRelayCommand(SetCreditsFreezeOffCore, onError: HandleError);
        SetCreditsMultiplierGlobalCommand = new AsyncRelayCommand(SetCreditsMultGlobalCore, onError: HandleError);
        GetCreditsMultiplierGlobalCommand = new AsyncRelayCommand(GetCreditsMultGlobalCore, onError: HandleError);

        InitCapabilityMetadata();
    }

    /// <summary>Test ctor — accepts a recording dispatcher to skip the bridge.</summary>
    internal EconomyTabViewModel(IEconomyDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _sink = new RecordingFeedbackSink();
        _toggles = new FeatureToggleCoordinator(_sink);
        _state = new EconomyTabState(dispatcher, _sink, _toggles);

        SetCreditsCommand = new AsyncRelayCommand(SetCreditsCore, onError: HandleError);
        SetTechCommand = new AsyncRelayCommand(SetTechCore, onError: HandleError);
        DrainEnemyCommand = new AsyncRelayCommand(DrainEnemyCore, onError: HandleError);
        UncapCreditsCommand = new AsyncRelayCommand(UncapCreditsCore, onError: HandleError);
        SetIncomeMultCommand = new AsyncRelayCommand(SetIncomeMultCore, () => false, HandleError);
        SetBuildSpeedCommand = new AsyncRelayCommand(SetBuildSpeedCore, () => false, HandleError);
        SetBuildCostCommand = new AsyncRelayCommand(SetBuildCostCore, () => false, HandleError);
        ToggleFreezeCommand = new AsyncRelayCommand(ToggleFreezeCore, () => false, HandleError);
        ToggleInstantBuildCommand = new AsyncRelayCommand(ToggleInstantBuildCore, () => false, HandleError);
        ToggleFreeBuildCommand = new AsyncRelayCommand(ToggleFreeBuildCore, () => false, HandleError);
        SetCreditsFreezeOnCommand = new AsyncRelayCommand(SetCreditsFreezeOnCore, onError: HandleError);
        SetCreditsFreezeOffCommand = new AsyncRelayCommand(SetCreditsFreezeOffCore, onError: HandleError);
        SetCreditsMultiplierGlobalCommand = new AsyncRelayCommand(SetCreditsMultGlobalCore, onError: HandleError);
        GetCreditsMultiplierGlobalCommand = new AsyncRelayCommand(GetCreditsMultGlobalCore, onError: HandleError);

        InitCapabilityMetadata();
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(SetCredits))]
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(SetTech))]
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(DrainEnemy))]
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(UncapCredits))]
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(SetIncomeMult))]
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(SetBuildSpeed))]
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(SetBuildCost))]
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(ToggleFreeze))]
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(ToggleInstantBuild))]
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(ToggleFreeBuild))]
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(SetCreditsFreezeGlobal))]
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(SetCreditsMultiplierGlobal))]
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(GetCreditsMultiplierGlobal))]
    private void InitCapabilityMetadata()
    {
        // 2026-04-27 (iter 60): per-button capability metadata. Economy
        // mixes LIVE (credits/tech writers, drain, uncap) with PHASE 2
        // PENDING (income mult, build speed/cost, freeze, instant-build,
        // free-build — all Phase-1-mirror).
        SetCredits = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set credits", "SWFOC_SetCreditsForSlot");
        SetTech = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set tech level", "SWFOC_SetTechForSlot");
        DrainEnemy = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Drain enemy credits", "SWFOC_DrainEnemyCredits");
        UncapCredits = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Uncap credits", "SWFOC_UncapCredits");
        SetIncomeMult = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set income multiplier", "SWFOC_SetIncomeMultiplier");
        SetBuildSpeed = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set build speed", "SWFOC_SetBuildSpeed");
        SetBuildCost = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set build cost", "SWFOC_SetBuildCost");
        ToggleFreeze = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Toggle freeze credits", "SWFOC_FreezeCredits");
        ToggleInstantBuild = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Toggle instant build", "SWFOC_InstantBuild");
        ToggleFreeBuild = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Toggle free build", "SWFOC_FreeBuild");
        // 2026-05-06 (iter 233): GLOBAL credits LIVE wires (iter 231 +4 LIVE flips).
        // Distinct surface from per-slot freeze/income mult above (those stay PHASE 2 PENDING).
        SetCreditsFreezeGlobal = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Freeze credits (GLOBAL)",
            "SWFOC_SetCreditsFreezeGlobal", "SWFOC_GetCreditsFreezeGlobal");
        SetCreditsMultiplierGlobal = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set credits mult (GLOBAL)", "SWFOC_SetCreditsMultiplierGlobal");
        GetCreditsMultiplierGlobal = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Read credits mult (GLOBAL)", "SWFOC_GetCreditsMultiplierGlobal");
    }

    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetCredits { get; private set; } = default!;
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetTech { get; private set; } = default!;
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction DrainEnemy { get; private set; } = default!;
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction UncapCredits { get; private set; } = default!;
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetIncomeMult { get; private set; } = default!;
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetBuildSpeed { get; private set; } = default!;
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetBuildCost { get; private set; } = default!;
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ToggleFreeze { get; private set; } = default!;
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ToggleInstantBuild { get; private set; } = default!;
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ToggleFreeBuild { get; private set; } = default!;
    /// <summary>2026-05-06 (iter 233): GLOBAL credits freeze toggle (LIVE iter 231).</summary>
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetCreditsFreezeGlobal { get; private set; } = default!;
    /// <summary>2026-05-06 (iter 233): GLOBAL credits multiplier setter (LIVE iter 231).</summary>
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetCreditsMultiplierGlobal { get; private set; } = default!;
    /// <summary>2026-05-06 (iter 233): GLOBAL credits multiplier reader (LIVE iter 231).</summary>
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction GetCreditsMultiplierGlobal { get; private set; } = default!;

    public IReadOnlyList<SwfocTrainer.Core.Diagnostics.CapabilityAwareAction> AllActions => new[]
    {
        SetCredits, SetTech, DrainEnemy, UncapCredits,
        SetIncomeMult, SetBuildSpeed, SetBuildCost,
        ToggleFreeze, ToggleInstantBuild, ToggleFreeBuild,
        // iter 233: GLOBAL credits freeze + mult LIVE wires (closes A1.x FreezeCredits arc)
        SetCreditsFreezeGlobal, SetCreditsMultiplierGlobal, GetCreditsMultiplierGlobal,
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

    // ─── Bindable inputs ────────────────────────────────────────

    public int Slot
    {
        get => _slot;
        set { if (SetField(ref _slot, value)) _state.Slot = value; }
    }

    public double CreditsAmount
    {
        get => _creditsAmount;
        set { if (SetField(ref _creditsAmount, value)) _state.CreditsAmount = value; }
    }

    public int TechLevel
    {
        get => _techLevel;
        set { if (SetField(ref _techLevel, value)) _state.TechLevel = value; }
    }

    public float IncomeMultiplier
    {
        get => _incomeMultiplier;
        set { if (SetField(ref _incomeMultiplier, value)) _state.IncomeMultiplier = value; }
    }

    public float BuildSpeedMultiplier
    {
        get => _buildSpeedMultiplier;
        set { if (SetField(ref _buildSpeedMultiplier, value)) _state.BuildSpeedMultiplier = value; }
    }

    public float BuildCostMultiplier
    {
        get => _buildCostMultiplier;
        set { if (SetField(ref _buildCostMultiplier, value)) _state.BuildCostMultiplier = value; }
    }

    public double FreezeCreditsTarget
    {
        get => _freezeCreditsTarget;
        set { if (SetField(ref _freezeCreditsTarget, value)) _state.FreezeCreditsTarget = value; }
    }

    /// <summary>
    /// 2026-05-06 (iter 233): GLOBAL credits multiplier staged for the
    /// "Apply (GLOBAL)" button on the new Economy tab "GLOBAL economy controls"
    /// GroupBox. mult=1.0 = identity. Bridge clamps [0.0, 100.0].
    /// </summary>
    public float GlobalCreditsMultiplier
    {
        get => _globalCreditsMultiplier;
        set { if (SetField(ref _globalCreditsMultiplier, value)) _state.GlobalCreditsMultiplierStaged = value; }
    }

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge(
        "SWFOC_SetCredits", "SWFOC_SetTechLevel", "SWFOC_DrainEnemyCredits", "SWFOC_UncapCredits",
        "SWFOC_SetIncomeMultiplier", "SWFOC_SetBuildSpeed", "SWFOC_SetBuildCost",
        "SWFOC_FreezeCredits", "SWFOC_InstantBuild", "SWFOC_FreeBuild");

    public bool IsFreezeCreditsEnabled => _toggles.IsEnabled("freeze_credits");
    public bool IsInstantBuildEnabled => _toggles.IsEnabled("instant_build");
    public bool IsFreeBuildEnabled => _toggles.IsEnabled("free_build");

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    // ─── Commands ────────────────────────────────────────────────

    public ICommand SetCreditsCommand { get; }
    public ICommand SetTechCommand { get; }
    public ICommand DrainEnemyCommand { get; }
    public ICommand UncapCreditsCommand { get; }
    public ICommand SetIncomeMultCommand { get; }
    public ICommand SetBuildSpeedCommand { get; }
    public ICommand SetBuildCostCommand { get; }
    public ICommand ToggleFreezeCommand { get; }
    public ICommand ToggleInstantBuildCommand { get; }
    public ICommand ToggleFreeBuildCommand { get; }
    /// <summary>2026-05-06 (iter 233): freeze ON via SWFOC_SetCreditsFreezeGlobal(1).</summary>
    public ICommand SetCreditsFreezeOnCommand { get; }
    /// <summary>2026-05-06 (iter 233): freeze OFF via SWFOC_SetCreditsFreezeGlobal(0).</summary>
    public ICommand SetCreditsFreezeOffCommand { get; }
    /// <summary>2026-05-06 (iter 233): apply GLOBAL mult via SWFOC_SetCreditsMultiplierGlobal.</summary>
    public ICommand SetCreditsMultiplierGlobalCommand { get; }
    /// <summary>2026-05-06 (iter 233): read-back GLOBAL mult via SWFOC_GetCreditsMultiplierGlobal.</summary>
    public ICommand GetCreditsMultiplierGlobalCommand { get; }

    // ─── Command bodies ─────────────────────────────────────────

    private async Task SetCreditsCore() { ApplyFeedback(await _state.SetCreditsAsync()); }
    private async Task SetTechCore() { ApplyFeedback(await _state.SetTechAsync()); }
    private async Task DrainEnemyCore() { ApplyFeedback(await _state.DrainEnemyCreditsAsync()); }
    private async Task UncapCreditsCore() { ApplyFeedback(await _state.UncapCreditsAsync()); }
    private async Task SetIncomeMultCore() { ApplyFeedback(await _state.SetIncomeMultiplierAsync()); }
    private async Task SetBuildSpeedCore() { ApplyFeedback(await _state.SetBuildSpeedAsync()); }
    private async Task SetBuildCostCore() { ApplyFeedback(await _state.SetBuildCostAsync()); }

    private async Task ToggleFreezeCore()
    {
        var next = !_toggles.IsEnabled("freeze_credits");
        ApplyFeedback(await _state.ToggleFreezeCreditsAsync(next));
        OnPropertyChanged(nameof(IsFreezeCreditsEnabled));
    }

    private async Task ToggleInstantBuildCore()
    {
        var next = !_toggles.IsEnabled("instant_build");
        ApplyFeedback(await _state.ToggleInstantBuildAsync(next));
        OnPropertyChanged(nameof(IsInstantBuildEnabled));
    }

    private async Task ToggleFreeBuildCore()
    {
        var next = !_toggles.IsEnabled("free_build");
        ApplyFeedback(await _state.ToggleFreeBuildAsync(next));
        OnPropertyChanged(nameof(IsFreeBuildEnabled));
    }

    // 2026-05-06 (iter 233): GLOBAL credits LIVE wire handlers. Pattern
    // parallels iter-227 SetFireRateMultiplierGlobal/GetFireRateMultiplierGlobal
    // on CombatTabViewModel exactly. The hardcoded-bool freeze on/off pair
    // mirrors iter-204 lineage (now 8 iters deep: 204→208→211→212→213→215→217→233).
    private async Task SetCreditsFreezeOnCore()
    {
        _state.GlobalCreditsFreezeStaged = true;
        ApplyFeedback(await _state.SetCreditsFreezeGlobalAsync().ConfigureAwait(true));
    }

    private async Task SetCreditsFreezeOffCore()
    {
        _state.GlobalCreditsFreezeStaged = false;
        ApplyFeedback(await _state.SetCreditsFreezeGlobalAsync().ConfigureAwait(true));
    }

    private async Task SetCreditsMultGlobalCore()
    {
        ApplyFeedback(await _state.SetCreditsMultiplierGlobalAsync().ConfigureAwait(true));
    }

    private async Task GetCreditsMultGlobalCore()
    {
        var v = await _state.GetCreditsMultiplierGlobalAsync().ConfigureAwait(true);
        LastStatus = $"[ok] GetCreditsMultiplierGlobal -> {v.ToString("0.000", CultureInfo.InvariantCulture)}";
    }

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
