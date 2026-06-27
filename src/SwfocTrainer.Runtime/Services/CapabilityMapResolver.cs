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
        ArgumentNullException.ThrowIfNull(mapsRootPath);
        ArgumentNullException.ThrowIfNull(logger);
        _mapsRootPath = mapsRootPath;
        _logger = logger;
    }

    public Task<CapabilityResolutionResult> ResolveAsync(
        BinaryFingerprint fingerprint,
        string requestedProfileId,
        string operationId,
        IReadOnlySet<string> resolvedAnchors)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(requestedProfileId);
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(resolvedAnchors);
        return ResolveAsync(fingerprint, requestedProfileId, operationId, resolvedAnchors, CancellationToken.None);
    }

    public async Task<CapabilityResolutionResult> ResolveAsync(
        BinaryFingerprint fingerprint,
        string requestedProfileId,
        string operationId,
        IReadOnlySet<string> resolvedAnchors,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(requestedProfileId);
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(resolvedAnchors);
        var map = await LoadMapAsync(fingerprint, cancellationToken);
        if (map is null)
        {
            return BuildUnavailableResult(
                fingerprint,
                requestedProfileId,
                operationId,
                CapabilityReasonCode.FingerprintMapMissing);
        }

        var preconditionFailure = TryResolveOperationPreconditions(
            fingerprint,
            requestedProfileId,
            operationId,
            map,
            out var operation);
        if (preconditionFailure is not null)
        {
            return preconditionFailure;
        }

        if (TryResolveDeclaredUnavailable(
                map,
                requestedProfileId,
                operationId,
                fingerprint.FingerprintId,
                out var declaredUnavailable))
        {
            return declaredUnavailable;
        }

        return ResolveOperationAnchors(new AnchorResolutionInput(
            fingerprint,
            requestedProfileId,
            operationId,
            operation,
            resolvedAnchors,
            ResolveCapabilityHint(map, operationId)));
    }

    public Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        return ResolveDefaultProfileIdAsync(fingerprint, CancellationToken.None);
    }

    public async Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        var map = await LoadMapAsync(fingerprint, cancellationToken);
        return map?.DefaultProfileId;
    }

    private async Task<CapabilityMap?> LoadMapAsync(BinaryFingerprint fingerprint, CancellationToken cancellationToken)
    {
        var mapPath = Path.Join(_mapsRootPath, $"{fingerprint.FingerprintId}.json");
        if (!File.Exists(mapPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(mapPath, cancellationToken);
            return DeserializeCapabilityMap(json, fingerprint);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to parse capability map for fingerprint {FingerprintId}", fingerprint.FingerprintId);
            return null;
        }
        catch (System.Text.Json.JsonException ex)
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
            Array.Empty<string>(),
            CapabilityResolutionMetadata.Empty);
    }

    private static CapabilityResolutionResult? TryResolveOperationPreconditions(
        BinaryFingerprint fingerprint,
        string requestedProfileId,
        string operationId,
        CapabilityMap map,
        out CapabilityOperationMap operation)
    {
        operation = default!;
        if (IsRequestedProfileMismatch(requestedProfileId, map.DefaultProfileId))
        {
            return BuildUnavailableResult(
                fingerprint,
                requestedProfileId,
                operationId,
                CapabilityReasonCode.RequestedProfileMismatch);
        }

        if (!map.Operations.TryGetValue(operationId, out var resolvedOperation) || resolvedOperation is null)
        {
            return BuildUnavailableResult(
                fingerprint,
                requestedProfileId,
                operationId,
                CapabilityReasonCode.OperationNotMapped);
        }

        operation = resolvedOperation;
        return null;
    }

    private static bool IsRequestedProfileMismatch(string requestedProfileId, string? defaultProfileId)
    {
        if (string.IsNullOrWhiteSpace(defaultProfileId))
        {
            return false;
        }

        if (IsGeneratedCustomProfileCompatible(requestedProfileId, defaultProfileId))
        {
            return false;
        }

        return !string.Equals(requestedProfileId, defaultProfileId, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(requestedProfileId, "universal_auto", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedCustomProfileCompatible(string requestedProfileId, string defaultProfileId)
    {
        if (string.IsNullOrWhiteSpace(requestedProfileId) ||
            !requestedProfileId.StartsWith("custom_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (requestedProfileId.EndsWith("_swfoc", StringComparison.OrdinalIgnoreCase) &&
            defaultProfileId.EndsWith("_swfoc", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (requestedProfileId.EndsWith("_sweaw", StringComparison.OrdinalIgnoreCase) &&
            defaultProfileId.EndsWith("_sweaw", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private readonly record struct AnchorResolutionInput(
        BinaryFingerprint Fingerprint,
        string RequestedProfileId,
        string OperationId,
        CapabilityOperationMap Operation,
        IReadOnlySet<string> ResolvedAnchors,
        CapabilityAvailabilityHint? CapabilityHint);

    private static CapabilityResolutionResult ResolveOperationAnchors(AnchorResolutionInput input)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var matched = input.Operation.RequiredAnchors
            .Where(anchor => ContainsAnchor(input.ResolvedAnchors, anchor, comparer))
            .ToArray();
        var missingRequired = input.Operation.RequiredAnchors
            .Where(anchor => !ContainsAnchor(input.ResolvedAnchors, anchor, comparer))
            .ToArray();
        if (missingRequired.Length > 0)
        {
            var requiredCtx = new AnchorValidationContext(
                input.RequestedProfileId, input.OperationId, matched, missingRequired, input.CapabilityHint);
            return BuildRequiredAnchorsMissingResult(requiredCtx, input.Operation, input.Fingerprint);
        }

        var missingOptional = input.Operation.OptionalAnchors
            .Where(anchor => !ContainsAnchor(input.ResolvedAnchors, anchor, comparer))
            .ToArray();
        if (missingOptional.Length > 0)
        {
            var optionalCtx = new AnchorValidationContext(
                input.RequestedProfileId, input.OperationId, matched, missingOptional, input.CapabilityHint);
            return BuildOptionalAnchorsMissingResult(optionalCtx, input.Fingerprint.FingerprintId);
        }

        return BuildAllRequiredAnchorsPresentResult(
            input.RequestedProfileId,
            input.OperationId,
            input.Fingerprint.FingerprintId,
            matched,
            input.CapabilityHint);
    }

    private static CapabilityResolutionResult BuildRequiredAnchorsMissingResult(
        AnchorValidationContext ctx,
        CapabilityOperationMap operation,
        BinaryFingerprint fingerprint)
    {
        return new CapabilityResolutionResult(
            ctx.RequestedProfileId,
            ctx.OperationId,
            SdkCapabilityStatus.Degraded,
            CapabilityReasonCode.RequiredAnchorsMissing,
            BuildConfidence(ctx.MatchedAnchors.Count, operation.RequiredAnchors.Count),
            fingerprint.FingerprintId,
            ctx.MatchedAnchors,
            ctx.MissingAnchors,
            ResolveCapabilityMetadata(ctx.CapabilityHint));
    }

    private static CapabilityResolutionResult BuildOptionalAnchorsMissingResult(
        AnchorValidationContext ctx,
        string fingerprintId)
    {
        return new CapabilityResolutionResult(
            ctx.RequestedProfileId,
            ctx.OperationId,
            SdkCapabilityStatus.Degraded,
            CapabilityReasonCode.OptionalAnchorsMissing,
            0.85d,
            fingerprintId,
            ctx.MatchedAnchors,
            ctx.MissingAnchors,
            ResolveCapabilityMetadata(ctx.CapabilityHint));
    }

    private static CapabilityResolutionResult BuildAllRequiredAnchorsPresentResult(
        string requestedProfileId,
        string operationId,
        string fingerprintId,
        IReadOnlyList<string> matchedAnchors,
        CapabilityAvailabilityHint? capabilityHint)
    {
        return new CapabilityResolutionResult(
            requestedProfileId,
            operationId,
            SdkCapabilityStatus.Available,
            CapabilityReasonCode.AllRequiredAnchorsPresent,
            1.0d,
            fingerprintId,
            matchedAnchors,
            Array.Empty<string>(),
            ResolveCapabilityMetadata(capabilityHint));
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

        var capabilityHints = BuildCapabilityHints(dto.Capabilities);

        return new CapabilityMap(
            SchemaVersion: dto.SchemaVersion ?? "1.0",
            FingerprintId: dto.FingerprintId ?? fingerprint.FingerprintId,
            DefaultProfileId: dto.DefaultProfileId,
            GeneratedAtUtc: dto.GeneratedAtUtc == default ? DateTimeOffset.UtcNow : dto.GeneratedAtUtc,
            Operations: operations,
            CapabilityHints: capabilityHints);
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

    private static Dictionary<string, CapabilityAvailabilityHint> BuildCapabilityHints(
        IReadOnlyList<CapabilityEntryDto>? capabilities)
    {
        if (capabilities is null || capabilities.Count == 0)
        {
            return new Dictionary<string, CapabilityAvailabilityHint>(StringComparer.OrdinalIgnoreCase);
        }

        return capabilities
            .Where(x => !string.IsNullOrWhiteSpace(x.FeatureId))
            .ToDictionary(
                x => x.FeatureId!,
                x => new CapabilityAvailabilityHint(
                    FeatureId: x.FeatureId!,
                    Available: x.Available,
                    State: x.State ?? string.Empty,
                    ReasonCode: x.ReasonCode ?? string.Empty,
                    RequiredAnchors: x.RequiredAnchors ?? Array.Empty<string>()),
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

    private static bool TryResolveDeclaredUnavailable(
        CapabilityMap map,
        string requestedProfileId,
        string operationId,
        string fingerprintId,
        out CapabilityResolutionResult result)
    {
        result = default!;
        if (!map.CapabilityHints.TryGetValue(operationId, out var hint) || hint.Available)
        {
            return false;
        }

        var resolvedReason = MapExternalReasonCode(hint.ReasonCode);
        var requiredAnchors = hint.RequiredAnchors;
        result = new CapabilityResolutionResult(
            requestedProfileId,
            operationId,
            SdkCapabilityStatus.Unavailable,
            resolvedReason,
            0.0d,
            fingerprintId,
            Array.Empty<string>(),
            requiredAnchors,
            ResolveCapabilityMetadata(hint));
        return true;
    }

    private static CapabilityAvailabilityHint? ResolveCapabilityHint(CapabilityMap map, string operationId)
    {
        return map.CapabilityHints.TryGetValue(operationId, out var hint) ? hint : null;
    }

    private static CapabilityResolutionMetadata ResolveCapabilityMetadata(CapabilityAvailabilityHint? hint)
    {
        if (hint is null)
        {
            return CapabilityResolutionMetadata.Empty;
        }

        return new CapabilityResolutionMetadata(
            SourceReasonCode: hint.ReasonCode,
            SourceState: hint.State,
            DeclaredAvailable: hint.Available);
    }

    private static CapabilityReasonCode MapExternalReasonCode(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return CapabilityReasonCode.Unknown;
        }

        return reasonCode.ToUpperInvariant() switch
        {
            "CAPABILITY_REQUIRED_MISSING" => CapabilityReasonCode.RequiredAnchorsMissing,
            "CAPABILITY_PROBE_PASS" => CapabilityReasonCode.AllRequiredAnchorsPresent,
            "CAPABILITY_ANCHOR_INVALID" => CapabilityReasonCode.RequiredAnchorsMissing,
            "CAPABILITY_ANCHOR_UNREADABLE" => CapabilityReasonCode.RequiredAnchorsMissing,
            "CAPABILITY_BACKEND_UNAVAILABLE" => CapabilityReasonCode.RuntimeNotAttached,
            "SAFETY_FAIL_CLOSED" => CapabilityReasonCode.MutationBlockedByCapabilityState,
            _ => CapabilityReasonCode.Unknown
        };
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

    private sealed record AnchorValidationContext(
        string RequestedProfileId,
        string OperationId,
        IReadOnlyList<string> MatchedAnchors,
        IReadOnlyList<string> MissingAnchors,
        CapabilityAvailabilityHint? CapabilityHint);


    private sealed class CapabilityEntryDto
    {
        public string? FeatureId { get; set; } = string.Empty;

        public bool Available { get; set; } = true;

        public string? State { get; set; } = string.Empty;

        public string? ReasonCode { get; set; } = string.Empty;

        public string[]? RequiredAnchors { get; set; } = Array.Empty<string>();
    }
}
