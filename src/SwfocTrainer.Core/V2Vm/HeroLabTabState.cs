using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// V2 Tab 7 (Hero Lab). Task #151 — hero roster + respawn timer +
/// permadeath toggle + per-hero kill/revive + stat edits.
/// </summary>
public sealed class HeroLabTabState
{
    private readonly IHeroLabDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;
    private readonly FeatureToggleCoordinator _toggles;
    private List<HeroRow> _heroes = new();

    public HeroLabTabState(IHeroLabDispatcher dispatcher, IUxFeedbackSink feedback,
                            FeatureToggleCoordinator toggles)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(toggles);
        _dispatcher = dispatcher;
        _feedback = feedback;
        _toggles = toggles;
    }

    public IReadOnlyList<HeroRow> Heroes => _heroes;
    public long SelectedHeroAddr { get; set; }
    public int CustomRespawnMs { get; set; } = 5000;
    public string EditField { get; set; } = "hull";
    public float EditValue { get; set; }

    public async Task<UxFeedback> RefreshHeroesAsync(CancellationToken ct = default)
    {
        var heroes = await _dispatcher.ListHeroesAsync(ct);
        _heroes = heroes.ToList();
        return Emit(UxFeedback.Info("list_heroes",
            $"loaded {_heroes.Count} heroes", "list_heroes"));
    }

    public async Task<UxFeedback> SetCustomRespawnAsync(CancellationToken ct = default)
    {
        if (SelectedHeroAddr == 0)
        {
            return Emit(UxFeedback.Error("set_respawn",
                "no hero selected", "set_respawn"));
        }
        if (CustomRespawnMs < 0)
        {
            return Emit(UxFeedback.Error("set_respawn",
                $"respawn ms must be >= 0, got {CustomRespawnMs}", "set_respawn"));
        }
        var ok = await _dispatcher.SetHeroRespawnTimerAsync(
            SelectedHeroAddr, CustomRespawnMs, ct);
        return Emit(ok
            ? UxFeedback.Success("set_respawn",
                $"hero 0x{SelectedHeroAddr:X} respawn → {CustomRespawnMs}ms",
                "set_respawn")
            : UxFeedback.Error("set_respawn", "bridge rejected", "set_respawn"));
    }

    public Task<UxFeedback> TogglePermadeathAsync(bool permadeath, CancellationToken ct = default)
    {
        var key = $"permadeath_0x{SelectedHeroAddr:X}";
        if (SelectedHeroAddr == 0)
        {
            var fb = UxFeedback.Error("set_permadeath", "no hero selected", "set_permadeath");
            _feedback.Emit(fb);
            return Task.FromResult(fb);
        }
        return _toggles.ToggleAsync(key, permadeath,
            action: async cancel =>
            {
                var ok = await _dispatcher.SetPermadeathAsync(SelectedHeroAddr, permadeath, cancel);
                return ok
                    ? UxFeedback.Success("set_permadeath",
                        $"hero 0x{SelectedHeroAddr:X} permadeath = {permadeath}",
                        "set_permadeath")
                    : UxFeedback.Error("set_permadeath", "bridge rejected", "set_permadeath");
            },
            disableAction: permadeath
                ? async cancel =>
                {
                    var ok = await _dispatcher.SetPermadeathAsync(SelectedHeroAddr, false, cancel);
                    return ok
                        ? UxFeedback.Info("set_permadeath",
                            $"hero 0x{SelectedHeroAddr:X} respawn restored (cleanup)",
                            "set_permadeath")
                        : UxFeedback.Warning("set_permadeath", "cleanup-disable failed",
                            "set_permadeath");
                }
        : null,
            cancellationToken: ct);
    }

    public async Task<UxFeedback> KillHeroAsync(CancellationToken ct = default)
    {
        if (SelectedHeroAddr == 0)
        {
            return Emit(UxFeedback.Error("kill_hero", "no hero selected", "kill_hero"));
        }
        var ok = await _dispatcher.KillHeroAsync(SelectedHeroAddr, ct);
        return Emit(ok
            ? UxFeedback.Success("kill_hero", $"hero 0x{SelectedHeroAddr:X} killed", "kill_hero")
            : UxFeedback.Error("kill_hero", "bridge rejected", "kill_hero"));
    }

    public async Task<UxFeedback> ReviveHeroAsync(CancellationToken ct = default)
    {
        if (SelectedHeroAddr == 0)
        {
            return Emit(UxFeedback.Error("revive_hero", "no hero selected", "revive_hero"));
        }
        var ok = await _dispatcher.ReviveHeroAsync(SelectedHeroAddr, ct);
        return Emit(ok
            ? UxFeedback.Success("revive_hero", $"hero 0x{SelectedHeroAddr:X} revived",
                "revive_hero")
            : UxFeedback.Error("revive_hero", "bridge rejected", "revive_hero"));
    }

    /// <summary>
    /// 2026-04-27: revive every hero currently listed in <see cref="Heroes"/>.
    /// Walks the cached list (caller must <see cref="RefreshHeroesAsync"/>
    /// first) and fires <c>SWFOC_ReviveUnit</c> on each one. The composite
    /// rolls up per-hero successes/failures into a single status banner so
    /// the operator doesn't have to dig through 20 individual entries.
    /// </summary>
    public async Task<UxFeedback> ReviveAllHeroesAsync(CancellationToken ct = default)
    {
        if (_heroes.Count == 0)
        {
            return Emit(UxFeedback.Warning("revive_all",
                "no heroes loaded — click Refresh first", "revive_all"));
        }
        var succeeded = 0;
        var failed = 0;
        foreach (var h in _heroes)
        {
            if (h.ObjAddr <= 0) continue;
            ct.ThrowIfCancellationRequested();
            var ok = await _dispatcher.ReviveHeroAsync(h.ObjAddr, ct).ConfigureAwait(false);
            if (ok) succeeded++; else failed++;
        }
        var msg = failed == 0
            ? $"revived {succeeded} hero(es)"
            : $"revived {succeeded}, failed {failed} (bridge rejected one or more)";
        return Emit(failed == 0
            ? UxFeedback.Success("revive_all", msg, "revive_all")
            : UxFeedback.Warning("revive_all", msg, "revive_all"));
    }

    public async Task<UxFeedback> EditStatAsync(CancellationToken ct = default)
    {
        if (SelectedHeroAddr == 0)
        {
            return Emit(UxFeedback.Error("edit_stat", "no hero selected", "edit_stat"));
        }
        if (string.IsNullOrWhiteSpace(EditField))
        {
            return Emit(UxFeedback.Error("edit_stat", "no field name provided", "edit_stat"));
        }
        var ok = await _dispatcher.EditHeroStatAsync(SelectedHeroAddr, EditField, EditValue, ct);
        return Emit(ok
            ? UxFeedback.Success("edit_stat",
                $"hero 0x{SelectedHeroAddr:X} {EditField} = {EditValue}", "edit_stat")
            : UxFeedback.Error("edit_stat", "bridge rejected (unknown field?)", "edit_stat"));
    }

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

