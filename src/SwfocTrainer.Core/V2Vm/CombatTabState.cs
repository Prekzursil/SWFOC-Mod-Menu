using System.Globalization;
using System.Text.RegularExpressions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// 2026-05-07 (iter 338; iter-343 added IconPath): one row in the Combat tab
/// Hardpoint Inspector. Surfaces the iter-281 SWFOC_GetHardpoints LIVE bridge
/// wire (RequiresLiveSwfoc catalog status) for operator-facing read-only
/// inspection of a unit's hardpoint vector. iter-343 ships Approach A
/// optimistic chain (per iter-342 research): GetHardpoints → SWFOC_GetTypeLua
/// per child → ResolveWeaponIcon → IconPath. If tostring(GameObjectType_handle)
/// returns name string, icons render LIVE; if it returns "userdata: 0x...",
/// IconPath stays null + iter-344 pivots to Approach B (NEW name-extraction
/// bridge wire).
/// </summary>
/// <param name="Index">Zero-based slot index inside the unit's Components array (0..31).</param>
/// <param name="ChildAddr">Engine pointer to the hardpoint child object.</param>
/// <param name="Hp">Current hull HP of the hardpoint.</param>
/// <param name="IconPath">2026-05-07 (iter 343): optional cached PNG path from ResolveWeaponIcon. Null when no resolver wired OR icon-resolution chain returns a non-name (e.g. userdata pointer).</param>
public sealed record HardpointEntry(int Index, long ChildAddr, float Hp, string? IconPath = null)
{
    /// <summary>
    /// 2026-05-07 (iter 338): parses the SWFOC_GetHardpoints bridge reply.
    /// Format per lua_bridge.cpp:2228 — "count=N child0=0x... hp0=... child1=0x... hp1=...".
    /// Empty / count=0 / malformed input → empty list (defensive null-safe).
    /// Per iter-336 preflight: this parser is the operator-facing layer above
    /// the bridge wire's raw textual format.
    /// </summary>
    public static IReadOnlyList<HardpointEntry> ParseListFromBridgeReply(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<HardpointEntry>();
        // Expect leading "count=N" sentinel; bail if absent.
        var countMatch = Regex.Match(raw, @"count=(\d+)");
        if (!countMatch.Success) return Array.Empty<HardpointEntry>();
        if (!int.TryParse(countMatch.Groups[1].Value, out var count) || count <= 0)
        {
            return Array.Empty<HardpointEntry>();
        }
        var entries = new List<HardpointEntry>(count);
        // Match "childN=0xHEX hpN=FLOAT" pairs in order.
        var pairMatches = Regex.Matches(raw, @"child(\d+)=0x([0-9A-Fa-f]+)\s+hp\1=(-?\d+(?:\.\d+)?)");
        foreach (Match m in pairMatches)
        {
            if (!int.TryParse(m.Groups[1].Value, out var idx)) continue;
            if (!long.TryParse(m.Groups[2].Value, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var addr)) continue;
            if (!float.TryParse(m.Groups[3].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var hp)) continue;
            entries.Add(new HardpointEntry(idx, addr, hp));
        }
        return entries;
    }
}

/// <summary>
/// Headless state model for V2 Tab 2 (Combat). Task #146 wires every
/// combat helper into one cohesive state-machine: god mode, OHK,
/// combined god+OHK, damage mult, shield edit, fire rate, area damage,
/// target filter.
///
/// Like <see cref="EconomyTabState"/>, this stays Core-only — no WPF
/// dependencies — so the combat-tab logic is unit-testable in isolation
/// from XAML binding.
/// </summary>
public sealed class CombatTabState
{
    private readonly ICombatDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;
    private readonly FeatureToggleCoordinator _toggles;

