using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class CapabilityMapResolver : ICapabilityMapResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _mapsRootPath;
    private readonly ILogger<CapabilityMapResolver> _logger;

    public CapabilityMapResolver(string mapsRootPath, ILogger<CapabilityMapResolver> logger)
    {
        _mapsRootPath = mapsRootPath;
        _logger = logger;
    }

    public Task<CapabilityResolutionResult> ResolveAsync(
        BinaryFingerprint fingerprint,
        string requestedProfileId,
        string operationId,
        IReadOnlySet<string> resolvedAnchors)
    {
        return ResolveAsync(fingerprint, requestedProfileId, operationId, resolvedAnchors, CancellationToken.None);
    }

    public async Task<CapabilityResolutionResult> ResolveAsync(
        BinaryFingerprint fingerprint,
        string requestedProfileId,
        string operationId,
        IReadOnlySet<string> resolvedAnchors,
        CancellationToken cancellationToken)
    {
        var map = await LoadMapAsync(fingerprint, cancellationToken);
        if (map is null)
        {
            return BuildUnavailableResult(
                fingerprint,
                requestedProfileId,
                operationId,
                CapabilityReasonCode.FingerprintMapMissing);
        }

        if (IsRequestedProfileMismatch(requestedProfileId, map.DefaultProfileId))
        {
            return BuildUnavailableResult(
                fingerprint,
                requestedProfileId,
                operationId,
                CapabilityReasonCode.RequestedProfileMismatch);
        }

        if (!map.Operations.TryGetValue(operationId, out var op))
        {
            return BuildUnavailableResult(
                fingerprint,
                requestedProfileId,
                operationId,
                CapabilityReasonCode.OperationNotMapped);
        }

        return ResolveOperationAnchors(fingerprint, requestedProfileId, operationId, op, resolvedAnchors);
    }

    public Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint)
    {
        return ResolveDefaultProfileIdAsync(fingerprint, CancellationToken.None);
    }

    public async Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint, CancellationToken cancellationToken)
    {
        var map = await LoadMapAsync(fingerprint, cancellationToken);
        return map?.DefaultProfileId;
    }

    private async Task<CapabilityMap?> LoadMapAsync(BinaryFingerprint fingerprint, CancellationToken cancellationToken)
    {
        var mapPath = Path.Combine(_mapsRootPath, $"{fingerprint.FingerprintId}.json");
        if (!File.Exists(mapPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(mapPath, cancellationToken);
            return DeserializeCapabilityMap(json, fingerprint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse capability map for fingerprint {FingerprintId}", fingerprint.FingerprintId);
            return null;
        }
    }

    private static CapabilityResolutionResult BuildUnavailableResult(
        BinaryFingerprint fingerprint,
        string requestedProfileId,
        string operationId,
        CapabilityReasonCode reasonCode)
    {
        return new CapabilityResolutionResult(
            requestedProfileId,
            operationId,
            SdkCapabilityStatus.Unavailable,
            reasonCode,
            0.0d,
            fingerprint.FingerprintId,
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    private static bool IsRequestedProfileMismatch(string requestedProfileId, string? defaultProfileId)
    {
        if (string.IsNullOrWhiteSpace(defaultProfileId))
        {
            return false;
        }

        return !string.Equals(requestedProfileId, defaultProfileId, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(requestedProfileId, "universal_auto", StringComparison.OrdinalIgnoreCase);
    }

    private static CapabilityResolutionResult ResolveOperationAnchors(
        BinaryFingerprint fingerprint,
        string requestedProfileId,
        string operationId,
        CapabilityOperationMap operation,
        IReadOnlySet<string> resolvedAnchors)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var matched = operation.RequiredAnchors
            .Where(anchor => ContainsAnchor(resolvedAnchors, anchor, comparer))
            .ToArray();
        var missingRequired = operation.RequiredAnchors
            .Where(anchor => !ContainsAnchor(resolvedAnchors, anchor, comparer))
            .ToArray();
        if (missingRequired.Length > 0)
        {
            return BuildRequiredAnchorsMissingResult(
                requestedProfileId,
                operationId,
                operation,
                fingerprint,
                matched,
                missingRequired);
        }

        var missingOptional = operation.OptionalAnchors
            .Where(anchor => !ContainsAnchor(resolvedAnchors, anchor, comparer))
            .ToArray();
        if (missingOptional.Length > 0)
        {
            return BuildOptionalAnchorsMissingResult(
                requestedProfileId,
                operationId,
                fingerprint.FingerprintId,
                matched,
                missingOptional);
        }

        return BuildAllRequiredAnchorsPresentResult(requestedProfileId, operationId, fingerprint.FingerprintId, matched);
    }

    private static CapabilityResolutionResult BuildRequiredAnchorsMissingResult(
        string requestedProfileId,
        string operationId,
        CapabilityOperationMap operation,
        BinaryFingerprint fingerprint,
        IReadOnlyList<string> matchedAnchors,
        IReadOnlyList<string> missingAnchors)
    {
        return new CapabilityResolutionResult(
            requestedProfileId,
            operationId,
            SdkCapabilityStatus.Degraded,
            CapabilityReasonCode.RequiredAnchorsMissing,
            BuildConfidence(matchedAnchors.Count, operation.RequiredAnchors.Count),
            fingerprint.FingerprintId,
            matchedAnchors,
            missingAnchors);
    }

    private static CapabilityResolutionResult BuildOptionalAnchorsMissingResult(
        string requestedProfileId,
        string operationId,
        string fingerprintId,
        IReadOnlyList<string> matchedAnchors,
        IReadOnlyList<string> missingAnchors)
    {
        return new CapabilityResolutionResult(
            requestedProfileId,
            operationId,
            SdkCapabilityStatus.Degraded,
            CapabilityReasonCode.OptionalAnchorsMissing,
            0.85d,
            fingerprintId,
            matchedAnchors,
            missingAnchors);
    }

    private static CapabilityResolutionResult BuildAllRequiredAnchorsPresentResult(
        string requestedProfileId,
        string operationId,
        string fingerprintId,
        IReadOnlyList<string> matchedAnchors)
    {
        return new CapabilityResolutionResult(
            requestedProfileId,
            operationId,
            SdkCapabilityStatus.Available,
            CapabilityReasonCode.AllRequiredAnchorsPresent,
            1.0d,
            fingerprintId,
            matchedAnchors,
            Array.Empty<string>());
    }

    private static CapabilityMap? DeserializeCapabilityMap(string json, BinaryFingerprint fingerprint)
    {
        var dto = JsonSerializer.Deserialize<CapabilityMapDto>(json, JsonOptions);
        if (dto is null)
        {
            return null;
        }

        var operations = BuildOperations(dto.Operations);
        if (operations.Count == 0)
        {
            operations = BuildOperationsFromCapabilities(dto.Capabilities);
        }

        return new CapabilityMap(
            SchemaVersion: dto.SchemaVersion ?? "1.0",
            FingerprintId: dto.FingerprintId ?? fingerprint.FingerprintId,
            DefaultProfileId: dto.DefaultProfileId,
            GeneratedAtUtc: dto.GeneratedAtUtc == default ? DateTimeOffset.UtcNow : dto.GeneratedAtUtc,
            Operations: operations);
    }

    private static Dictionary<string, CapabilityOperationMap> BuildOperations(
        IReadOnlyDictionary<string, CapabilityOperationDto>? operations)
    {
        if (operations is null || operations.Count == 0)
        {
            return new Dictionary<string, CapabilityOperationMap>(StringComparer.OrdinalIgnoreCase);
        }

        return operations.ToDictionary(
            kv => kv.Key,
            kv => new CapabilityOperationMap(
                kv.Value.RequiredAnchors ?? Array.Empty<string>(),
                kv.Value.OptionalAnchors ?? Array.Empty<string>()),
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, CapabilityOperationMap> BuildOperationsFromCapabilities(
        IReadOnlyList<CapabilityEntryDto>? capabilities)
    {
        if (capabilities is null || capabilities.Count == 0)
        {
            return new Dictionary<string, CapabilityOperationMap>(StringComparer.OrdinalIgnoreCase);
        }

        return capabilities
            .Where(x => !string.IsNullOrWhiteSpace(x.FeatureId))
            .ToDictionary(
                kv => kv.FeatureId!,
                kv => new CapabilityOperationMap(
                    kv.RequiredAnchors ?? Array.Empty<string>(),
                    Array.Empty<string>()),
                StringComparer.OrdinalIgnoreCase);
    }

    private static double BuildConfidence(int matchedRequired, int totalRequired)
    {
        if (totalRequired <= 0)
        {
            return 0.50d;
        }

        return Math.Clamp((double)matchedRequired / totalRequired, 0.0d, 1.0d);
    }

    private static bool ContainsAnchor(IReadOnlySet<string> anchors, string value, StringComparer comparer)
    {
        return anchors.Any(anchor => comparer.Equals(anchor, value));
    }

    private sealed class CapabilityMapDto
    {
        public string? SchemaVersion { get; set; } = string.Empty;

        public string? FingerprintId { get; set; } = string.Empty;

        public string? DefaultProfileId { get; set; } = string.Empty;

        public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public Dictionary<string, CapabilityOperationDto>? Operations { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<CapabilityEntryDto>? Capabilities { get; set; } = new();
    }

    private sealed class CapabilityOperationDto
    {
        public string[]? RequiredAnchors { get; set; } = Array.Empty<string>();

        public string[]? OptionalAnchors { get; set; } = Array.Empty<string>();
    }

    private sealed class CapabilityEntryDto
    {
        public string? FeatureId { get; set; } = string.Empty;

        public string[]? RequiredAnchors { get; set; } = Array.Empty<string>();
    }
}
