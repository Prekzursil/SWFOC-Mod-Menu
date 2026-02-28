namespace SwfocTrainer.DataIndex.Models;

public sealed record MegaFileEntry(
    string FileName,
    int LoadOrder,
    bool Enabled,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record MegaFilesIndex(
    IReadOnlyList<MegaFileEntry> Files,
    IReadOnlyList<string> Diagnostics)
{
    public static readonly MegaFilesIndex Empty = new(
        Array.Empty<MegaFileEntry>(),
        Array.Empty<string>());

    public IReadOnlyList<MegaFileEntry> GetEnabledFilesInLoadOrder()
    {
        return Files
            .Where(file => file.Enabled)
            .OrderBy(file => file.LoadOrder)
            .ToArray();
    }
}

public sealed record EffectiveGameDataIndexRequest(
    string ProfileId,
    string GameRootPath,
    string? ModPath = null,
    string MegaFilesXmlRelativePath = @"Data\MegaFiles.xml");

public sealed record EffectiveFileMapEntry(
    string RelativePath,
    string SourceType,
    string SourcePath,
    int OverrideRank,
    bool Active,
    string? ShadowedBy);

public sealed record EffectiveFileMapReport(
    string ProfileId,
    string GameRootPath,
    string? ModPath,
    IReadOnlyList<EffectiveFileMapEntry> Files,
    IReadOnlyList<string> Diagnostics)
{
    public static readonly EffectiveFileMapReport Empty = new(
        ProfileId: string.Empty,
        GameRootPath: string.Empty,
        ModPath: null,
        Files: Array.Empty<EffectiveFileMapEntry>(),
        Diagnostics: Array.Empty<string>());
}
