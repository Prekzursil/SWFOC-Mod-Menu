using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Combat tab) — adapter for ICombatDispatcher.
/// All eight methods route through V2BridgeAdapter.SendRawAsync with
/// hand-built Lua matching the bridge registrations in lua_bridge.cpp's
/// RegisterAll. Errors collapse to false; UI shows "bridge rejected".
///
/// Note: SetUnitShieldAsync / SetFireRateAsync / SetTargetFilterAsync /
/// SetDamageMultiplierAsync / SetOhkAttackPowerAsync / SetAreaDamageAsync
/// land as Phase-1 mirrors today (CapabilityStatus.Phase2HookPending);
/// the live engine effect requires the IDA-pinned detours in
/// knowledge-base/phase2_hook_backlog_2026-04-26.md.
/// </summary>
public sealed class BridgeCombatDispatcher : ICombatDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeCombatDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public Task<bool> SetGodModeAsync(bool enable, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_GodMode({0})", enable ? 1 : 0), ct);

    public Task<bool> SetOhkAsync(bool enable, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_OneHitKill({0})", enable ? 1 : 0), ct);

    public Task<bool> SetOhkAttackPowerAsync(bool enable, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_ToggleOHKAttackPower({0})", enable ? 1 : 0), ct);

    public Task<bool> SetAreaDamageAsync(bool enable, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetAreaDamage({0})", enable ? 1 : 0), ct);

    public Task<bool> SetDamageMultiplierAsync(int slot, float mult, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetDamageMultiplier({0}, {1})", slot, mult), ct);

    public Task<bool> SetUnitShieldAsync(long objAddr, float shieldValue, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetUnitShield({0}, {1})", objAddr, shieldValue), ct);

    public Task<bool> SetFireRateAsync(int slot, float mult, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetFireRate({0}, {1})", slot, mult), ct);

    public Task<bool> SetTargetFilterAsync(int slot, int bitmask, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetTargetFilter({0}, {1})", slot, bitmask), ct);

    // 2026-04-28 (iter 96 + iter 100): global damage multiplier — LIVE via
    // Take_Damage_Outer detour. SWFOC_SetDamageMultiplierGlobal scales the
    // damage param at the engine call site itself.
    public Task<bool> SetDamageMultiplierGlobalAsync(float mult, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetDamageMultiplierGlobal({0})", mult), ct);

    public async Task<float> GetDamageMultiplierGlobalAsync(CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync(
            "return SWFOC_GetDamageMultiplierGlobal()", ct).ConfigureAwait(false);
        if (!rt.Succeeded || string.IsNullOrEmpty(rt.Response)) return 1.0f;
        var resp = rt.Response.Trim();
        if (resp.StartsWith("ERR:", StringComparison.Ordinal)) return 1.0f;
        return float.TryParse(resp, NumberStyles.Float, Inv, out var v) ? v : 1.0f;
    }

    // 2026-05-06 (iter 225/227): global fire-rate multiplier — LIVE via the
    // WeaponTick MinHook detour @ 0x387010 that scales the `dt` arg passed
    // to sub_140387400 by g_fireRateMult_global. Pattern matches iter-96
    // SetDamageMultiplierGlobal exactly (different RVA, same shape).
    // Sanity clamp [0.0, 100.0] enforced bridge-side; mult=0 effectively
    // freezes weapon time (use Suspend_AI for proper pause).
    public Task<bool> SetFireRateMultiplierGlobalAsync(float mult, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetFireRateMultiplierGlobal({0})", mult), ct);

    public async Task<float> GetFireRateMultiplierGlobalAsync(CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync(
            "return SWFOC_GetFireRateMultiplierGlobal()", ct).ConfigureAwait(false);
        if (!rt.Succeeded || string.IsNullOrEmpty(rt.Response)) return 1.0f;
        var resp = rt.Response.Trim();
        if (resp.StartsWith("ERR:", StringComparison.Ordinal)) return 1.0f;
        return float.TryParse(resp, NumberStyles.Float, Inv, out var v) ? v : 1.0f;
    }

    private async Task<bool> Send(string lua, CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync(lua, ct).ConfigureAwait(false);
        if (!rt.Succeeded) return false;
        var resp = rt.Response ?? string.Empty;
        return !resp.StartsWith("ERR:", StringComparison.Ordinal);
    }
}
