namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// 2026-05-07 (iter 318, second UI consumer of iter-313 ResolvePortrait —
/// follows iter-317 Galactic planet-icon column pattern verbatim) — row
/// model used by the Hero Lab tab DataGrid to render hero rows with
/// optional in-game hero portrait thumbnails.
///
/// Mirrors the iter-308 <see cref="UnitTypeRow"/> + iter-317
/// <see cref="PlanetRowWithIcon"/> shape: original HeroRow's 6 fields
/// (ObjAddr/TypeName/OwnerSlot/Alive/RespawnRemainingMs/RespawnEnabled)
/// plus IconPath. The split keeps icon lookup orthogonal to
/// <see cref="HeroRow"/> + <c>HeroLabTabState</c> (which stay icon-unaware) —
/// the HeroLab VM produces a parallel UI-only projection populated alongside
/// <c>Heroes</c> on every refresh.
///
/// Resolver default size 64 (iter-313 ResolvePortrait) renders portraits
/// at hero-photo scale, larger than the iter-308 unit icon (32) but
/// smaller than the iter-317 planet icon (96) — matches typical
/// hero-roster card UX.
///
/// When IconPath is null (no resolver wired OR operator hasn't extracted
/// the hero portrait DDS yet) the WPF Image control bound to it stays
/// hidden via standard null-binding behavior — operator sees the hero
/// type with no portrait, not a broken-image placeholder.
/// </summary>
public sealed record HeroRowWithPortrait(
    long ObjAddr,
    string TypeName,
    int OwnerSlot,
    bool Alive,
    int RespawnRemainingMs,
    bool RespawnEnabled,
    string? IconPath)
{
    /// <summary>
    /// 2026-05-07 (iter 318): mirror of <see cref="HeroRow.RespawnRemainingDisplay"/>
    /// (em-dash for disabled, ms below 1s, "X.X sec" below 60s, "N min M sec"
    /// otherwise). Existing Hero Lab DataGrid binds to this property name —
    /// when iter-318 flips ItemsSource from Heroes to HeroRowsWithPortrait,
    /// the binding still resolves cleanly to the same operator-visible string.
    /// LOGIC PINNED — keep in sync with HeroRow.RespawnRemainingDisplay (Iter318
    /// HeroRowWithPortrait pin tests catch any drift in either direction).
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
                return seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " sec";
            }
            return $"{RespawnRemainingMs} ms";
        }
    }
}
