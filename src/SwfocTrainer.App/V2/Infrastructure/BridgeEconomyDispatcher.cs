using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// Phase 1 (thread A) — adapter that satisfies <see cref="IEconomyDispatcher"/>
/// by sending hand-built Lua commands through the V2 bridge. Function names
/// match the registrations in <c>swfoc_lua_bridge/lua_bridge.cpp</c> so a
/// successful round-trip is an actual engine-side state change (Phase 1
/// helpers record into pending maps; Phase 2 detours are not yet wired).
///
/// Lua 5.0 cannot parse <c>0x</c>-prefixed integer literals — every numeric
/// argument is emitted as a decimal via <see cref="CultureInfo.InvariantCulture"/>.
/// </summary>
public sealed class BridgeEconomyDispatcher : IEconomyDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeEconomyDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public Task<bool> SetCreditsAsync(int slot, double amount, CancellationToken ct)
    {
        // slot < 0 routes to the local-player single-arg variant; slot >= 0
        // uses the per-slot helper.
        var lua = slot < 0
            ? string.Format(Inv, "return SWFOC_SetCredits({0})", amount)
            : string.Format(Inv, "return SWFOC_SetCreditsForSlot({0}, {1})", slot, amount);
        return SendAsync(lua, ct);
    }

    public Task<bool> SetTechAsync(int slot, int level, CancellationToken ct)
    {
        var lua = slot < 0
            ? string.Format(Inv, "return SWFOC_SetTechLevel({0})", level)
            : string.Format(Inv, "return SWFOC_SetTechForSlot({0}, {1})", slot, level);
        return SendAsync(lua, ct);
    }

    public Task<bool> DrainEnemyCreditsAsync(CancellationToken ct)
        => SendAsync("return SWFOC_DrainEnemyCredits()", ct);

    public Task<bool> UncapCreditsAsync(CancellationToken ct)
        => SendAsync("return SWFOC_UncapCredits()", ct);

    public Task<bool> SetIncomeMultiplierAsync(int slot, float mult, CancellationToken ct)
        => SendAsync(string.Format(Inv,
            "return SWFOC_SetIncomeMultiplier({0}, {1})", slot, mult), ct);

    public Task<bool> SetBuildSpeedAsync(int slot, float mult, CancellationToken ct)
        => SendAsync(string.Format(Inv,
            "return SWFOC_SetBuildSpeed({0}, {1})", slot, mult), ct);

    public Task<bool> SetBuildCostAsync(int slot, float mult, CancellationToken ct)
        => SendAsync(string.Format(Inv,
            "return SWFOC_SetBuildCost({0}, {1})", slot, mult), ct);

    public Task<bool> SetFreezeCreditsAsync(
        int slot, bool enable, double target, CancellationToken ct)
        => SendAsync(string.Format(Inv,
            "return SWFOC_FreezeCredits({0}, {1}, {2})",
            slot, enable ? 1 : 0, target), ct);

    public Task<bool> SetInstantBuildAsync(bool enable, CancellationToken ct)
        => SendAsync(string.Format(Inv,
            "return SWFOC_InstantBuild({0})", enable ? 1 : 0), ct);

    public Task<bool> SetFreeBuildAsync(bool enable, CancellationToken ct)
        => SendAsync(string.Format(Inv,
            "return SWFOC_FreeBuild({0})", enable ? 1 : 0), ct);

    // 2026-05-06 (iter 231/233): GLOBAL credits freeze + multiplier — LIVE via
    // AddCredits MinHook detour at RVA 0x27F370 (universal engine credit-adjust
    // function, 47 callers). Pattern parallels iter-96 SetDamageMultiplierGlobal +
    // iter-225 SetFireRateMultiplierGlobal exactly. Distinct surface from per-slot
    // SetCredits/SetIncomeMultiplier (those stay PHASE 2 PENDING). See
    // iter230_freeze_credits_re_kickoff.md for design + engine semantic caveats.
    public Task<bool> SetCreditsFreezeGlobalAsync(bool freeze, CancellationToken ct)
        => SendAsync(string.Format(Inv,
            "return SWFOC_SetCreditsFreezeGlobal({0})", freeze ? 1 : 0), ct);

    public async Task<bool> GetCreditsFreezeGlobalAsync(CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync(
            "return SWFOC_GetCreditsFreezeGlobal()", ct).ConfigureAwait(false);
        if (!rt.Succeeded || string.IsNullOrEmpty(rt.Response)) return false;
        var resp = rt.Response.Trim();
        if (resp.StartsWith("ERR:", StringComparison.Ordinal)) return false;
        return resp == "1";
    }

    public Task<bool> SetCreditsMultiplierGlobalAsync(float mult, CancellationToken ct)
        => SendAsync(string.Format(Inv,
            "return SWFOC_SetCreditsMultiplierGlobal({0})", mult), ct);

    public async Task<float> GetCreditsMultiplierGlobalAsync(CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync(
            "return SWFOC_GetCreditsMultiplierGlobal()", ct).ConfigureAwait(false);
        if (!rt.Succeeded || string.IsNullOrEmpty(rt.Response)) return 1.0f;
        var resp = rt.Response.Trim();
        if (resp.StartsWith("ERR:", StringComparison.Ordinal)) return 1.0f;
        return float.TryParse(resp, NumberStyles.Float, Inv, out var v) ? v : 1.0f;
    }

    private async Task<bool> SendAsync(string lua, CancellationToken ct)
    {
        var roundTrip = await _bridge.SendRawAsync(lua, ct).ConfigureAwait(false);
        if (!roundTrip.Succeeded) return false;
        var response = roundTrip.Response ?? string.Empty;
        // Bridge convention: error responses start with "ERR:". Anything else
        // (including "OK: ..." status strings) is treated as success.
        return !response.StartsWith("ERR:", StringComparison.Ordinal);
    }
}