    public CombatTabState(
        ICombatDispatcher dispatcher,
        IUxFeedbackSink feedback,
        FeatureToggleCoordinator toggles)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(toggles);
        _dispatcher = dispatcher;
        _feedback = feedback;
        _toggles = toggles;
    }

    // ── Inputs ─────────────────────────────────────────────────

    public int Slot { get; set; } = -1;
    public long SelectedObjAddr { get; set; }
    public float DamageMultiplier { get; set; } = 1.0f;
    public float ShieldValue { get; set; }
    public float FireRateMultiplier { get; set; } = 1.0f;
    public int TargetFilterBitmask { get; set; } = 0x7;  // ENEMY|FRIENDLY|NEUTRAL

    // ── Toggles (cleanup-on-disable) ───────────────────────────

    public Task<UxFeedback> ToggleGodModeAsync(bool enable, CancellationToken ct = default) =>
        BoolToggle("god_mode", enable, _dispatcher.SetGodModeAsync, ct);

    public Task<UxFeedback> ToggleOhkAsync(bool enable, CancellationToken ct = default) =>
        BoolToggle("one_hit_kill", enable, _dispatcher.SetOhkAsync, ct);

    public Task<UxFeedback> ToggleOhkAttackPowerAsync(bool enable, CancellationToken ct = default) =>
        BoolToggle("ohk_attack_power", enable, _dispatcher.SetOhkAttackPowerAsync, ct);

    public Task<UxFeedback> ToggleAreaDamageAsync(bool enable, CancellationToken ct = default) =>
        BoolToggle("area_damage", enable, _dispatcher.SetAreaDamageAsync, ct);

    /// <summary>
    /// Combined god+OHK toggle (#128). Routes through the coordinator
    /// as a SINGLE feature key so cleanup-on-disable triggers ONE
    /// disable callback that flips both flags atomically.
    /// </summary>
    public Task<UxFeedback> ToggleCombinedGodAndOhkAsync(bool enable, CancellationToken ct = default)
    {
        return _toggles.ToggleAsync("combined_god_ohk", enable,
            action: async cancel =>
            {
                var ok1 = await _dispatcher.SetGodModeAsync(enable, cancel);
                var ok2 = await _dispatcher.SetOhkAsync(enable, cancel);
                if (ok1 && ok2)
                {
                    return UxFeedback.Success("combined_god_ohk",
                        enable ? "GodMode + OHK both enabled" : "both disabled",
                        "combined_god_ohk");
                }
                return UxFeedback.Error("combined_god_ohk",
                    $"god={ok1} ohk={ok2} — partial failure", "combined_god_ohk");
            },
            disableAction: enable
                ? async cancel =>
                {
                    var ok1 = await _dispatcher.SetGodModeAsync(false, cancel);
                    var ok2 = await _dispatcher.SetOhkAsync(false, cancel);
                    return ok1 && ok2
                        ? UxFeedback.Info("combined_god_ohk", "both disabled (cleanup)",
                            "combined_god_ohk")
                        : UxFeedback.Warning("combined_god_ohk",
                            $"cleanup partial: god={ok1} ohk={ok2}", "combined_god_ohk");
                }
        : null,
            cancellationToken: ct);
    }

    // ── Per-slot/per-unit settings (no cleanup) ────────────────

    public async Task<UxFeedback> SetDamageMultiplierAsync(CancellationToken ct = default)
    {
        if (DamageMultiplier < 0)
        {
            return EmitAndReturn(UxFeedback.Error("set_damage_mult",
                $"multiplier must be >= 0 (got {DamageMultiplier})", "set_damage_mult"));
        }
        var ok = await _dispatcher.SetDamageMultiplierAsync(Slot, DamageMultiplier, ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("set_damage_mult",
                $"slot={Slot} → {DamageMultiplier:0.00}×", "set_damage_mult")
            : UxFeedback.Error("set_damage_mult", "bridge rejected", "set_damage_mult"));
    }

    /// <summary>
    /// 2026-04-28 (iter 96 + iter 100): set the GLOBAL damage multiplier.
    /// LIVE via the Take_Damage_Outer detour @ RVA 0x38A350 — the bridge
    /// reads this value and scales <c>damageParams[0]</c> before forwarding
    /// to the engine. Sibling to <see cref="SetDamageMultiplierAsync"/>
    /// (which is per-slot and stays PHASE 2 PENDING).
    /// </summary>
    public async Task<UxFeedback> SetDamageMultiplierGlobalAsync(CancellationToken ct = default)
    {
        if (DamageMultiplier < 0)
        {
            return EmitAndReturn(UxFeedback.Error("set_damage_mult_global",
                $"multiplier must be >= 0 (got {DamageMultiplier})", "set_damage_mult_global"));
        }
        var ok = await _dispatcher.SetDamageMultiplierGlobalAsync(DamageMultiplier, ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("set_damage_mult_global",
                $"global → {DamageMultiplier:0.00}×", "set_damage_mult_global")
            : UxFeedback.Error("set_damage_mult_global",
                "bridge rejected", "set_damage_mult_global"));
    }

    /// <summary>
    /// 2026-04-28 (iter 100): read-back the global damage multiplier the
    /// bridge currently has stored. Returns 1.0 on bridge error.
    /// </summary>
    public Task<float> GetDamageMultiplierGlobalAsync(CancellationToken ct = default)
        => _dispatcher.GetDamageMultiplierGlobalAsync(ct);

    /// <summary>
    /// 2026-05-06 (iter 225 + iter 227): set the GLOBAL fire-rate multiplier.
    /// LIVE via the WeaponTick MinHook detour @ 0x387010 — the bridge scales
    /// the <c>dt</c> arg passed to sub_140387400 by <c>g_fireRateMult_global</c>.
    /// Sanity clamp [0.0, 100.0] enforced bridge-side. Per-iter-224 caveat:
    /// <c>mult=0</c> effectively freezes weapon-cooldown time; use
    /// Suspend_AI for proper AI pause. Closes A1.3 SetFireRate global path
    /// after 124-day deferral. Sibling to <see cref="SetDamageMultiplierGlobalAsync"/>.
    /// </summary>
    public async Task<UxFeedback> SetFireRateMultiplierGlobalAsync(CancellationToken ct = default)
    {
        if (FireRateMultiplier < 0)
        {
            return EmitAndReturn(UxFeedback.Error("set_fire_rate_global",
                $"multiplier must be >= 0 (got {FireRateMultiplier})", "set_fire_rate_global"));
        }
        var ok = await _dispatcher.SetFireRateMultiplierGlobalAsync(FireRateMultiplier, ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("set_fire_rate_global",
                $"global fire-rate → {FireRateMultiplier:0.00}×", "set_fire_rate_global")
            : UxFeedback.Error("set_fire_rate_global",
                "bridge rejected", "set_fire_rate_global"));
    }

    /// <summary>
    /// 2026-05-06 (iter 227): read-back the global fire-rate multiplier the
    /// bridge currently has stored. Returns 1.0 on bridge error.
    /// </summary>
    public Task<float> GetFireRateMultiplierGlobalAsync(CancellationToken ct = default)
        => _dispatcher.GetFireRateMultiplierGlobalAsync(ct);

    public async Task<UxFeedback> SetUnitShieldAsync(CancellationToken ct = default)
    {
        if (SelectedObjAddr == 0)
        {
            return EmitAndReturn(UxFeedback.Error("set_unit_shield",
                "no unit selected", "set_unit_shield"));
        }
        if (ShieldValue < 0)
        {
            return EmitAndReturn(UxFeedback.Error("set_unit_shield",
                $"shield must be >= 0 (got {ShieldValue})", "set_unit_shield"));
        }
        var ok = await _dispatcher.SetUnitShieldAsync(SelectedObjAddr, ShieldValue, ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("set_unit_shield",
                $"unit 0x{SelectedObjAddr:X} → shield {ShieldValue:0}", "set_unit_shield")
            : UxFeedback.Error("set_unit_shield", "bridge rejected", "set_unit_shield"));
    }

    public async Task<UxFeedback> SetFireRateAsync(CancellationToken ct = default)
    {
        if (FireRateMultiplier <= 0)
        {
            return EmitAndReturn(UxFeedback.Error("set_fire_rate",
                $"multiplier must be > 0 (got {FireRateMultiplier})", "set_fire_rate"));
        }
        var ok = await _dispatcher.SetFireRateAsync(Slot, FireRateMultiplier, ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("set_fire_rate",
                $"slot={Slot} → {FireRateMultiplier:0.00}×", "set_fire_rate")
            : UxFeedback.Error("set_fire_rate", "bridge rejected", "set_fire_rate"));
    }

    public async Task<UxFeedback> SetTargetFilterAsync(CancellationToken ct = default)
    {
        var label = ResolveFilterLabel(TargetFilterBitmask);
        var ok = await _dispatcher.SetTargetFilterAsync(Slot, TargetFilterBitmask, ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("set_target_filter",
                $"slot={Slot} → mask=0x{TargetFilterBitmask:X} ({label})", "set_target_filter")
            : UxFeedback.Error("set_target_filter", "bridge rejected", "set_target_filter"));
    }

    private static string ResolveFilterLabel(int mask)
    {
        var bits = new List<string>();
        if ((mask & 0x1) != 0) bits.Add("ENEMY");
        if ((mask & 0x2) != 0) bits.Add("FRIENDLY");
        if ((mask & 0x4) != 0) bits.Add("NEUTRAL");
        return bits.Count == 0 ? "DISARM" : string.Join("|", bits);
    }

    private async Task<UxFeedback> BoolToggle(
        string featureId,
        bool enable,
        Func<bool, CancellationToken, Task<bool>> action,
        CancellationToken ct)
    {
        return await _toggles.ToggleAsync(featureId, enable,
            action: async cancel =>
            {
                var ok = await action(enable, cancel);
                return ok
                    ? UxFeedback.Success(featureId,
                        enable ? "enabled" : "disabled", featureId)
                    : UxFeedback.Error(featureId, "bridge rejected", featureId);
            },
            disableAction: enable
                ? async cancel =>
                {
                    var ok = await action(false, cancel);
                    return ok
                        ? UxFeedback.Info(featureId, "disabled (cleanup)", featureId)
                        : UxFeedback.Warning(featureId, "cleanup failed", featureId);
                }
        : null,
            cancellationToken: ct);
    }

    private UxFeedback EmitAndReturn(UxFeedback fb)
    {
        _feedback.Emit(fb);
        return fb;
    }
}

