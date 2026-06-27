using System.Threading;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 53) — operator-facing composite quick-actions. Each
/// command bundles 2-8 primitive bridge calls into a single click, for
/// the workflows the operator runs most often. The user's overlay design
/// asked for this kind of "logical, handy, useful, intuitive" surface;
/// the editor pre-builds it today so the in-game overlay can call the
/// same composites later when it ships.
///
/// 2026-04-27 (iter 54) — composites are now capability-aware via
/// <see cref="CapabilityAwareAction"/> (extracted to the shared Core layer
/// in iter 55). When any composite mixes LIVE + Phase-2-pending primitives
/// a tab-level amber banner warns the operator.
/// </summary>
public sealed class QuickActionsTabViewModel : ObservableBase
{
    private readonly V2BridgeAdapter _bridge;
    private readonly Dictionary<string, IReadOnlyList<string>> _commandsByName = new();
    private string _lastStatus = "(idle)";

    public QuickActionsTabViewModel(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;

        OperatorGodMode = RegisterComposite("Operator god mode", new[]
        {
            "return SWFOC_GodMode(1)",
            "return SWFOC_HealAllLocal()",
            "return SWFOC_UncapCredits()",
        });
        DrainEnemies = RegisterComposite("Drain enemies", new[]
        {
            "return SWFOC_DrainEnemyCredits()",
        });
        RevealGalaxy = RegisterComposite("Reveal galaxy", new[]
        {
            "return SWFOC_RevealAll(1)",
            "return SWFOC_GetPlanets()",
            "return SWFOC_GetAllPlayers()",
        });
        ResetToggles = RegisterComposite("Reset toggles", new[]
        {
            "return SWFOC_GodMode(0)",
            "return SWFOC_OneHitKill(0)",
            "return SWFOC_SetCreditsFreezeGlobal(0)",
            "return SWFOC_SuspendAiLua('0')",
        });

        // 2026-04-27 (iter 64): operator workflow shortcuts for the
        // content-creator / streamer cases the user's overlay design
        // (knowledge-base/overlay_design_2026-04-27.md) called out.
        // Both compose existing primitives — no new bridge surface.
        BattleSetup = RegisterComposite("Battle setup", new[]
        {
            "return SWFOC_GodMode(1)",
            "return SWFOC_HealAllLocal()",
            "return SWFOC_UncapCredits()",
            "return SWFOC_DrainEnemyCredits()",
        });
        // Filming uses Hide_HUD via DoString (engine global; LIVE) and
        // pairs it with Suspend_AI + god-mode for clean cinematic shots.
        // Phase-2-only permadeath/freecam toggles are deliberately omitted
        // so the one-click composite only fires live-backed primitives.
        FilmingSetup = RegisterComposite("Filming setup", new[]
        {
            "return SWFOC_DoString(\"if Hide_HUD then Hide_HUD(1) end\")",
            "return SWFOC_SuspendAiLua('3600')",
            "return SWFOC_GodMode(1)",
        });

        // 2026-04-28 (iter 80): capstone composite trifecta tying iter 76
        // (Combat presets), iter 77 (Speed presets), and iter 78 (HeroLab
        // respawn presets) together at the workflow level. Each composite
        // is the operator's "I know what I want; one click" path for the
        // three most-common multi-tab setups.
        //
        // Tournament: a hard challenge — boosted enemy damage scalars,
        // slow respawn, real-time speed. Operator opts in to difficulty.
        TournamentSetup = RegisterComposite("Tournament setup", new[]
        {
            "return SWFOC_SetDamageMultiplierGlobal(1.5)",
            "return SWFOC_SetFireRateMultiplierGlobal(1.25)",
            "return SWFOC_SetHeroRespawn(15)",
        });
        // Sandbox: full operator control — god mode, infinite credits,
        // 2× speed, quick hero rotation. The "I just want to mess
        // around" config.
        SandboxSetup = RegisterComposite("Sandbox setup", new[]
        {
            "return SWFOC_GodMode(1)",
            "return SWFOC_HealAllLocal()",
            "return SWFOC_UncapCredits()",
            "return SWFOC_DrainEnemyCredits()",
            "return SWFOC_SetHeroRespawn(2.5)",
        });
        // Streaming: cinematic posture — HUD off, AI frozen for
        // controlled framing, slow-mo for action shots, slow respawn so
        // hero deaths look intentional. Pairs with iter-64 FilmingSetup
        // (which is more focused on stills) — Streaming is for live
        // action shots.
        StreamingSetup = RegisterComposite("Streaming setup", new[]
        {
            "return SWFOC_DoString(\"if Hide_HUD then Hide_HUD(1) end\")",
            "return SWFOC_SuspendAiLua('3600')",
            "return SWFOC_SetHeroRespawn(15)",
        });

        OperatorGodModeCommand = new AsyncRelayCommand(
            () => RunComposite(OperatorGodMode),
            onError: ex => LastStatus = $"OperatorGodMode failed: {ex.Message}");

        DrainEnemiesCommand = new AsyncRelayCommand(
            () => RunComposite(DrainEnemies),
            onError: ex => LastStatus = $"DrainEnemies failed: {ex.Message}");

        RevealGalaxyCommand = new AsyncRelayCommand(
            () => RunComposite(RevealGalaxy),
            onError: ex => LastStatus = $"RevealGalaxy failed: {ex.Message}");

        ResetTogglesCommand = new AsyncRelayCommand(
            () => RunComposite(ResetToggles),
            onError: ex => LastStatus = $"ResetToggles failed: {ex.Message}");

        BattleSetupCommand = new AsyncRelayCommand(
            () => RunComposite(BattleSetup),
            onError: ex => LastStatus = $"BattleSetup failed: {ex.Message}");

        FilmingSetupCommand = new AsyncRelayCommand(
            () => RunComposite(FilmingSetup),
            onError: ex => LastStatus = $"FilmingSetup failed: {ex.Message}");

        // 2026-04-28 (iter 80): capstone composite commands.
        TournamentSetupCommand = new AsyncRelayCommand(
            () => RunComposite(TournamentSetup),
            onError: ex => LastStatus = $"TournamentSetup failed: {ex.Message}");

        SandboxSetupCommand = new AsyncRelayCommand(
            () => RunComposite(SandboxSetup),
            onError: ex => LastStatus = $"SandboxSetup failed: {ex.Message}");

        StreamingSetupCommand = new AsyncRelayCommand(
            () => RunComposite(StreamingSetup),
            onError: ex => LastStatus = $"StreamingSetup failed: {ex.Message}");
    }

