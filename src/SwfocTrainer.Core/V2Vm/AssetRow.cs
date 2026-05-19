namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// 2026-05-07 (iter 321, Asset Browser tab kickoff — closes the iter-313
/// honest defer; last UI consumer surface in the Thread D arc).
///
/// Row model for the Asset Browser tab DataGrid. Unlike iter-308/317/318/319
/// which tied each asset class to a specific tab (Spawning/Galactic/HeroLab/
/// PlayerState), the Asset Browser surfaces ALL extracted assets in one
/// place, classified by the i_*_*.dds filename prefix:
///   - <c>i_button_*.dds</c>   → Category="unit"     (iter-308 convention)
///   - <c>i_portrait_*.dds</c> → Category="hero"     (iter-313 convention)
///   - <c>i_planet_*.dds</c>   → Category="planet"   (iter-315 convention)
///   - <c>i_faction_*.dds</c>  → Category="faction"  (iter-314 convention)
///
/// IconPath is the cached PNG (resolved via iter-307 ThumbnailCache + the
/// appropriate iter-313/314/315 Resolve* method); DdsPath is the source DDS
/// for operator's reference + future export workflow. Both null until the
/// browser walker populates them.
/// </summary>
public sealed record AssetRow(
    string Category,
    string Name,
    string? IconPath,
    string? DdsPath);
