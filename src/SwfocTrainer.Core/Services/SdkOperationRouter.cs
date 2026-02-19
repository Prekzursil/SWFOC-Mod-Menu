using System.Text.Json;
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class SdkOperationRouter : ISdkOperationRouter
{
    private readonly ISdkRuntimeAdapter _sdkRuntimeAdapter;
    private readonly IProfileVariantResolver _profileVariantResolver;
    private readonly IBinaryFingerprintService _binaryFingerprintService;
    private readonly ICapabilityMapResolver _capabilityMapResolver;
    private readonly ISdkExecutionGuard _sdkExecutionGuard;
    private readonly ISdkDiagnosticsSink _sdkDiagnosticsSink;

    public SdkOperationRouter(
        ISdkRuntimeAdapter sdkRuntimeAdapter,
        IProfileVariantResolver profileVariantResolver,
        IBinaryFingerprintService binaryFingerprintService,
        ICapabilityMapResolver capabilityMapResolver,
        ISdkExecutionGuard sdkExecutionGuard,
        ISdkDiagnosticsSink? sdkDiagnosticsSink = null)
    {
        _sdkRuntimeAdapter = sdkRuntimeAdapter;
        _profileVariantResolver = profileVariantResolver;
        _binaryFingerprintService = binaryFingerprintService;
        _capabilityMapResolver = capabilityMapResolver;
        _sdkExecutionGuard = sdkExecutionGuard;
        _sdkDiagnosticsSink = sdkDiagnosticsSink ?? new NullSdkDiagnosticsSink();
    }

    public async Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request, CancellationToken cancellationToken = default)
    {
        var gate = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        if (!string.Equals(gate, "1", StringComparison.OrdinalIgnoreCase))
        {
            var disabled = new SdkOperationResult(
                false,
                "SDK path disabled. Set SWFOC_EXPERIMENTAL_SDK=1 to enable R&D runtime routing.",
                CapabilityReasonCode.FeatureFlagDisabled,
                SdkCapabilityStatus.Unavailable);
            await _sdkDiagnosticsSink.WriteAsync(request, disabled, cancellationToken);
            return disabled;
        }

        var processPath = ReadContextString(request.Context, "processPath");
        var processId = ReadContextInt(request.Context, "processId");
        if (string.IsNullOrWhiteSpace(processPath))
        {
            var detached = new SdkOperationResult(
                false,
                "Runtime session context is missing (processPath unavailable).",
                CapabilityReasonCode.RuntimeNotAttached,
                SdkCapabilityStatus.Unavailable);
            await _sdkDiagnosticsSink.WriteAsync(request, detached, cancellationToken);
            return detached;
        }

        if (!SdkOperationCatalog.TryGet(request.OperationId, out var operationDefinition))
        {
            var unknownOperation = new SdkOperationResult(
                false,
                $"SDK operation '{request.OperationId}' is not part of the v1 catalog.",
                CapabilityReasonCode.UnknownSdkOperation,
                SdkCapabilityStatus.Unavailable);
            await _sdkDiagnosticsSink.WriteAsync(request, unknownOperation, cancellationToken);
            return unknownOperation;
        }

        if (!operationDefinition.IsModeAllowed(request.RuntimeMode))
        {
            var allowedModes = operationDefinition.AllowedModes.Count == 0
                ? "any"
                : string.Join(",", operationDefinition.AllowedModes.OrderBy(x => x.ToString()).Select(x => x.ToString()));
            var modeMismatch = new SdkOperationResult(
                false,
                $"SDK operation '{request.OperationId}' is blocked in mode '{request.RuntimeMode}'. Allowed modes: {allowedModes}.",
                CapabilityReasonCode.ModeMismatch,
                SdkCapabilityStatus.Unavailable,
                new Dictionary<string, object?>
                {
                    ["runtimeMode"] = request.RuntimeMode.ToString(),
                    ["allowedModes"] = allowedModes
                });
            await _sdkDiagnosticsSink.WriteAsync(request, modeMismatch, cancellationToken);
            return modeMismatch;
        }

        var variant = await _profileVariantResolver.ResolveAsync(request.ProfileId, cancellationToken: cancellationToken);
        var fingerprint = await _binaryFingerprintService.CaptureFromPathAsync(processPath, processId, cancellationToken);

        var anchors = ExtractResolvedAnchors(request.Context);
        var capability = await _capabilityMapResolver.ResolveAsync(
            fingerprint,
            variant.ResolvedProfileId,
            request.OperationId,
            anchors,
            cancellationToken);

        var decision = _sdkExecutionGuard.CanExecute(capability, operationDefinition.IsMutation);
        if (!decision.Allowed)
        {
            var blocked = new SdkOperationResult(
                false,
                decision.Message,
                decision.ReasonCode,
                capability.State,
                new Dictionary<string, object?>
                {
                    ["resolvedVariant"] = variant.ResolvedProfileId,
                    ["fingerprintId"] = capability.FingerprintId,
                    ["capabilityReasonCode"] = capability.ReasonCode.ToString()
                });
            await _sdkDiagnosticsSink.WriteAsync(request, blocked, cancellationToken);
            return blocked;
        }

        var routedRequest = request with
        {
            ProfileId = variant.ResolvedProfileId,
            IsMutation = operationDefinition.IsMutation,
            Context = MergeContext(request.Context, capability, variant)
        };

        var result = await _sdkRuntimeAdapter.ExecuteAsync(routedRequest, cancellationToken);
        await _sdkDiagnosticsSink.WriteAsync(routedRequest, result, cancellationToken);
        return result;
    }

    private static IReadOnlySet<string> ExtractResolvedAnchors(IReadOnlyDictionary<string, object?>? context)
    {
        if (context is null || !context.TryGetValue("resolvedAnchors", out var raw) || raw is null)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        if (raw is IEnumerable<string> strings)
        {
            return new HashSet<string>(strings.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        }

        if (raw is JsonArray jsonArray)
        {
            var values = jsonArray
                .Select(x => x?.GetValue<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x!)
                .ToArray();
            return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
        }

        if (raw is string serialized)
        {
            try
            {
                var values = JsonSerializer.Deserialize<string[]>(serialized) ?? Array.Empty<string>();
                return new HashSet<string>(values.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, object?> MergeContext(
        IReadOnlyDictionary<string, object?>? original,
        CapabilityResolutionResult capability,
        ProfileVariantResolution variant)
    {
        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (original is not null)
        {
            foreach (var kv in original)
            {
                merged[kv.Key] = kv.Value;
            }
        }

        merged["resolvedVariant"] = variant.ResolvedProfileId;
        merged["variantReasonCode"] = variant.ReasonCode;
        merged["variantConfidence"] = variant.Confidence;
        merged["fingerprintId"] = capability.FingerprintId;
        merged["capabilityState"] = capability.State.ToString();
        merged["capabilityReasonCode"] = capability.ReasonCode.ToString();

        return merged;
    }

    private static string? ReadContextString(IReadOnlyDictionary<string, object?>? context, string key)
    {
        if (context is null || !context.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value as string ?? value.ToString();
    }

    private static int? ReadContextInt(IReadOnlyDictionary<string, object?>? context, string key)
    {
        if (context is null || !context.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is int i)
        {
            return i;
        }

        if (value is long l && l <= int.MaxValue && l >= int.MinValue)
        {
            return (int)l;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }
}
