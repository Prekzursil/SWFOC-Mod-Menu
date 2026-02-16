using System.Text.Json;
using System.Text.Json.Serialization;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class SdkCapabilityResolver : ISdkCapabilityResolver
{
    private readonly string _sdkRootPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SdkCapabilityResolver(string? sdkRootPath = null)
    {
        _sdkRootPath = ResolveSdkRootPath(sdkRootPath);
    }

    public SdkCapabilityReport Resolve(TrainerProfile profile, ProcessMetadata process, SymbolMap symbols)
    {
        var operationMap = LoadOperationMap(profile.Id);
        if (operationMap is null)
        {
            return BuildUnavailableReport(profile.Id, process.Mode, "operation_map_missing");
        }

        var operations = new List<SdkOperationCapability>();
        foreach (var operation in operationMap.Operations)
        {
            if (!TryParseOperationId(operation.OperationId, out var operationId))
            {
                continue;
            }

            var requiredSymbols = operation.RequiredSymbols ?? [];
            if (requiredSymbols.Count == 0)
            {
                operations.Add(new SdkOperationCapability(
                    operationId,
                    SdkCapabilityStatus.Unavailable,
                    operation.ReadOnly,
                    ParseRuntimeMode(operation.RequiredMode),
                    "no_required_symbols_configured",
                    new Dictionary<string, object?>
                    {
                        ["requiredSymbols"] = Array.Empty<string>(),
                        ["resolvedSymbols"] = Array.Empty<string>()
                    }));
                continue;
            }

            var resolved = new List<string>();
            var unresolved = new List<string>();
            foreach (var required in requiredSymbols)
            {
                if (symbols.TryGetValue(required, out var symbol) &&
                    symbol is not null &&
                    symbol.Address != nint.Zero &&
                    symbol.HealthStatus != SymbolHealthStatus.Unresolved)
                {
                    resolved.Add(required);
                }
                else
                {
                    unresolved.Add(required);
                }
            }

            var status = resolved.Count == requiredSymbols.Count
                ? SdkCapabilityStatus.Available
                : resolved.Count > 0
                    ? SdkCapabilityStatus.Degraded
                    : SdkCapabilityStatus.Unavailable;
            var reason = status switch
            {
                SdkCapabilityStatus.Available => "anchors_resolved",
                SdkCapabilityStatus.Degraded => "anchors_partial",
                _ => "anchors_missing"
            };

            operations.Add(new SdkOperationCapability(
                operationId,
                status,
                operation.ReadOnly,
                ParseRuntimeMode(operation.RequiredMode),
                reason,
                new Dictionary<string, object?>
                {
                    ["requiredSymbols"] = requiredSymbols,
                    ["resolvedSymbols"] = resolved,
                    ["unresolvedSymbols"] = unresolved,
                    ["validators"] = operation.Validators ?? []
                }));
        }

        foreach (var operationId in Enum.GetValues<SdkOperationId>())
        {
            if (operations.Any(x => x.OperationId == operationId))
            {
                continue;
            }

            operations.Add(new SdkOperationCapability(
                operationId,
                SdkCapabilityStatus.Unavailable,
                ReadOnly: operationId is SdkOperationId.ListNearby or SdkOperationId.ListSelected,
                RequiredMode: RuntimeMode.Unknown,
                ReasonCode: "operation_not_mapped"));
        }

        return new SdkCapabilityReport(profile.Id, process.Mode, operations.OrderBy(x => x.OperationId).ToArray());
    }

    private SdkOperationMapFile? LoadOperationMap(string profileId)
    {
        try
        {
            var path = Path.Combine(_sdkRootPath, profileId, "sdk_operation_map.json");
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SdkOperationMapFile>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static SdkCapabilityReport BuildUnavailableReport(string profileId, RuntimeMode runtimeMode, string reason)
    {
        var operations = Enum
            .GetValues<SdkOperationId>()
            .Select(operationId => new SdkOperationCapability(
                operationId,
                SdkCapabilityStatus.Unavailable,
                ReadOnly: operationId is SdkOperationId.ListNearby or SdkOperationId.ListSelected,
                RequiredMode: RuntimeMode.Unknown,
                ReasonCode: reason))
            .ToArray();
        return new SdkCapabilityReport(profileId, runtimeMode, operations);
    }

    private static RuntimeMode ParseRuntimeMode(string? raw)
    {
        return Enum.TryParse<RuntimeMode>(raw ?? string.Empty, ignoreCase: true, out var mode)
            ? mode
            : RuntimeMode.Unknown;
    }

    private static bool TryParseOperationId(string raw, out SdkOperationId operationId)
    {
        if (Enum.TryParse<SdkOperationId>(raw, ignoreCase: true, out operationId))
        {
            return true;
        }

        var normalized = raw
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToLowerInvariant();
        operationId = normalized switch
        {
            "listselected" => SdkOperationId.ListSelected,
            "listnearby" => SdkOperationId.ListNearby,
            "spawn" => SdkOperationId.Spawn,
            "kill" => SdkOperationId.Kill,
            "setowner" => SdkOperationId.SetOwner,
            "teleport" => SdkOperationId.Teleport,
            "setplanetowner" => SdkOperationId.SetPlanetOwner,
            "sethp" => SdkOperationId.SetHp,
            "setshield" => SdkOperationId.SetShield,
            "setcooldown" => SdkOperationId.SetCooldown,
            _ => default
        };

        return normalized is
            "listselected" or
            "listnearby" or
            "spawn" or
            "kill" or
            "setowner" or
            "teleport" or
            "setplanetowner" or
            "sethp" or
            "setshield" or
            "setcooldown";
    }

    private static string ResolveSdkRootPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        var fromEnv = Environment.GetEnvironmentVariable("SWFOC_SDK_MAP_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv!;
        }

        var baseCandidate = Path.Combine(AppContext.BaseDirectory, "profiles", "default", "sdk");
        if (Directory.Exists(baseCandidate))
        {
            return baseCandidate;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var probe = Path.Combine(current.FullName, "profiles", "default", "sdk");
            if (Directory.Exists(probe))
            {
                return probe;
            }

            current = current.Parent;
        }

        return baseCandidate;
    }

    private sealed class SdkOperationMapFile
    {
        public string SchemaVersion { get; init; } = "1.0";
        public List<SdkOperationDefinition> Operations { get; init; } = [];
    }

    private sealed class SdkOperationDefinition
    {
        public string OperationId { get; init; } = string.Empty;
        public bool ReadOnly { get; init; }
        public string? RequiredMode { get; init; }
        public List<string>? RequiredSymbols { get; init; }
        public List<string>? Validators { get; init; }
        [JsonPropertyName("argumentSchema")]
        public JsonElement ArgumentSchema { get; init; }
    }
}
