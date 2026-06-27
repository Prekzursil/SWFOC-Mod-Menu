using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// V2 Tab 3 (Speed). Task #147 — global game-speed slider, per-faction
/// move-speed multiplier, selected-unit individual speed.
/// </summary>
public sealed class SpeedTabState
{
    private readonly ISpeedDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;

    public SpeedTabState(ISpeedDispatcher dispatcher, IUxFeedbackSink feedback)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        _dispatcher = dispatcher;
        _feedback = feedback;
    }

    public float GlobalGameSpeed { get; set; } = 1.0f;
    public int FactionSlot { get; set; } = -1;
    public float FactionMoveSpeedMultiplier { get; set; } = 1.0f;
    public long SelectedObjAddr { get; set; }
    public float UnitSpeed { get; set; } = 5.0f;

    public async Task<UxFeedback> SetGlobalGameSpeedAsync(CancellationToken ct = default)
    {
        if (GlobalGameSpeed < 0)
        {
            return Emit(UxFeedback.Error("set_game_speed",
                $"speed must be >= 0, got {GlobalGameSpeed}", "set_game_speed"));
        }
        var ok = await _dispatcher.SetGameSpeedAsync(GlobalGameSpeed, ct);
        return Emit(ok
            ? UxFeedback.Success("set_game_speed",
                GlobalGameSpeed == 0 ? "paused (0×)" : $"{GlobalGameSpeed:0.00}×",
                "set_game_speed")
            : UxFeedback.Error("set_game_speed", "bridge rejected", "set_game_speed"));
    }

    public async Task<UxFeedback> SetFactionMoveSpeedAsync(CancellationToken ct = default)
    {
        if (FactionMoveSpeedMultiplier < 0)
        {
            return Emit(UxFeedback.Error("set_faction_speed",
                $"multiplier must be >= 0, got {FactionMoveSpeedMultiplier}",
                "set_faction_speed"));
        }
        var ok = await _dispatcher.SetFactionSpeedMultiplierAsync(
            FactionSlot, FactionMoveSpeedMultiplier, ct);
        return Emit(ok
            ? UxFeedback.Success("set_faction_speed",
                $"slot={FactionSlot} → {FactionMoveSpeedMultiplier:0.00}×",
                "set_faction_speed")
            : UxFeedback.Error("set_faction_speed", "bridge rejected", "set_faction_speed"));
    }

    public async Task<UxFeedback> SetUnitSpeedAsync(CancellationToken ct = default)
    {
        if (SelectedObjAddr == 0)
        {
            return Emit(UxFeedback.Error("set_unit_speed", "no unit selected",
                "set_unit_speed"));
        }
        if (UnitSpeed < 0)
        {
            return Emit(UxFeedback.Error("set_unit_speed",
                $"speed must be >= 0, got {UnitSpeed}", "set_unit_speed"));
        }
        var ok = await _dispatcher.SetUnitSpeedAsync(SelectedObjAddr, UnitSpeed, ct);
        return Emit(ok
            ? UxFeedback.Success("set_unit_speed",
                $"unit 0x{SelectedObjAddr:X} → {UnitSpeed:0.00}", "set_unit_speed")
            : UxFeedback.Error("set_unit_speed", "bridge rejected", "set_unit_speed"));
    }

    /// <summary>
    /// 2026-04-28 (iter 100): revert per-unit speed override. Calls the
    /// engine's ClearSpeedOverride helper at RVA 0x38F8B0 — the locomotor
    /// active-flag at +0x29C clears and the engine re-applies the unit's
    /// natural max speed. Pairs with <see cref="SetUnitSpeedAsync"/>.
    /// </summary>
    public async Task<UxFeedback> ClearUnitSpeedOverrideAsync(CancellationToken ct = default)
    {
        if (SelectedObjAddr == 0)
        {
            return Emit(UxFeedback.Error("clear_unit_speed_override",
                "no unit selected", "clear_unit_speed_override"));
        }
        var ok = await _dispatcher.ClearUnitSpeedOverrideAsync(SelectedObjAddr, ct);
        return Emit(ok
            ? UxFeedback.Success("clear_unit_speed_override",
                $"unit 0x{SelectedObjAddr:X} reverted to natural max",
                "clear_unit_speed_override")
            : UxFeedback.Error("clear_unit_speed_override",
                "bridge rejected", "clear_unit_speed_override"));
    }

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

public interface ISpeedDispatcher
{
    Task<bool> SetGameSpeedAsync(float speed, CancellationToken ct);
    Task<bool> SetFactionSpeedMultiplierAsync(int slot, float mult, CancellationToken ct);
    Task<bool> SetUnitSpeedAsync(long objAddr, float speed, CancellationToken ct);

    // 2026-04-28 (iter 100, master ralph loop): revert helper. The bridge
    // calls ClearSpeedOverride @ RVA 0x38F8B0, which clears the active
    // flag at locomotor +0x29C and lets the engine apply natural max.
    // Returning false on bridge ERR; default impl helps older mocks.
    Task<bool> ClearUnitSpeedOverrideAsync(long objAddr, CancellationToken ct)
        => Task.FromResult(false);
}
