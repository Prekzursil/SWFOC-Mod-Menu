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

    public async Task<CapabilityResolutionResult> ResolveAsync(
        BinaryFingerprint fingerprint,
        string requestedProfileId,
        string operationId,
        IReadOnlySet<string> resolvedAnchors,
        CancellationToken cancellationToken = default)
    {
        var map = await LoadMapAsync(fingerprint, cancellationToken);
        if (map is null)
        {
            return new CapabilityResolutionResult(
                requestedProfileId,
                operationId,
                SdkCapabilityStatus.Unavailable,
                CapabilityReasonCode.FingerprintMapMissing,
                0.0d,
                fingerprint.FingerprintId,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        if (!string.IsNullOrWhiteSpace(map.DefaultProfileId) &&
            !string.Equals(requestedProfileId, map.DefaultProfileId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(requestedProfileId, "universal_auto", StringComparison.OrdinalIgnoreCase))
        {
            return new CapabilityResolutionResult(
                requestedProfileId,
                operationId,
                SdkCapabilityStatus.Unavailable,
                CapabilityReasonCode.RequestedProfileMismatch,
                0.0d,
                fingerprint.FingerprintId,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        if (!map.Operations.TryGetValue(operationId, out var op))
        {
            return new CapabilityResolutionResult(
                requestedProfileId,
                operationId,
                SdkCapabilityStatus.Unavailable,
                CapabilityReasonCode.OperationNotMapped,
                0.0d,
                fingerprint.FingerprintId,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        var comparer = StringComparer.OrdinalIgnoreCase;
        var matched = op.RequiredAnchors
            .Where(anchor => ContainsAnchor(resolvedAnchors, anchor, comparer))
            .ToArray();
        var missingRequired = op.RequiredAnchors
            .Where(anchor => !ContainsAnchor(resolvedAnchors, anchor, comparer))
            .ToArray();

        if (missingRequired.Length > 0)
        {
            return new CapabilityResolutionResult(
                requestedProfileId,
                operationId,
                SdkCapabilityStatus.Degraded,
                CapabilityReasonCode.RequiredAnchorsMissing,
                BuildConfidence(matched.Length, op.RequiredAnchors.Count),
                fingerprint.FingerprintId,
                matched,
                missingRequired);
        }

        var missingOptional = op.OptionalAnchors
            .Where(anchor => !ContainsAnchor(resolvedAnchors, anchor, comparer))
            .ToArray();

        if (missingOptional.Length > 0)
        {
            return new CapabilityResolutionResult(
                requestedProfileId,
                operationId,
                SdkCapabilityStatus.Degraded,
                CapabilityReasonCode.OptionalAnchorsMissing,
                0.85d,
                fingerprint.FingerprintId,
                matched,
                missingOptional);
        }

        return new CapabilityResolutionResult(
            requestedProfileId,
            operationId,
            SdkCapabilityStatus.Available,
            CapabilityReasonCode.AllRequiredAnchorsPresent,
            1.0d,
            fingerprint.FingerprintId,
            matched,
            Array.Empty<string>());
    }

    public async Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint, CancellationToken cancellationToken = default)
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
            var dto = JsonSerializer.Deserialize<CapabilityMapDto>(json, JsonOptions);
            if (dto is null)
            {
                return null;
            }

            var operations = dto.Operations?.ToDictionary(
                kv => kv.Key,
                kv => new CapabilityOperationMap(
                    kv.Value.RequiredAnchors ?? Array.Empty<string>(),
                    kv.Value.OptionalAnchors ?? Array.Empty<string>()),
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, CapabilityOperationMap>(StringComparer.OrdinalIgnoreCase);

            return new CapabilityMap(
                SchemaVersion: dto.SchemaVersion ?? "1.0",
                FingerprintId: dto.FingerprintId ?? fingerprint.FingerprintId,
                DefaultProfileId: dto.DefaultProfileId,
                GeneratedAtUtc: dto.GeneratedAtUtc == default ? DateTimeOffset.UtcNow : dto.GeneratedAtUtc,
                Operations: operations);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse capability map for fingerprint {FingerprintId}", fingerprint.FingerprintId);
            return null;
        }
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
        public string? SchemaVersion { get; set; } = null;

        public string? FingerprintId { get; set; } = null;

        public string? DefaultProfileId { get; set; } = null;

        public DateTimeOffset GeneratedAtUtc { get; set; } = default;

        public Dictionary<string, CapabilityOperationDto>? Operations { get; set; } = null;
    }

    private sealed class CapabilityOperationDto
    {
        public string[]? RequiredAnchors { get; set; } = null;

        public string[]? OptionalAnchors { get; set; } = null;
    }
}
