using System.Globalization;
using SwfocTrainer.Core.Services;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-27: centralised wrapper over the bridge's raw unit-mutation
/// helpers. Replaces inline string-built Lua at call sites in
/// <see cref="ViewModels.UnitControlTabViewModel"/> and
/// <see cref="ViewModels.PlayerStateTabViewModel"/>.
/// </summary>
/// <remarks>
/// <para>
/// The five helpers wrapped here (<c>SWFOC_SetUnitHull</c>,
/// <c>SWFOC_SetUnitInvuln</c>, <c>SWFOC_PreventUnitDeath</c>,
/// <c>SWFOC_NullAiBrain</c>, <c>SWFOC_AttachAiBrain</c>) were previously
/// constructed at every call site as <c>$"return SWFOC_X({addr}, {flag})"</c>
/// strings. A single signature change in <c>powrprof.dll</c> would silently
/// break every consumer with no compile-time hint. This dispatcher
/// concentrates the format string in one place so a bridge-side rename or
/// arity tweak fails fast and visibly.
/// </para>
/// <para>
/// Each method returns the <see cref="BridgeRoundTripResult"/> directly
/// instead of swallowing it — view-models still own the user-facing message
/// formatting, so the dispatcher stays UX-agnostic.
/// </para>
/// <para>
/// Lua 5.0 caveats baked into the format strings:
/// <list type="bullet">
///   <item>Pointer addresses MUST be emitted as decimal (no <c>0x</c>
///         prefix). Lua 5.1 added hex literals; 5.0 rejects them with
///         <c>"')' expected near 'x0'"</c>.</item>
///   <item>Booleans go as <c>1</c>/<c>0</c> integers because the bridge
///         helpers take <c>int</c> parameters, not Lua booleans.</item>
///   <item>All numbers go through <c>CultureInfo.InvariantCulture</c> so
///         a German operator's locale doesn't emit <c>1,5</c> instead of
///         <c>1.5</c>.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class V2UnitMutationDispatcher
{
    private readonly V2BridgeAdapter _bridge;

    public V2UnitMutationDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    // ulong is the natural type for x64 pointers but is not CLS-compliant.
    // The App assembly is marked CLSCompliant(true) for parity with Core,
    // so we annotate the address-taking methods explicitly. Operators and
    // tooling stay inside the App boundary, so this is safe.
    [CLSCompliant(false)]
    public Task<BridgeRoundTripResult> SetUnitHullAsync(
        ulong objAddr, double hp, CancellationToken cancellationToken)
    {
        var lua = BuildSetUnitHullLuaCommand(objAddr, hp);
        return _bridge.SendRawAsync(lua, cancellationToken);
    }

    [CLSCompliant(false)]
    public Task<BridgeRoundTripResult> SetUnitInvulnAsync(
        ulong objAddr, bool enable, CancellationToken cancellationToken)
    {
        var lua = BuildSetUnitInvulnLuaCommand(objAddr, enable);
        return _bridge.SendRawAsync(lua, cancellationToken);
    }

    [CLSCompliant(false)]
    public Task<BridgeRoundTripResult> PreventUnitDeathAsync(
        ulong objAddr, bool enable, CancellationToken cancellationToken)
    {
        var lua = BuildPreventUnitDeathLuaCommand(objAddr, enable);
        return _bridge.SendRawAsync(lua, cancellationToken);
    }

    public Task<BridgeRoundTripResult> NullAiBrainAsync(
        int slot, CancellationToken cancellationToken)
    {
        var lua = BuildNullAiBrainLuaCommand(slot);
        return _bridge.SendRawAsync(lua, cancellationToken);
    }

    public Task<BridgeRoundTripResult> AttachAiBrainAsync(
        int slot, CancellationToken cancellationToken)
    {
        var lua = BuildAttachAiBrainLuaCommand(slot);
        return _bridge.SendRawAsync(lua, cancellationToken);
    }

    // 2026-04-28 (iter 117): wrappers for the iter 110-112 engine-Lua-API
    // wires. Operator surface in UnitControl tab — single TextBox for the
    // unit Lua expression, multiple buttons fire against it. The dispatcher
    // composes the SWFOC_* call with the operator's expression spliced as
    // the first arg; the bridge then composes the actual `(<unit>):<method>(...)`
    // Lua source. Two layers of expression-splicing — the inner Lua
    // (target unit) and the outer SWFOC_* string-arg.

    public Task<BridgeRoundTripResult> MakeUnitInvulnLuaAsync(
        string unitLuaExpr, bool enable, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_MakeUnitInvulnLua", unitLuaExpr,
                enable ? "true" : "false"),
            cancellationToken);

    public Task<BridgeRoundTripResult> HideUnitLuaAsync(
        string unitLuaExpr, bool enable, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_HideUnitLua", unitLuaExpr,
                enable ? "true" : "false"),
            cancellationToken);

    public Task<BridgeRoundTripResult> PreventAiUsageLuaAsync(
        string unitLuaExpr, bool enable, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_PreventAiUsageLua", unitLuaExpr,
                enable ? "true" : "false"),
            cancellationToken);

    public Task<BridgeRoundTripResult> SetUnitSelectableLuaAsync(
        string unitLuaExpr, bool enable, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_SetUnitSelectableLua", unitLuaExpr,
                enable ? "true" : "false"),
            cancellationToken);

    public Task<BridgeRoundTripResult> DespawnUnitLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_DespawnUnitLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> StopUnitLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_StopUnitLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> RetreatUnitLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_RetreatUnitLua", unitLuaExpr),
            cancellationToken);

    // 2026-05-05 (iter 188): read-side native UX for iter 167-172 wires.
    // All 4 are no-arg unit getters; bridge dispatches via iter-167 helper
    // (Lua_DispatchUnitGetterNoArg) and returns the engine value as a string
    // in the response payload — operators read the result from the Bridge
    // responses ListBox below the buttons.
    public Task<BridgeRoundTripResult> GetHullLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetHullLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetShieldLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetShieldLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetPositionLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetPositionLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetGarrisonUnitsLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetGarrisonUnitsLua", unitLuaExpr),
            cancellationToken);

    // 2026-05-05 (iter 189): player-receiver read-side wires (iter 169).
    // Bridge's iter-167 helper is shape-agnostic so player Lua expressions
    // (e.g. Find_Player("REBEL"), Get_Local_Player()) compose into the same
    // (handle):method() pattern as units. The "unitLuaExpr" parameter name is
    // historical — it's just whatever obj-lua-expression the helper formats.
    public Task<BridgeRoundTripResult> GetPlayerCreditsLuaAsync(
        string playerLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetCreditsLua", playerLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetPlayerTechLevelLuaAsync(
        string playerLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetTechLevelLua", playerLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetPlayerFactionLuaAsync(
        string playerLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetFactionLua", playerLuaExpr),
            cancellationToken);

    // 2026-05-05 (iter 191): Inspector-tab read-side wires. iter-168/169
    // unit-receiver getters surfaced as native UX in the Inspector tab. Same
    // shape-agnostic helper used by iter 188 dispatcher methods — bridge
    // composes (unit):Get_Type() / (unit):Get_Owner() / (unit):Has_Attack_Target()
    // / (unit):Are_Engines_Online() via DoString and captures engine return
    // value as a string in the response payload.
    public Task<BridgeRoundTripResult> GetTypeLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetTypeLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetOwnerLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetOwnerLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> HasAttackTargetLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_HasAttackTargetLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> AreEnginesOnlineLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_AreEnginesOnlineLua", unitLuaExpr),
            cancellationToken);

    // 2026-05-05 (iter 197): Inspector-tab read-side extension wires (iter 171/172).
    // All 6 are no-arg unit getters via the iter-167 unit-getter helper.
    // Same shape as iter-191 read-side wires; no signature differences.
    public Task<BridgeRoundTripResult> GetParentObjectLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetParentObjectLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetAttackTargetLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetAttackTargetLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetDamageModifierLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetDamageModifierLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetContainedObjectCountLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetContainedObjectCountLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetBehaviorIdLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetBehaviorIdLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetRateOfFireModifierLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetRateOfFireModifierLua", unitLuaExpr),
            cancellationToken);

    // 2026-05-05 (iter 198): Inspector-tab arg-getter extension wires (iter 173).
    // All 4 are unit-receiver getters that take a single string arg.
    // Bridge dispatches via iter-173 Lua_DispatchUnitGetterArg helper (7th in
    // dispatcher set; first arg-getter to capture engine return values).
    // Wire format: return SWFOC_X('unit_lua_expr', 'arg_lua_expr').
    public Task<BridgeRoundTripResult> IsAbilityActiveLuaAsync(
        string unitLuaExpr, string abilityNameExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_IsAbilityActiveLua", unitLuaExpr, abilityNameExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> HasPropertyLuaAsync(
        string unitLuaExpr, string propertyNameExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_HasPropertyLua", unitLuaExpr, propertyNameExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> IsCategoryLuaAsync(
        string unitLuaExpr, string categoryNameExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_IsCategoryLua", unitLuaExpr, categoryNameExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetDistanceLuaAsync(
        string unitLuaExpr, string targetExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_GetDistanceLua", unitLuaExpr, targetExpr),
            cancellationToken);

    // 2026-05-05 (iter 199): PlayerState-tab read-side extension wires.
    // GetName is no-arg (iter 170); IsEnemy/IsAlly take other-player arg
    // (iter 179, via iter-173 helper which is shape-agnostic for player
    // receivers — same finding as iter-179's bridge-side validation).
    public Task<BridgeRoundTripResult> GetPlayerNameLuaAsync(
        string playerLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_GetNameLua", playerLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> IsEnemyLuaAsync(
        string playerLuaExpr, string otherPlayerExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_IsEnemyLua", playerLuaExpr, otherPlayerExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> IsAllyLuaAsync(
        string playerLuaExpr, string otherPlayerExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_IsAllyLua", playerLuaExpr, otherPlayerExpr),
            cancellationToken);

    // 2026-05-06 (iter 209): PlayerState diplomacy write-side wires (iter 161).
    // Lock_Tech: complement to iter-155 PlayerUnlockTech — 2-arg (player +
    // tech-name). Make_Ally / Make_Enemy: PlayerWrapper diplomacy primitives
    // — 2-arg (player + other-player). All three reuse iter-154 generic
    // 2-arg helper via existing BuildUnitLuaMethodCall (regex-invisible
    // string-literal form). WARNING: Make_Ally/Make_Enemy state RESETS on
    // every game-mode change (Galactic↔Tactical) per docs/lua-api.md
    // section 6 — caller must re-apply after each transition.
    public Task<BridgeRoundTripResult> LockTechLuaAsync(
        string playerLuaExpr, string techNameExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_LockTechLua", playerLuaExpr, techNameExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> MakeAllyLuaAsync(
        string playerLuaExpr, string otherPlayerExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_MakeAllyLua", playerLuaExpr, otherPlayerExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> MakeEnemyLuaAsync(
        string playerLuaExpr, string otherPlayerExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_MakeEnemyLua", playerLuaExpr, otherPlayerExpr),
            cancellationToken);

    // 2026-05-06 (iter 210): PlayerState player-extension write-side wires (iter
    // 164 PlayerWrapper Other section). Enable_As_Actor: no-arg via iter-112
    // helper (enables AI actor mode). Release_Credits_For_Tactical: 2-arg
    // (player + amount, releases banked credits during galactic→tactical
    // transition). Select_Object: 2-arg (player + object handle, selects unit
    // in player's UI). All three reuse existing helpers — zero new dispatchers.
    public Task<BridgeRoundTripResult> EnableAsActorLuaAsync(
        string playerLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_EnableAsActorLua", playerLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> ReleaseCreditsForTacticalLuaAsync(
        string playerLuaExpr, string amountExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_ReleaseCreditsForTacticalLua", playerLuaExpr, amountExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> SelectObjectLuaAsync(
        string playerLuaExpr, string objectExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_SelectObjectLua", playerLuaExpr, objectExpr),
            cancellationToken);

    // 2026-05-06 (iter 217): PlayerState tab final extension wires. Closes the
    // last 3 PlayerState-tab-relevant LIVE wires from the iter 100-186 work.
    // Disable_Orbital_Bombardment is iter-160 player-method (1-arg bool via
    // iter-111 obj-bool helper — shape-agnostic across unit/player receivers).
    // GlobalMakeAlly/GlobalMakeEnemy are iter-182 global 2-arg form alternatives
    // to the iter-161 obj-receiver Make_Ally/Make_Enemy already surfaced in
    // iter-209. Both forms work; operator preference. The iter-204 hardcoded-bool
    // on/off lineage now extends to 7 iters (204→208→211→212→213→215→217).
    public Task<BridgeRoundTripResult> DisableOrbitalBombardmentLuaAsync(
        string playerLuaExpr, string boolLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_DisableOrbitalBombardmentLua", playerLuaExpr, boolLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GlobalMakeAllyLuaAsync(
        string player1LuaExpr, string player2LuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_GlobalMakeAllyLua", player1LuaExpr, player2LuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GlobalMakeEnemyLuaAsync(
        string player1LuaExpr, string player2LuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_GlobalMakeEnemyLua", player1LuaExpr, player2LuaExpr),
            cancellationToken);

    // 2026-05-06 (iter 218): UnitControl Corrupt + Galactic TaskForceMoveToTarget
    // cross-tab single-wire batch. Corrupt is iter-180 wire — Underworld faction
    // signature ability that degrades unit hostility/loyalty (pairs semantically
    // with iter-212 Bribe which takes ownership). TaskForceMoveToTarget is iter-179
    // wire — TaskForceClass-only method distinct from iter-215 TaskForceMoveTo
    // (Move_To_Target takes a target object; Move_To takes a position). Both 2-arg
    // dispatchers via existing BuildUnitLuaMethodCall (shape-agnostic helper).
    public Task<BridgeRoundTripResult> CorruptLuaAsync(
        string unitLuaExpr, string amountExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_CorruptLua", unitLuaExpr, amountExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> TaskForceMoveToTargetLuaAsync(
        string taskForceLuaExpr, string targetLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_TaskForceMoveToTargetLua", taskForceLuaExpr, targetLuaExpr),
            cancellationToken);

    // 2026-05-06 (iter 219): Suspend_AI(seconds) wire — last unsurfaced wire
    // from the iter-216 changelog queue. iter-162 LIVE global with single
    // numeric arg via iter-158 helper (regex-invisible string-literal form).
    // Cinematic helper that pauses AI player decision-making for a given
    // duration. Pairs with iter-208 Lock_Controls + iter-145 cinematic camera
    // quad for full battle-pause cinematic recording workflow.
    public Task<BridgeRoundTripResult> SuspendAiLuaAsync(
        string secondsExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_SuspendAiLua", secondsExpr),
            cancellationToken);

    // 2026-05-06 (iter 211): UnitControl unit-method extension write-side wires
    // (iter 156). Activate_Ability: 1-arg ability-name string via iter-154 helper.
    // Disable_Capture: 1-arg bool via iter-111 helper. Set_Garrison_Spawn: 1-arg
    // bool via iter-111 helper. Cancel_Hyperspace: no-arg via iter-112 helper.
    // All four reuse existing helpers — zero new dispatchers. UnitControl tab's
    // SelectedUnitLuaExpr field anchors all four (shared with iter-117 7-wire
    // batch + iter-118 ChangeUnitOwner + iter-188/197/198 read-side surfacing).
    public Task<BridgeRoundTripResult> ActivateAbilityLuaAsync(
        string unitLuaExpr, string abilityNameExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_ActivateAbilityLua", unitLuaExpr, abilityNameExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> DisableCaptureLuaAsync(
        string unitLuaExpr, string boolLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_DisableCaptureLua", unitLuaExpr, boolLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> SetGarrisonSpawnLuaAsync(
        string unitLuaExpr, string boolLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_SetGarrisonSpawnLua", unitLuaExpr, boolLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> CancelHyperspaceLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_CancelHyperspaceLua", unitLuaExpr),
            cancellationToken);

    // 2026-05-06 (iter 212): UnitControl unit-method mega-batch (iter-157
    // 6 wires). Set_In_Limbo + Set_Check_Contested_Space: bool args via
    // iter-111 helper (hardcoded "1"/"0" via iter-204 on/off pattern).
    // Sell: no-arg via iter-112 helper. Bribe + Move_To + Fire_Special_Weapon:
    // 1-arg via iter-154 helper. Bribe takes player handle, Move_To takes
    // position, Fire_Special_Weapon takes slot id. All reuse existing
    // helpers — zero new dispatchers.
    public Task<BridgeRoundTripResult> SetInLimboLuaAsync(
        string unitLuaExpr, string boolLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_SetInLimboLua", unitLuaExpr, boolLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> SetCheckContestedSpaceLuaAsync(
        string unitLuaExpr, string boolLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_SetCheckContestedSpaceLua", unitLuaExpr, boolLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> SellUnitLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_SellUnitLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> BribeLuaAsync(
        string unitLuaExpr, string playerLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_BribeLua", unitLuaExpr, playerLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> MoveToLuaAsync(
        string unitLuaExpr, string positionLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_MoveToLua", unitLuaExpr, positionLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> FireSpecialWeaponLuaAsync(
        string unitLuaExpr, string slotExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_FireSpecialWeaponLua", unitLuaExpr, slotExpr),
            cancellationToken);

    // 2026-05-06 (iter 213): UnitControl unit-method bool-batch (iter-153 +
    // iter-162). Set_Cannot_Be_Killed + Enable_Stealth: bool args via iter-111
    // helper (iter-204 on/off pattern). Override_Max_Speed: 1-arg float via
    // iter-154 helper (per-unit speed override; complements iter-100's
    // per-faction SetPerFactionSpeedMultiplier global). All three reuse
    // existing helpers — zero new dispatchers.
    public Task<BridgeRoundTripResult> SetCannotBeKilledLuaAsync(
        string unitLuaExpr, string boolLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_SetCannotBeKilledLua", unitLuaExpr, boolLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> EnableStealthLuaAsync(
        string unitLuaExpr, string boolLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_EnableStealthLua", unitLuaExpr, boolLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> OverrideMaxSpeedLuaAsync(
        string unitLuaExpr, string speedExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_OverrideMaxSpeedLua", unitLuaExpr, speedExpr),
            cancellationToken);

    // 2026-05-06 (iter 214): Inspector cross-receiver arg-getter batch (iter-174
    // 4 wires). All four are 2-arg via iter-173 helper but span 3 receiver
    // types: Get_Bone_Position (unit + bone-name), Contains_Object_Type
    // (unit + child-type), Get_Space_Station_Level (player + planet),
    // Get_Type_Of_Unit (TaskForce + idx). Helper is shape-agnostic — first
    // arg is just "Lua expression that resolves to a receiver" regardless
    // of receiver type. Zero new dispatchers — pure pattern reuse.
    public Task<BridgeRoundTripResult> GetBonePositionLuaAsync(
        string unitLuaExpr, string boneNameExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_GetBonePositionLua", unitLuaExpr, boneNameExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> ContainsObjectTypeLuaAsync(
        string unitLuaExpr, string childTypeExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_ContainsObjectTypeLua", unitLuaExpr, childTypeExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetSpaceStationLevelLuaAsync(
        string playerLuaExpr, string planetExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_GetSpaceStationLevelLua", playerLuaExpr, planetExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GetTypeOfUnitLuaAsync(
        string taskForceLuaExpr, string indexExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_GetTypeOfUnitLua", taskForceLuaExpr, indexExpr),
            cancellationToken);

    // 2026-05-06 (iter 215): Galactic TaskForce write-side mega-batch (iter-175 +
    // iter-176 = 8 wires). All TaskForce-receiver methods, distinct from
    // unit-method versions (e.g. iter-157 SWFOC_MoveToLua handles unit Move_To;
    // iter-175 SWFOC_TaskForceMoveToLua handles taskforce Move_To). Mixed
    // arity: 2 no-arg (Reinforce/Release_Reinforcements via iter-112), 5 1-arg
    // (Move_To/Reinforce-with-type/Launch_Units/Attack_Target/Guard_Target/
    // Land_Units via iter-154), 1 bool (Set_As_Goal_System_Removable via iter-111).
    // NOTE: catalog says Reinforce takes a type arg. Re-checking — iter-175
    // catalog has "Reinforce(type)" → 1-arg, NOT no-arg. Only Release is no-arg.
    public Task<BridgeRoundTripResult> TaskForceMoveToLuaAsync(
        string taskForceLuaExpr, string targetExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_TaskForceMoveToLua", taskForceLuaExpr, targetExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> TaskForceReinforceLuaAsync(
        string taskForceLuaExpr, string typeExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_TaskForceReinforceLua", taskForceLuaExpr, typeExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> TaskForceReleaseReinforcementsLuaAsync(
        string taskForceLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_TaskForceReleaseReinforcementsLua", taskForceLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> TaskForceLaunchUnitsLuaAsync(
        string taskForceLuaExpr, string planetExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_TaskForceLaunchUnitsLua", taskForceLuaExpr, planetExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> TaskForceAttackTargetLuaAsync(
        string taskForceLuaExpr, string targetExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_TaskForceAttackTargetLua", taskForceLuaExpr, targetExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> TaskForceGuardTargetLuaAsync(
        string taskForceLuaExpr, string targetExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_TaskForceGuardTargetLua", taskForceLuaExpr, targetExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> TaskForceLandUnitsLuaAsync(
        string taskForceLuaExpr, string planetExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_TaskForceLandUnitsLua", taskForceLuaExpr, planetExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> TaskForceSetAsGoalSystemRemovableLuaAsync(
        string taskForceLuaExpr, string boolLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_TaskForceSetAsGoalSystemRemovableLua", taskForceLuaExpr, boolLuaExpr),
            cancellationToken);

    // 2026-05-05 (iter 193): Combat-tab per-unit write-side wires (iter 154).
    // Heal is no-arg; TakeDamage/SetDamageModifier/SetRateOfFireModifier take a
    // single float arg (passed as raw Lua string — operator may type "0.5" or
    // "myVar"). Bridge dispatches via iter-154 Lua_DispatchUnitFloatMethod.
    public Task<BridgeRoundTripResult> HealUnitLuaAsync(
        string unitLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_HealUnitLua", unitLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> TakeDamageLuaAsync(
        string unitLuaExpr, string amountExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_TakeDamageLua", unitLuaExpr, amountExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> SetDamageModifierLuaAsync(
        string unitLuaExpr, string modifierExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_SetDamageModifierLua", unitLuaExpr, modifierExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> SetRateOfFireModifierLuaAsync(
        string unitLuaExpr, string modifierExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_SetRateOfFireModifierLua", unitLuaExpr, modifierExpr),
            cancellationToken);

    // 2026-05-05 (iter 194): UnitControl combat-order extension wires (iter 163).
    // Each takes a (unit, target) pair: Attack_Target / Guard_Target target a
    // unit, Divert targets a position. Bridge dispatches via iter-154 generic
    // 2-arg helper. The wire format places SelectedUnitLuaExpr first and the
    // target/position second.
    public Task<BridgeRoundTripResult> AttackTargetLuaAsync(
        string unitLuaExpr, string targetLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_AttackTargetLua", unitLuaExpr, targetLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> GuardTargetLuaAsync(
        string unitLuaExpr, string targetLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_GuardTargetLua", unitLuaExpr, targetLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> DivertLuaAsync(
        string unitLuaExpr, string positionLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaMethodCall("SWFOC_DivertLua", unitLuaExpr, positionLuaExpr),
            cancellationToken);

    /// <summary>
    /// 2026-04-29 (iter 118): wraps the iter 108 LIVE wire for unit-level
    /// ownership transfer. Two-Lua-expr signature: target unit AND target
    /// player. Operator surface: UnitControl tab — reuses the iter 117
    /// SelectedUnitLuaExpr TextBox plus a separate TargetPlayerLuaExpr
    /// TextBox.
    /// </summary>
    /// <remarks>
    /// Wire format pinned in <c>Iter118ChangeUnitOwnerShapeTests</c>:
    /// outer single quotes wrap each inner Lua expression so embedded
    /// double-quotes pass through unescaped. Inner single quotes are
    /// escaped to <c>\'</c> identical to <see cref="BuildUnitLuaMethodCall"/>.
    /// </remarks>
    public Task<BridgeRoundTripResult> ChangeUnitOwnerLuaAsync(
        string unitLuaExpr, string playerLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildChangeUnitOwnerLuaCommand(unitLuaExpr, playerLuaExpr),
            cancellationToken);

    internal static string BuildChangeUnitOwnerLuaCommand(
        string unitLuaExpr, string playerLuaExpr)
    {
        var safeUnit = unitLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var safePlayer = playerLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"return SWFOC_ChangeUnitOwner('{safeUnit}', '{safePlayer}')");
    }

    /// <summary>
    /// 2026-04-29 (iter 119): wraps the iter 109 LIVE wire for Lua-driven
    /// unit spawning. Three-expr signature (player, type, position) — the
    /// most expressive of the iter 100-113 wires. Operator surface: Spawning
    /// tab — sibling to the existing PHASE 2 PENDING SWFOC_SpawnUnit button.
    /// </summary>
    /// <remarks>
    /// Wire format pinned in <c>Iter119SpawnUnitLuaShapeTests</c>: three
    /// independent inner expressions, each wrapped in single quotes with
    /// single-quote escaping. Different from iter 117/118 in that all
    /// three args take Lua expression strings (not bool/no-arg).
    /// </remarks>
    public Task<BridgeRoundTripResult> SpawnUnitLuaAsync(
        string playerLuaExpr, string typeLuaExpr, string positionLuaExpr,
        CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildSpawnUnitLuaCommand(playerLuaExpr, typeLuaExpr, positionLuaExpr),
            cancellationToken);

    internal static string BuildSpawnUnitLuaCommand(
        string playerLuaExpr, string typeLuaExpr, string positionLuaExpr)
    {
        var safePlayer = playerLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var safeType = typeLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var safePos = positionLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"return SWFOC_SpawnUnitLua('{safePlayer}', '{safeType}', '{safePos}')");
    }

    // 2026-05-05 (iter 195): Spawning tab spawn-variant extension wires (iter 185).
    // ReinforceUnit + SpawnFromReinforcementPool share the (player, type, position)
    // parameter shape with iter 109 Spawn_Unit. CreateGenericObject has DIFFERENT
    // parameter order: (type, position, player). Each method's wire format
    // matches the engine call shape exactly so operators don't have to mentally
    // re-order.
    public Task<BridgeRoundTripResult> ReinforceUnitLuaAsync(
        string playerLuaExpr, string typeLuaExpr, string positionLuaExpr,
        CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildSpawnVariantPlayerTypePosCommand("SWFOC_ReinforceUnitLua",
                playerLuaExpr, typeLuaExpr, positionLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> SpawnFromReinforcementPoolLuaAsync(
        string playerLuaExpr, string typeLuaExpr, string positionLuaExpr,
        CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildSpawnVariantPlayerTypePosCommand("SWFOC_SpawnFromReinforcementPoolLua",
                playerLuaExpr, typeLuaExpr, positionLuaExpr),
            cancellationToken);

    /// <summary>
    /// 2026-05-05 (iter 195): CreateGenericObject takes (type, position, player)
    /// — DIFFERENT parameter order from iter-109 Spawn_Unit and the iter-185
    /// reinforcement spawn variants. Catalog rationale + iter-185 pin tests
    /// already document this; the dispatcher signature MUST follow the engine
    /// order (NOT the operator-friendly player-first order).
    /// </summary>
    public Task<BridgeRoundTripResult> CreateGenericObjectLuaAsync(
        string typeLuaExpr, string positionLuaExpr, string playerLuaExpr,
        CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildCreateGenericObjectCommand(typeLuaExpr, positionLuaExpr, playerLuaExpr),
            cancellationToken);

    // 2026-05-05 (iter 200): Galactic-tab FOW (Fog-of-War) reveal wires.
    // FOWRevealAll/FOWUndoRevealAll are iter-180 (1-arg player) — reuse the
    // shape-agnostic BuildUnitLuaNoArgCall helper (name says "Unit" but the
    // builder is just `return SWFOC_X('expr')` — same shape works for any
    // single-string-arg dispatch). FOWReveal is iter-184 (3-arg
    // player/position/radius) and uses the new iter-184 global-3-arg helper.
    // Each method's signature mirrors the engine FOWManager method order
    // exactly so operators don't have to mentally re-order args.
    public Task<BridgeRoundTripResult> FOWRevealAllLuaAsync(
        string playerLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_FOWRevealAllLua", playerLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> FOWUndoRevealAllLuaAsync(
        string playerLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_FOWUndoRevealAllLua", playerLuaExpr),
            cancellationToken);

    /// <summary>
    /// 2026-05-05 (iter 200): partial-reveal complement to FOWRevealAll.
    /// Wraps FOWManager.Reveal(player, position, radius) — the iter-184
    /// 11th helper / 3-arg global. Operators can reveal a specific area
    /// instead of the entire galactic map. Position is a Lua expression
    /// (typical: <c>FindPlanet("Yavin"):Get_Position()</c>); radius is a
    /// raw Lua number expression (typical: <c>"500"</c>).
    /// </summary>
    public Task<BridgeRoundTripResult> FOWRevealLuaAsync(
        string playerLuaExpr, string positionLuaExpr, string radiusLuaExpr,
        CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildFOWRevealCommand(playerLuaExpr, positionLuaExpr, radiusLuaExpr),
            cancellationToken);

    internal static string BuildFOWRevealCommand(
        string playerLuaExpr, string positionLuaExpr, string radiusLuaExpr)
    {
        // Param order: player, position, radius — matches FOWManager.Reveal exactly.
        var safePlayer = playerLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var safePos = positionLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var safeRadius = radiusLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"return SWFOC_FOWRevealLua('{safePlayer}', '{safePos}', '{safeRadius}')");
    }

    // 2026-05-05 (iter 201): WorldState-tab Story & Audio native UX wires
    // (iter-159 string-arg globals). All 4 take a single string argument
    // via the iter-158 global-arg helper. Bridge composes
    // `Story_Event(name)` / `Add_Objective(name)` / `Play_Music(name)` /
    // `Play_SFX_Event(name)` and dispatches via DoString. Operator surface:
    // a single TextBox in the new "Story & Audio" GroupBox shared across
    // all 4 buttons (same pattern as iter-200 FOW player Lua expr).
    public Task<BridgeRoundTripResult> StoryEventLuaAsync(
        string nameLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_StoryEventLua", nameLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> AddObjectiveLuaAsync(
        string nameLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_AddObjectiveLua", nameLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> PlayMusicLuaAsync(
        string nameLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_PlayMusicLua", nameLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> PlaySfxEventLuaAsync(
        string nameLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_PlaySfxEventLua", nameLuaExpr),
            cancellationToken);

    // 2026-05-05 (iter 202): WorldState audio + story-trigger extension.
    // Stop_All_Music + Resume_Mode_Based_Music are iter-166 no-arg globals
    // (Lua_DispatchGlobalNoArgMethod on the bridge side) — need a NEW
    // BuildGlobalLuaNoArgCall builder since `BuildUnitLuaNoArgCall("SWFOC_X", "")`
    // would emit `return SWFOC_X('')` (empty-string arg, NOT no-arg). Story
    // Event Trigger reuses the iter-159 string-arg shape via existing
    // BuildUnitLuaNoArgCall — same shared StoryAudioNameLuaExpr field.
    public Task<BridgeRoundTripResult> StopAllMusicLuaAsync(
        CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildGlobalLuaNoArgCall("SWFOC_StopAllMusicLua"),
            cancellationToken);

    public Task<BridgeRoundTripResult> ResumeModeBasedMusicLuaAsync(
        CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildGlobalLuaNoArgCall("SWFOC_ResumeModeBasedMusicLua"),
            cancellationToken);

    public Task<BridgeRoundTripResult> StoryEventTriggerLuaAsync(
        string nameLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_StoryEventTriggerLua", nameLuaExpr),
            cancellationToken);

    /// <summary>
    /// 2026-05-05 (iter 204): toggles SFXManager.Allow_Unit_Reponse_VO(bool)
    /// to gate per-unit voice-over responses to player commands.
    /// **Engine typo: "Reponse" not "Response".** The catalog rationale +
    /// iter-181 Iter181NamespaceExpansionTests pin this typo so future
    /// readers don't "correct" it. iter 204 surfaces it through dispatcher
    /// → VM → XAML, with this iter's pin tests asserting the typo
    /// survives at every layer.
    /// </summary>
    public Task<BridgeRoundTripResult> SfxAllowUnitReponseVoLuaAsync(
        string boolLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_SFXAllowUnitReponseVoLua", boolLuaExpr),
            cancellationToken);

    /// <summary>
    /// 2026-05-05 (iter 208): cinematic input-lock pair. Lock_Controls(bool)
    /// is iter-160 (1-arg via iter-158 helper); Unlock_Controls() is iter-180
    /// (no-arg via iter-166 helper). Operator workflow: LockControls(true) →
    /// record cutscene with iter-145 cinematic camera + iter-150 letterbox +
    /// iter-201 Play_Music → Unlock_Controls() to restore. Pair-completion
    /// across two iter shapes — bool-arg and no-arg.
    /// </summary>
    public Task<BridgeRoundTripResult> LockControlsLuaAsync(
        string boolLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_LockControlsLua", boolLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> UnlockControlsLuaAsync(
        CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildGlobalLuaNoArgCall("SWFOC_UnlockControlsLua"),
            cancellationToken);

    /// <summary>
    /// 2026-05-07 (iter 461): SWFOC_TriggerVictory native UX wire. Surfaces
    /// the iter-450 DORMANT MinHook scaffolding through the editor with a
    /// dedicated WorldState GroupBox + 14-name allow-list ComboBox. The
    /// bridge wrapper validates the victory_type against
    /// kKnownVictoryTypes[] and returns PHASE2_PENDING until iter-450c+
    /// flips MH_EnableHook to active injection. Reuses the iter-201 single-
    /// string-arg shape via BuildUnitLuaNoArgCall — same wire format as
    /// SWFOC_StoryEventLua / SWFOC_AddObjectiveLua / SWFOC_PlayMusicLua.
    /// </summary>
    public Task<BridgeRoundTripResult> TriggerVictoryLuaAsync(
        string victoryTypeLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_TriggerVictory", victoryTypeLuaExpr),
            cancellationToken);

    /// <summary>
    /// 2026-05-05 (iter 202): build a `return SWFOC_X()` string for
    /// no-receiver, no-arg engine Lua APIs (iter-166 batch + iter-178 getters
    /// when called for side-effects rather than return capture). Distinct
    /// from <see cref="BuildUnitLuaNoArgCall"/> which always emits a single
    /// quoted argument.
    /// </summary>
    internal static string BuildGlobalLuaNoArgCall(string swfocFn)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"return {swfocFn}()");

    // 2026-05-05 (iter 203): Spawning-tab Discovery helpers wires
    // (iter-177 trio + iter-186 Find_Nearest). All return engine handles
    // useful for piping into spawn workflows. iter-177 wires reuse the
    // 1-arg shape; iter-186 needs the new generic 3-arg builder.
    public Task<BridgeRoundTripResult> FindObjectTypeLuaAsync(
        string typeNameLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_FindObjectTypeLua", typeNameLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> FindPlanetLuaAsync(
        string planetNameLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_FindPlanetLua", planetNameLuaExpr),
            cancellationToken);

    public Task<BridgeRoundTripResult> FindFirstObjectLuaAsync(
        string typeNameLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_FindFirstObjectLua", typeNameLuaExpr),
            cancellationToken);

    /// <summary>
    /// 2026-05-05 (iter 206): Find_All_Objects_Of_Type(type) discovery wire
    /// (iter-179 LIVE). Returns engine table-handle of every instance of
    /// the given type currently in the running game. Pairs naturally with
    /// iter-177 Find_First_Object (single instance) and iter-186 Find_Nearest
    /// (closest instance) — operators get the trio "first / nearest / all"
    /// in one place. Reuses iter-203 FindTypeNameLuaExpr field.
    /// </summary>
    public Task<BridgeRoundTripResult> FindAllObjectsOfTypeLuaAsync(
        string typeNameLuaExpr, CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildUnitLuaNoArgCall("SWFOC_FindAllObjectsOfTypeLua", typeNameLuaExpr),
            cancellationToken);

    /// <summary>
    /// 2026-05-05 (iter 203): Find_Nearest(type, pos, player) discovery wire.
    /// Returns the engine handle of the nearest matching object or nil.
    /// First wire to use the new generic 3-arg builder
    /// <see cref="BuildSwfocLua3ArgCall"/> — distinct from iter-200's
    /// FOW-specific builder which hardcoded the SWFOC name.
    /// </summary>
    public Task<BridgeRoundTripResult> FindNearestLuaAsync(
        string typeLuaExpr, string positionLuaExpr, string playerLuaExpr,
        CancellationToken cancellationToken)
        => _bridge.SendRawAsync(
            BuildSwfocLua3ArgCall("SWFOC_FindNearestLua", typeLuaExpr, positionLuaExpr, playerLuaExpr),
            cancellationToken);

    /// <summary>
    /// 2026-05-05 (iter 203): generic 3-arg SWFOC wire builder. Same wire
    /// shape as <see cref="BuildFOWRevealCommand"/> but accepts the SWFOC
    /// name as a parameter, so future 3-arg discovery / mutation wires can
    /// reuse it without per-wire builders.
    /// </summary>
    internal static string BuildSwfocLua3ArgCall(
        string swfocFn, string arg1, string arg2, string arg3)
    {
        var safe1 = arg1.Replace("'", "\\'", StringComparison.Ordinal);
        var safe2 = arg2.Replace("'", "\\'", StringComparison.Ordinal);
        var safe3 = arg3.Replace("'", "\\'", StringComparison.Ordinal);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"return {swfocFn}('{safe1}', '{safe2}', '{safe3}')");
    }

    internal static string BuildSpawnVariantPlayerTypePosCommand(
        string swfocFn, string playerLuaExpr, string typeLuaExpr, string positionLuaExpr)
    {
        var safePlayer = playerLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var safeType = typeLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var safePos = positionLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"return {swfocFn}('{safePlayer}', '{safeType}', '{safePos}')");
    }

    internal static string BuildCreateGenericObjectCommand(
        string typeLuaExpr, string positionLuaExpr, string playerLuaExpr)
    {
        // Param order: type, position, player — matches engine API exactly.
        var safeType = typeLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var safePos = positionLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var safePlayer = playerLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"return SWFOC_CreateGenericObjectLua('{safeType}', '{safePos}', '{safePlayer}')");
    }

    /// <summary>
    /// 2026-04-28 (iter 117): build a <c>return SWFOC_X('UNIT', 'BOOL')</c>
    /// string. Single quotes wrap the inner Lua expressions so embedded
    /// double-quotes (typical: <c>Find_First_Object("Empire_AT_AT")</c>)
    /// survive without escapes — Lua's string literals accept either
    /// quote style. Single quotes inside the unit expression are escaped
    /// to <c>\'</c>.
    /// </summary>
    internal static string BuildUnitLuaMethodCall(
        string swfocFn, string unitLuaExpr, string boolOrArgs)
    {
        var safe = unitLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"return {swfocFn}('{safe}', '{boolOrArgs}')");
    }

    internal static string BuildUnitLuaNoArgCall(
        string swfocFn, string unitLuaExpr)
    {
        var safe = unitLuaExpr.Replace("'", "\\'", StringComparison.Ordinal);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"return {swfocFn}('{safe}')");
    }

    // --- Lua command builders ---------------------------------------------
    // Internal so unit tests can pin the exact wire format. The Build*
    // methods migrated here from UnitControlTabViewModel + the inline
    // string.Create calls in PlayerStateTabViewModel as part of the
    // 2026-04-27 service-wrapper consolidation.

    internal static string BuildSetUnitHullLuaCommand(ulong addr, double hp)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"return SWFOC_SetUnitHull({addr}, {hp})");

    internal static string BuildSetUnitInvulnLuaCommand(ulong addr, bool enable)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"return SWFOC_SetUnitInvuln({addr}, {(enable ? 1 : 0)})");

    internal static string BuildPreventUnitDeathLuaCommand(ulong addr, bool enable)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"return SWFOC_PreventUnitDeath({addr}, {(enable ? 1 : 0)})");

    internal static string BuildNullAiBrainLuaCommand(int slot)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"return SWFOC_NullAiBrain({slot})");

    internal static string BuildAttachAiBrainLuaCommand(int slot)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"return SWFOC_AttachAiBrain({slot})");
}
