using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SwfocTrainer.Core.Models;

public sealed record SignatureSpec(
    string Name,
    string Pattern,
    int Offset,
    SignatureAddressMode AddressMode = SignatureAddressMode.HitPlusOffset,
    string Module = "",
    SymbolValueType ValueType = SymbolValueType.Int32);

public sealed record SignatureSet(
    string Name,
    string GameBuild,
    IReadOnlyList<SignatureSpec> Signatures);

public sealed record HelperHookSpec(
    string Id,
    string Script,
    string Version,
    string? EntryPoint = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record CatalogSource(
    string Type,
    string Path,
    bool Required = true,
    string? Description = null);

public sealed record TrainerProfile(
    string Id,
    string DisplayName,
    string? Inherits,
    ExeTarget ExeTarget,
    string? SteamWorkshopId,
    IReadOnlyList<SignatureSet> SignatureSets,
    IReadOnlyDictionary<string, long> FallbackOffsets,
    IReadOnlyDictionary<string, ActionSpec> Actions,
    IReadOnlyDictionary<string, bool> FeatureFlags,
    IReadOnlyList<CatalogSource> CatalogSources,
    string SaveSchemaId,
    IReadOnlyList<HelperHookSpec> HelperModHooks,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string BackendPreference = "auto",
    IReadOnlyList<string>? RequiredCapabilities = null,
    string HostPreference = "starwarsg_preferred",
    IReadOnlyList<string>? ExperimentalFeatures = null);

public sealed record ProfileBuild(
    string ProfileId,
    string GameBuild,
    string ExecutablePath,
    ExeTarget ExeTarget,
    string? ProcessCommandLine = null,
    int ProcessId = 0);

public sealed record SymbolValidationRule(
    string Symbol,
    RuntimeMode? Mode = null,
    long? IntMin = null,
    long? IntMax = null,
    double? FloatMin = null,
    double? FloatMax = null,
    bool Critical = false);

public sealed record SymbolInfo(
    string Name,
    nint Address,
    SymbolValueType ValueType,
    AddressSource Source,
    string? Diagnostics = null,
    double Confidence = 0.50d,
    SymbolHealthStatus HealthStatus = SymbolHealthStatus.Healthy,
    string? HealthReason = null,
    DateTimeOffset? LastValidatedAt = null);

public sealed record SymbolMap(IReadOnlyDictionary<string, SymbolInfo> Symbols)
{
    public bool TryGetValue(string symbol, out SymbolInfo? info)
    {
        if (Symbols.TryGetValue(symbol, out var value))
        {
            info = value;
            return true;
        }

        info = null;
        return false;
    }
}

public sealed record ProfileManifestEntry(
    string Id,
    string Version,
    string Sha256,
    string DownloadUrl,
    string MinAppVersion,
    string? Description = null);

public sealed record ProfileManifest(
    string Version,
    DateTimeOffset PublishedAt,
    IReadOnlyList<ProfileManifestEntry> Profiles);

public static class JsonProfileSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static JsonObject ToJsonObject<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, Options) as JsonObject;
        return node ?? new JsonObject();
    }
}
