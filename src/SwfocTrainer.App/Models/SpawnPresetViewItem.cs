using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.Models;

/// <summary>
/// UI model for profile-scoped spawn presets.
/// </summary>
public sealed record SpawnPresetViewItem(
    string Id,
    string Name,
    string UnitId,
    string Faction,
    string EntryMarker,
    int DefaultQuantity,
    int DefaultDelayMs,
    string Description)
{
    /// <summary>
    /// Converts this UI item into the core spawn preset model.
    /// </summary>
    /// <returns>Equivalent core preset.</returns>
    public SpawnPreset ToCorePreset()
        => new(
            Id,
            Name,
            UnitId,
            Faction,
            EntryMarker,
            DefaultQuantity,
            DefaultDelayMs,
            Description);
}
