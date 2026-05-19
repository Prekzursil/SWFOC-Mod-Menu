using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// Thin filesystem abstraction used by <see cref="ModConflictDetectorService"/>
/// so that IOException and null-root paths can be exercised in unit tests.
/// </summary>
internal interface IModFileSystem
{
    bool DirectoryExists(string path);
    string[] GetXmlFiles(string directoryPath);
    XDocument LoadXml(string filePath);
}

/// <summary>Default implementation that delegates to <see cref="Directory"/> and <see cref="XDocument"/>.</summary>
internal sealed class RealModFileSystem : IModFileSystem
{
    public static readonly RealModFileSystem Instance = new();

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string[] GetXmlFiles(string directoryPath) =>
        Directory.GetFiles(directoryPath, "*.xml", SearchOption.AllDirectories);

    public XDocument LoadXml(string filePath) => XDocument.Load(filePath);
}

public sealed class ModConflictDetectorService : IModConflictDetectorService
{
    private readonly ILogger<ModConflictDetectorService> _logger;
    private readonly IModFileSystem _fs;

    public ModConflictDetectorService(ILogger<ModConflictDetectorService> logger)
        : this(logger, RealModFileSystem.Instance)
    {
    }

    internal ModConflictDetectorService(
        ILogger<ModConflictDetectorService> logger,
        IModFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _logger = logger;
        _fs = fileSystem;
    }

    /// <summary>
    /// Builds a human-readable summary of detected mod conflicts.
    /// </summary>
    internal static string BuildConflictReportSummary(IReadOnlyList<ModConflictEntry> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        if (conflicts.Count == 0) return "No conflicts detected.";
        return $"{conflicts.Count} conflict(s): {string.Join(", ", conflicts.Select(c => c.EntityId).Distinct().Take(5))}";
    }

    public Task<IReadOnlyList<ModConflictEntry>> DetectConflictsAsync(
        IReadOnlyList<string> modPaths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modPaths);

        if (modPaths.Count < 2)
        {
            _logger.LogInformation(
                "Fewer than 2 mod paths provided ({Count}); no conflicts possible",
                modPaths.Count);

            return Task.FromResult<IReadOnlyList<ModConflictEntry>>(
                Array.Empty<ModConflictEntry>());
        }

        var entityMaps = new List<(string ModSource, Dictionary<string, string> Entities)>();

        foreach (var modPath in modPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(modPath))
            {
                continue;
            }

            var entities = ScanModPath(modPath);
            if (entities.Count > 0)
            {
                entityMaps.Add((modPath, entities));
            }
        }

        var conflicts = new List<ModConflictEntry>();

        for (var i = 0; i < entityMaps.Count; i++)
        {
            for (var j = i + 1; j < entityMaps.Count; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pairConflicts = DetectDuplicateEntities(
                    entityMaps[i].Entities,
                    entityMaps[j].Entities,
                    entityMaps[i].ModSource,
                    entityMaps[j].ModSource);

                conflicts.AddRange(pairConflicts);
            }
        }

        _logger.LogInformation(
            "Conflict scan complete: {ConflictCount} conflicts across {ModCount} mods",
            conflicts.Count,
            entityMaps.Count);

        return Task.FromResult<IReadOnlyList<ModConflictEntry>>(conflicts.AsReadOnly());
    }

    internal static IReadOnlyList<ModConflictEntry> DetectDuplicateEntities(
        IReadOnlyDictionary<string, string> entities1,
        IReadOnlyDictionary<string, string> entities2,
        string source1,
        string source2)
    {
        ArgumentNullException.ThrowIfNull(entities1);
        ArgumentNullException.ThrowIfNull(entities2);
        ArgumentNullException.ThrowIfNull(source1);
        ArgumentNullException.ThrowIfNull(source2);

        var conflicts = new List<ModConflictEntry>();

        foreach (var (entityId, file1) in entities1)
        {
            if (entities2.TryGetValue(entityId, out var file2))
            {
                conflicts.Add(new ModConflictEntry(
                    EntityId: entityId,
                    ModSource1: source1,
                    ModSource2: source2,
                    ConflictType: "duplicate_entity",
                    Details: $"Entity '{entityId}' defined in '{file1}' and '{file2}'"));
            }
        }

        return conflicts;
    }

    private Dictionary<string, string> ScanModPath(string modPath)
    {
        var entities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!_fs.DirectoryExists(modPath))
        {
            _logger.LogWarning("Mod path does not exist: {Path}", modPath);
            return entities;
        }

        string[] xmlFiles;
        try
        {
            xmlFiles = _fs.GetXmlFiles(modPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to enumerate XML files in mod path {Path}",
                modPath);
            return entities;
        }

        foreach (var xmlFile in xmlFiles)
        {
            ExtractEntityNames(xmlFile, entities);
        }

        return entities;
    }

    private void ExtractEntityNames(
        string xmlFilePath,
        Dictionary<string, string> entities)
    {
        try
        {
            var doc = _fs.LoadXml(xmlFilePath);
            var root = doc.Root;
            if (root is null)
            {
                return;
            }

            var relativePath = Path.GetFileName(xmlFilePath);

            foreach (var element in root.Descendants())
            {
                var nameAttr = element.Attribute("Name")
                    ?? element.Attribute("name");

                if (nameAttr is not null
                    && !string.IsNullOrWhiteSpace(nameAttr.Value))
                {
                    entities.TryAdd(nameAttr.Value, relativePath);
                }
            }
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse XML file {File}",
                xmlFilePath);
        }
    }
}
