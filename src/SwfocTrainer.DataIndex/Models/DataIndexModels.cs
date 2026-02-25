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