/// <summary>
/// Dispatch surface for the combat tab.
/// </summary>
public interface ICombatDispatcher
{
    Task<bool> SetGodModeAsync(bool enable, CancellationToken ct);
    Task<bool> SetOhkAsync(bool enable, CancellationToken ct);
    Task<bool> SetOhkAttackPowerAsync(bool enable, CancellationToken ct);
    Task<bool> SetAreaDamageAsync(bool enable, CancellationToken ct);
    Task<bool> SetDamageMultiplierAsync(int slot, float mult, CancellationToken ct);
    Task<bool> SetUnitShieldAsync(long objAddr, float shieldValue, CancellationToken ct);
    Task<bool> SetFireRateAsync(int slot, float mult, CancellationToken ct);
    Task<bool> SetTargetFilterAsync(int slot, int bitmask, CancellationToken ct);

    // 2026-04-28 (iter 96 + iter 100, master ralph loop): global damage
    // multiplier — LIVE via the Take_Damage_Outer detour. Per-slot stays
    // PHASE 2 PENDING (attacker-context not at Take_Damage layer).
    // Default impls keep older mocks compiling.
    Task<bool> SetDamageMultiplierGlobalAsync(float mult, CancellationToken ct)
        => Task.FromResult(false);
    Task<float> GetDamageMultiplierGlobalAsync(CancellationToken ct)
        => Task.FromResult(1.0f);

    // 2026-05-06 (iter 225/227, master ralph loop A1.3): global fire-rate
    // multiplier — LIVE via the WeaponTick MinHook detour @ 0x387010
    // (closes 124-day-deferred A1.3). Pattern matches iter-96 exactly.
    // Default impls keep older mocks compiling.
    Task<bool> SetFireRateMultiplierGlobalAsync(float mult, CancellationToken ct)
        => Task.FromResult(false);
    Task<float> GetFireRateMultiplierGlobalAsync(CancellationToken ct)
        => Task.FromResult(1.0f);
}
