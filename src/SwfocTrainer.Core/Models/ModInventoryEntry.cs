namespace SwfocTrainer.Core.Models;

/// <summary>
/// One mod discovered on disk — either a Steam Workshop install or a manual
/// Mods/&lt;name&gt; folder. Surfaced by <see cref="Services.ModInventoryService"/>
/// for the mod-authoring tab introduced in v1.1.
/// </summary>
public sealed record ModInventoryEntry(
    string DisplayName,
    string? Version,
    ModInventorySourceKind SourceKind,
    string FolderPath,
    string? WorkshopId,
    IReadOnlyList<string> Tags,
    string? IconRelativePath);

/// <summary>Provenance bucket for <see cref="ModInventoryEntry"/>.</summary>
public enum ModInventorySourceKind
{
    /// <summary>
    /// Discovered under <c>steamapps/workshop/content/32470/&lt;id&gt;/</c>
    /// with a parseable <c>modinfo.json</c>.
    /// </summary>
    Workshop,

    /// <summary>
    /// Discovered under <c>corruption/Mods/&lt;name&gt;/</c>. May or may not have
    /// a metadata file; <see cref="ModInventoryEntry.DisplayName"/> falls back to
    /// the folder name when no metadata is present.
    /// </summary>
    Manual,
}
