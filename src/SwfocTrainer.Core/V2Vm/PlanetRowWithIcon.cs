namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// 2026-05-07 (iter 317, first UI consumer of iter-315 ResolvePlanetIcon) —
/// row model used by the Galactic tab DataGrid to render planet rows with
/// optional in-game planet icon thumbnails.
///
/// Mirrors the iter-308 <see cref="UnitTypeRow"/> shape: TypeId/IconPath
/// becomes PlanetId/OwnerFaction/TechLevel/IconPath. The split keeps icon
/// lookup orthogonal to <see cref="PlanetRow"/> + <c>GalacticTabState</c>
/// (which stay icon-unaware) — the Galactic VM produces a parallel UI-only
/// projection populated alongside <c>Planets</c> on every refresh.
///
/// When IconPath is null (no resolver wired OR operator hasn't extracted
/// the planet DDS yet) the WPF Image control bound to it stays hidden via
/// standard null-binding behavior — operator sees the planet name with no
/// icon, not a broken-image placeholder.
/// </summary>
public sealed record PlanetRowWithIcon(
    string PlanetId,
    string OwnerFaction,
    int TechLevel,
    string? IconPath);
