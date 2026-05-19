using System.Text.Json;
using System.Text.Json.Serialization;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// Discovers SWFOC mods installed on the local machine. Surfaces both Steam
/// Workshop subscriptions (under <c>workshop/content/32470/&lt;id&gt;/</c>) and
/// manual installs (under <c>corruption/Mods/&lt;name&gt;/</c>) as a unified
/// list of <see cref="ModInventoryEntry"/> records the UI can render.
/// </summary>
/// <remarks>
/// Two-format reality: Workshop mods carry a JSON metadata file
/// (<c>modinfo.json</c>) with name/version/tags; manual installs commonly do
/// not. The service falls back to the folder name and reports
/// <see cref="ModInventoryEntry.Version"/> as null in that case. The caller
/// (typically the mod-authoring tab) is responsible for hand-off to
/// <c>IModConflictDetectorService</c> if it wants overlap analysis.
/// </remarks>
public sealed class ModInventoryService
{
    internal const string SwfocSteamAppId = "32470";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new TolerantStringConverter() },
    };

    private readonly IModInventoryFileSystem _fs;

    public ModInventoryService() : this(RealModInventoryFileSystem.Instance) { }

    internal ModInventoryService(IModInventoryFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fs = fileSystem;
    }

    /// <summary>
    /// Walk both Workshop and manual mod roots, returning every discovered mod.
    /// </summary>
    /// <param name="workshopContentRoot">
    /// Absolute path to <c>steamapps/workshop/content/32470/</c>. Skipped when
    /// null, empty, or missing.
    /// </param>
    /// <param name="manualModsRoot">
    /// Absolute path to <c>corruption/Mods/</c>. Skipped when null, empty, or
    /// missing.
    /// </param>
    public IReadOnlyList<ModInventoryEntry> Discover(
        string? workshopContentRoot,
        string? manualModsRoot)
    {
        var entries = new List<ModInventoryEntry>();

        if (!string.IsNullOrWhiteSpace(workshopContentRoot) && _fs.DirectoryExists(workshopContentRoot))
        {
            entries.AddRange(ScanWorkshop(workshopContentRoot));
        }

        if (!string.IsNullOrWhiteSpace(manualModsRoot) && _fs.DirectoryExists(manualModsRoot))
        {
            entries.AddRange(ScanManual(manualModsRoot));
        }

        return entries;
    }

    private IEnumerable<ModInventoryEntry> ScanWorkshop(string workshopContentRoot)
    {
        foreach (var subdir in _fs.EnumerateDirectories(workshopContentRoot))
        {
            // Workshop IDs are numeric — skip anything else (Steam sometimes
            // leaves orphan non-numeric folders).
            var folderName = Path.GetFileName(subdir);
            if (!ulong.TryParse(folderName, out _)) continue;

            var modinfoPath = Path.Combine(subdir, "modinfo.json");
            ModInfoDto? modinfo = null;
            if (_fs.FileExists(modinfoPath))
            {
                modinfo = TryReadModInfo(modinfoPath);
            }

            yield return new ModInventoryEntry(
                DisplayName: modinfo?.Name ?? $"Workshop mod {folderName}",
                Version: modinfo?.Version,
                SourceKind: ModInventorySourceKind.Workshop,
                FolderPath: subdir,
                WorkshopId: folderName,
                Tags: modinfo?.SteamData?.Tags ?? Array.Empty<string>(),
                IconRelativePath: modinfo?.Icon);
        }
    }

    private IEnumerable<ModInventoryEntry> ScanManual(string manualModsRoot)
    {
        foreach (var subdir in _fs.EnumerateDirectories(manualModsRoot))
        {
            var folderName = Path.GetFileName(subdir);
            // Try modinfo.json first (some manual mods adopt the Workshop format).
            var modinfoPath = Path.Combine(subdir, "modinfo.json");
            ModInfoDto? modinfo = _fs.FileExists(modinfoPath) ? TryReadModInfo(modinfoPath) : null;

            yield return new ModInventoryEntry(
                DisplayName: modinfo?.Name ?? folderName,
                Version: modinfo?.Version,
                SourceKind: ModInventorySourceKind.Manual,
                FolderPath: subdir,
                WorkshopId: null,
                Tags: modinfo?.SteamData?.Tags ?? Array.Empty<string>(),
                IconRelativePath: modinfo?.Icon);
        }
    }

    private ModInfoDto? TryReadModInfo(string path)
    {
        try
        {
            using var stream = _fs.OpenRead(path);
            return JsonSerializer.Deserialize<ModInfoDto>(stream, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ---- internal DTOs matching the Workshop modinfo.json schema ----

    internal sealed record ModInfoDto(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("icon")] string? Icon,
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("steamdata")] ModInfoSteamDataDto? SteamData);

    internal sealed record ModInfoSteamDataDto(
        [property: JsonPropertyName("publishedfileid")] string? PublishedFileId,
        [property: JsonPropertyName("contentfolder")] string? ContentFolder,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("metadata")] string? Metadata,
        [property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags);

    /// <summary>
    /// Some Workshop mods write "version" as either a string ("3.3") or a
    /// number (3.3). This converter accepts both so we don't crash on a
    /// real-world variation that's not the spec but is common in the wild.
    /// </summary>
    private sealed class TolerantStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.TryGetDouble(out var d) ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                JsonTokenType.True or JsonTokenType.False => reader.GetBoolean().ToString(),
                JsonTokenType.Null => null,
                _ => null,
            };

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value is null) writer.WriteNullValue(); else writer.WriteStringValue(value);
        }
    }
}

/// <summary>Thin filesystem abstraction so the scanner can be unit-tested without a real Steam install.</summary>
public interface IModInventoryFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    IEnumerable<string> EnumerateDirectories(string path);
    Stream OpenRead(string path);
}

internal sealed class RealModInventoryFileSystem : IModInventoryFileSystem
{
    public static readonly RealModInventoryFileSystem Instance = new();

    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path);
    public Stream OpenRead(string path) => File.OpenRead(path);
}