public sealed record HeroRow(
    long ObjAddr,
    string TypeName,
    int OwnerSlot,
    bool Alive,
    int RespawnRemainingMs,
    bool RespawnEnabled)
{
    /// <summary>
    /// 2026-04-27: human-readable respawn timer for the DataGrid. The
    /// engine exposes the value as int milliseconds; operators were having
    /// to mentally divide by 1000 every glance. We render "5.0 sec" / "—"
    /// for never-respawn / "0 ms" for instant. The original
    /// <see cref="RespawnRemainingMs"/> column stays available for
    /// power-users via the unit-stat editor + the bridge probe responses.
    /// </summary>
    public string RespawnRemainingDisplay
    {
        get
        {
            if (!RespawnEnabled) return "—";
            if (RespawnRemainingMs <= 0) return "0 ms";
            if (RespawnRemainingMs >= 60_000)
            {
                var minutes = RespawnRemainingMs / 60_000;
                var seconds = (RespawnRemainingMs % 60_000) / 1000;
                return seconds == 0
                    ? $"{minutes} min"
                    : $"{minutes} min {seconds} sec";
            }
            if (RespawnRemainingMs >= 1000)
            {
                var seconds = RespawnRemainingMs / 1000.0;
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.0} sec", seconds);
            }
            return $"{RespawnRemainingMs} ms";
        }
    }
}

public interface IHeroLabDispatcher
{
    Task<IReadOnlyList<HeroRow>> ListHeroesAsync(CancellationToken ct);
    Task<bool> SetHeroRespawnTimerAsync(long addr, int ms, CancellationToken ct);
    Task<bool> SetPermadeathAsync(long addr, bool permadeath, CancellationToken ct);
    Task<bool> KillHeroAsync(long addr, CancellationToken ct);
    Task<bool> ReviveHeroAsync(long addr, CancellationToken ct);
    Task<bool> EditHeroStatAsync(long addr, string field, float value, CancellationToken ct);
}