    public ICommand OperatorGodModeCommand { get; }
    public ICommand DrainEnemiesCommand { get; }
    public ICommand RevealGalaxyCommand { get; }
    public ICommand ResetTogglesCommand { get; }
    public ICommand BattleSetupCommand { get; }
    public ICommand FilmingSetupCommand { get; }

    /// <summary>2026-04-28 (iter 80): hard challenge composite — boosted enemy scalars + slow respawn + real-time speed.</summary>
    public ICommand TournamentSetupCommand { get; }
    /// <summary>2026-04-28 (iter 80): full operator control — god mode + infinite credits + 2× speed + quick respawn.</summary>
    public ICommand SandboxSetupCommand { get; }
    /// <summary>2026-04-28 (iter 80): cinematic posture — HUD off + AI frozen + slow-mo + slow respawn.</summary>
    public ICommand StreamingSetupCommand { get; }

    public CapabilityAwareAction OperatorGodMode { get; }
    public CapabilityAwareAction DrainEnemies { get; }
    public CapabilityAwareAction RevealGalaxy { get; }
    public CapabilityAwareAction ResetToggles { get; }
    public CapabilityAwareAction BattleSetup { get; }
    public CapabilityAwareAction FilmingSetup { get; }
    /// <summary>2026-04-28 (iter 80): tournament composite — Hard combat scalars + slow respawn.</summary>
    public CapabilityAwareAction TournamentSetup { get; }
    /// <summary>2026-04-28 (iter 80): sandbox composite — god mode + uncap + 2× speed + quick respawn.</summary>
    public CapabilityAwareAction SandboxSetup { get; }
    /// <summary>2026-04-28 (iter 80): streaming composite — HUD off + AI frozen + slow-mo + slow respawn.</summary>
    public CapabilityAwareAction StreamingSetup { get; }

    public IReadOnlyList<CapabilityAwareAction> AllComposites => new[]
    {
        OperatorGodMode, DrainEnemies, RevealGalaxy, ResetToggles, BattleSetup, FilmingSetup,
        // 2026-04-28 (iter 80): capstone trifecta.
        TournamentSetup, SandboxSetup, StreamingSetup,
    };

    /// <summary>
    /// 2026-04-27 (iter 61): alias of <see cref="AllComposites"/> so this
    /// VM matches the editor-wide <c>AllActions</c> contract used by every
    /// other capability-aware tab and consumed by the surface-report
    /// aggregator.
    /// </summary>
    public IReadOnlyList<CapabilityAwareAction> AllActions => AllComposites;

    public bool HasMixedComposite => AllComposites.Any(c => c.IsMixed);

    public string MixedCompositeWarning
    {
        get
        {
            var mixed = AllComposites.Where(c => c.IsMixed).ToList();
            if (mixed.Count == 0) return string.Empty;
            var parts = mixed.Select(c => $"{c.Name} ({c.Badge})");
            return "⚠ Some composites mix LIVE and PHASE 2 PENDING primitives — "
                + "an 'N/N OK' status line means every call returned a response, "
                + "NOT that every toggle had engine effect. Mixed: "
                + string.Join("; ", parts);
        }
    }

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    /// <summary>
    /// Construct a descriptor and remember the underlying Lua array so
    /// <see cref="RunComposite"/> can fire the calls without storing the
    /// commands inside <see cref="CapabilityAwareAction"/> itself
    /// (the descriptor lives in Core; raw Lua belongs in the App layer).
    /// </summary>
    private CapabilityAwareAction RegisterComposite(string name, IReadOnlyList<string> luaCommands)
    {
        _commandsByName[name] = luaCommands;
        return CapabilityAwareAction.FromLuaCommands(name, luaCommands);
    }

    private async Task RunComposite(CapabilityAwareAction composite)
    {
        var commands = _commandsByName[composite.Name];
        var ok = 0;
        var failures = new System.Collections.Generic.List<string>();
        foreach (var lua in commands)
        {
            var r = await _bridge.SendRawAsync(lua, CancellationToken.None).ConfigureAwait(true);
            if (r.Succeeded)
            {
                ok++;
            }
            else
            {
                failures.Add(CapabilityAwareAction.ExtractHelperName(lua));
            }
        }
        LastStatus = failures.Count == 0
            ? $"{composite.Name}: {ok}/{commands.Count} OK"
            : $"{composite.Name}: {ok}/{commands.Count} OK · failed: {string.Join(", ", failures)}";
    }
}
