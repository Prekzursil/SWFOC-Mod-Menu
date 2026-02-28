using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.Meg;

namespace SwfocTrainer.DataIndex.Services;

public sealed class EffectiveGameDataIndexService
{
    private readonly MegaFilesXmlIndexBuilder _megaFilesXmlIndexBuilder;
    private readonly IMegArchiveReader _megArchiveReader;

    public EffectiveGameDataIndexService()
        : this(new MegaFilesXmlIndexBuilder(), new MegArchiveReader())
    {
    }

    public EffectiveGameDataIndexService(
        MegaFilesXmlIndexBuilder megaFilesXmlIndexBuilder,
        IMegArchiveReader megArchiveReader)
    {
        _megaFilesXmlIndexBuilder = megaFilesXmlIndexBuilder;
        _megArchiveReader = megArchiveReader;
    }

    public EffectiveFileMapReport Build(EffectiveGameDataIndexRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProfileId) || string.IsNullOrWhiteSpace(request.GameRootPath))
        {
            return EffectiveFileMapReport.Empty with
            {
                Diagnostics = new[] { "profileId and gameRootPath are required." }
            };
        }

        var diagnostics = new List<string>();
        var records = new List<MutableEffectiveEntry>();
        var activeIndexByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rank = 0;

        // Lowest precedence: enabled MEGs in declared order (last wins among MEGs).
        AddMegEntries(request, diagnostics, records, activeIndexByPath, ref rank);
        // Middle precedence: loose files from game root.
        AddLooseEntries(
            rootPath: request.GameRootPath,
            sourceType: "game_loose",
            diagnostics,
            records,
            activeIndexByPath,
            ref rank);
        // Highest precedence: loose files from MODPATH.
        if (!string.IsNullOrWhiteSpace(request.ModPath))
        {
            AddLooseEntries(
                rootPath: request.ModPath!,
                sourceType: "mod_loose",
                diagnostics,
                records,
                activeIndexByPath,
                ref rank);
        }

        var projected = records
            .OrderBy(x => x.OverrideRank)
            .Select(x => new EffectiveFileMapEntry(
                RelativePath: x.RelativePath,
                SourceType: x.SourceType,
                SourcePath: x.SourcePath,
                OverrideRank: x.OverrideRank,
                Active: x.Active,
                ShadowedBy: x.ShadowedBy))
            .ToArray();

        return new EffectiveFileMapReport(
            ProfileId: request.ProfileId,
            GameRootPath: request.GameRootPath,
            ModPath: request.ModPath,
            Files: projected,
            Diagnostics: diagnostics);
    }

    private void AddMegEntries(
        EffectiveGameDataIndexRequest request,
        ICollection<string> diagnostics,
        IList<MutableEffectiveEntry> records,
        IDictionary<string, int> activeIndexByPath,
        ref int rank)
    {
        var megaFilesXmlPath = Path.Combine(request.GameRootPath, request.MegaFilesXmlRelativePath);
        if (!File.Exists(megaFilesXmlPath))
        {
            diagnostics.Add($"MegaFiles.xml not found at '{megaFilesXmlPath}'.");
            return;
        }

        var megaFilesXml = File.ReadAllText(megaFilesXmlPath);
        var megaIndex = _megaFilesXmlIndexBuilder.Build(megaFilesXml);
        foreach (var diagnostic in megaIndex.Diagnostics)
        {
            diagnostics.Add($"MegaFiles.xml: {diagnostic}");
        }

        foreach (var megaFileName in megaIndex
                     .GetEnabledFilesInLoadOrder()
                     .Select(static megaFile => megaFile.FileName))
        {
            AddMegFileEntries(
                request,
                megaFileName,
                diagnostics,
                records,
                activeIndexByPath,
                ref rank);
        }
    }

    private void AddMegFileEntries(
        EffectiveGameDataIndexRequest request,
        string megaFileName,
        ICollection<string> diagnostics,
        IList<MutableEffectiveEntry> records,
        IDictionary<string, int> activeIndexByPath,
        ref int rank)
    {
        var megaPath = ResolveMegaPath(request.GameRootPath, megaFileName);
        if (megaPath is null)
        {
            diagnostics.Add($"MEG file '{megaFileName}' was not found under game root '{request.GameRootPath}'.");
            return;
        }

        var openResult = _megArchiveReader.Open(megaPath);
        if (!openResult.Succeeded || openResult.Archive is null)
        {
            diagnostics.Add($"MEG parse failed '{megaPath}' reason={openResult.ReasonCode} message={openResult.Message}");
            foreach (var detail in openResult.Diagnostics)
            {
                diagnostics.Add($"MEG parse detail '{megaPath}': {detail}");
            }

            return;
        }

        foreach (var entryPath in openResult.Archive.Entries.Select(static entry => entry.Path))
        {
            AddEntry(
                relativePath: NormalizePath(entryPath),
                sourceType: "meg_entry",
                sourcePath: $"{megaPath}:{entryPath}",
                records,
                activeIndexByPath,
                ref rank);
        }
    }

    private static void AddLooseEntries(
        string rootPath,
        string sourceType,
        ICollection<string> diagnostics,
        IList<MutableEffectiveEntry> records,
        IDictionary<string, int> activeIndexByPath,
        ref int rank)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        if (!Directory.Exists(rootPath))
        {
            diagnostics.Add($"Loose-file root '{rootPath}' does not exist.");
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(rootPath, filePath);
            AddEntry(
                relativePath: NormalizePath(relativePath),
                sourceType: sourceType,
                sourcePath: filePath,
                records,
                activeIndexByPath,
                ref rank);
        }
    }

    private static string? ResolveMegaPath(string gameRootPath, string fileName)
    {
        if (Path.IsPathRooted(fileName))
        {
            return File.Exists(fileName) ? fileName : null;
        }

        var direct = Path.Combine(gameRootPath, fileName);
        if (File.Exists(direct))
        {
            return direct;
        }

        var underData = Path.Combine(gameRootPath, "Data", fileName);
        if (File.Exists(underData))
        {
            return underData;
        }

        return null;
    }

    private static void AddEntry(
        string relativePath,
        string sourceType,
        string sourcePath,
        IList<MutableEffectiveEntry> records,
        IDictionary<string, int> activeIndexByPath,
        ref int rank)
    {
        var normalizedPath = NormalizePath(relativePath);
        var normalizedSourcePath = sourcePath.Replace('\\', '/');
        if (activeIndexByPath.TryGetValue(normalizedPath, out var previousIndex))
        {
            records[previousIndex].Active = false;
            records[previousIndex].ShadowedBy = normalizedSourcePath;
        }

        records.Add(new MutableEffectiveEntry
        {
            RelativePath = normalizedPath,
            SourceType = sourceType,
            SourcePath = normalizedSourcePath,
            OverrideRank = rank++,
            Active = true
        });
        activeIndexByPath[normalizedPath] = records.Count - 1;
    }

    private static string NormalizePath(string value) =>
        value.Replace('\\', '/').TrimStart('/', '.');

    private sealed class MutableEffectiveEntry
    {
        public string RelativePath { get; init; } = string.Empty;

        public string SourceType { get; init; } = string.Empty;

        public string SourcePath { get; init; } = string.Empty;

        public int OverrideRank { get; init; }

        public bool Active { get; set; }

        public string? ShadowedBy { get; set; }
    }
}
