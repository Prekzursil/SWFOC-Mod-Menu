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
        ISdkDiagnosticsSink sdkDiagnosticsSink)
    {
        _sdkRuntimeAdapter = sdkRuntimeAdapter;
        _profileVariantResolver = profileVariantResolver;
        _binaryFingerprintService = binaryFingerprintService;
        _capabilityMapResolver = capabilityMapResolver;
        _sdkExecutionGuard = sdkExecutionGuard;
        _sdkDiagnosticsSink = sdkDiagnosticsSink;
    }

    public SdkOperationRouter(
        ISdkRuntimeAdapter sdkRuntimeAdapter,
        IProfileVariantResolver profileVariantResolver,
        IBinaryFingerprintService binaryFingerprintService,
        ICapabilityMapResolver capabilityMapResolver,
        ISdkExecutionGuard sdkExecutionGuard)
        : this(
            sdkRuntimeAdapter,
            profileVariantResolver,
            binaryFingerprintService,
            capabilityMapResolver,
            sdkExecutionGuard,
            new NullSdkDiagnosticsSink())
    {
    }

    public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request)
    {
        return ExecuteAsync(request, CancellationToken.None);
    }

    public async Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request, CancellationToken cancellationToken)
    {
        if (!IsSdkFeatureGateEnabled())
        {
            return await WriteAndReturnAsync(request, CreateFeatureGateDisabledResult(), cancellationToken);
        }

        var processPath = ReadContextString(request.Context, "processPath");
        var processId = ReadContextInt(request.Context, "processId");
        var runtimeDetached = CreateRuntimeDetachedResult(processPath);
        if (runtimeDetached is not null)
        {
            return await WriteAndReturnAsync(request, runtimeDetached, cancellationToken);
        }

        if (!SdkOperationCatalog.TryGet(request.OperationId, out var operationDefinition))
        {
            return await WriteAndReturnAsync(request, CreateUnknownOperationResult(request.OperationId), cancellationToken);
        }

        var modeMismatch = CreateModeMismatchResult(request, operationDefinition);
        if (modeMismatch is not null)
        {
            return await WriteAndReturnAsync(request, modeMismatch, cancellationToken);
        }

        var (variant, capability) = await ResolveVariantAndCapabilityAsync(
            request,
            processPath!,
            processId,
            cancellationToken);

        var decision = _sdkExecutionGuard.CanExecute(capability, operationDefinition.IsMutation);
        if (!decision.Allowed)
        {
            var blocked = CreateBlockedResult(decision, capability, variant);
            return await WriteAndReturnAsync(request, blocked, cancellationToken);
        }

        return await ExecuteRoutedAsync(
            request,
            operationDefinition,
            capability,
            variant,
            cancellationToken);
    }

    private async Task<(ProfileVariantResolution Variant, CapabilityResolutionResult Capability)> ResolveVariantAndCapabilityAsync(
        SdkOperationRequest request,
        string processPath,
        int? processId,
        CancellationToken cancellationToken)
    {
        var variant = await _profileVariantResolver.ResolveAsync(request.ProfileId, cancellationToken);
        var fingerprint = await CaptureFingerprintAsync(processPath, processId, cancellationToken);
        var anchors = ExtractResolvedAnchors(request.Context);
        var capability = await _capabilityMapResolver.ResolveAsync(
            fingerprint,
            variant.ResolvedProfileId,
            request.OperationId,
            anchors,
            cancellationToken);
        return (variant, capability);
    }

    private async Task<SdkOperationResult> WriteAndReturnAsync(
        SdkOperationRequest request,
        SdkOperationResult result,
        CancellationToken cancellationToken)
    {
        await _sdkDiagnosticsSink.WriteAsync(request, result, cancellationToken);
        return result;
    }

    private async Task<SdkOperationResult> ExecuteRoutedAsync(
        SdkOperationRequest request,
        SdkOperationDefinition operationDefinition,
        CapabilityResolutionResult capability,
        ProfileVariantResolution variant,
        CancellationToken cancellationToken)
    {
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

    private static bool IsSdkFeatureGateEnabled()
    {
        var gate = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        return string.Equals(gate, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static SdkOperationResult CreateFeatureGateDisabledResult()
    {
        return new SdkOperationResult(
            false,
            "SDK path disabled. Set SWFOC_EXPERIMENTAL_SDK=1 to enable R&D runtime routing.",
            CapabilityReasonCode.FeatureFlagDisabled,
            SdkCapabilityStatus.Unavailable);
    }

    private static SdkOperationResult? CreateRuntimeDetachedResult(string? processPath)
    {
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        return new SdkOperationResult(
            false,
            "Runtime session context is missing (processPath unavailable).",
            CapabilityReasonCode.RuntimeNotAttached,
            SdkCapabilityStatus.Unavailable);
    }

    private static SdkOperationResult CreateUnknownOperationResult(string operationId)
    {
        return new SdkOperationResult(
            false,
            $"SDK operation '{operationId}' is not part of the v1 catalog.",
            CapabilityReasonCode.UnknownSdkOperation,
            SdkCapabilityStatus.Unavailable);
    }

    private static SdkOperationResult? CreateModeMismatchResult(
        SdkOperationRequest request,
        SdkOperationDefinition operationDefinition)
    {
        if (operationDefinition.IsModeAllowed(request.RuntimeMode))
        {
            return null;
        }

        var allowedModes = FormatAllowedModes(operationDefinition.AllowedModes);
        return new SdkOperationResult(
            false,
            $"SDK operation '{request.OperationId}' is blocked in mode '{request.RuntimeMode}'. Allowed modes: {allowedModes}.",
            CapabilityReasonCode.ModeMismatch,
            SdkCapabilityStatus.Unavailable,
            new Dictionary<string, object?>
            {
                ["runtimeMode"] = request.RuntimeMode.ToString(),
                ["allowedModes"] = allowedModes
            });
    }

    private static string FormatAllowedModes(IReadOnlySet<RuntimeMode> allowedModes)
    {
        if (allowedModes.Count == 0)
        {
            return "any";
        }

        return string.Join(",", allowedModes.OrderBy(mode => mode.ToString()).Select(mode => mode.ToString()));
    }

    private async Task<BinaryFingerprint> CaptureFingerprintAsync(
        string processPath,
        int? processId,
        CancellationToken cancellationToken)
    {
        if (!processId.HasValue)
        {
            return await _binaryFingerprintService.CaptureFromPathAsync(processPath, cancellationToken);
        }

        return await _binaryFingerprintService.CaptureFromPathAsync(processPath, processId.Value, cancellationToken);
    }

    private static SdkOperationResult CreateBlockedResult(
        SdkExecutionDecision decision,
        CapabilityResolutionResult capability,
        ProfileVariantResolution variant)
    {
        return new SdkOperationResult(
            false,
            decision.Message,
            decision.ReasonCode,
            capability.State,
            new Dictionary<string, object?>
            {
                ["resolvedVariant"] = variant.ResolvedProfileId,
                ["fingerprintId"] = capability.FingerprintId,
                ["capabilityReasonCode"] = capability.ReasonCode.ToString(),
                ["capabilityMapReasonCode"] = capability.Metadata.SourceReasonCode,
                ["capabilityMapState"] = capability.Metadata.SourceState,
                ["capabilityDeclaredAvailable"] = capability.Metadata.DeclaredAvailable
            });
    }

    private static IReadOnlySet<string> ExtractResolvedAnchors(IReadOnlyDictionary<string, object?>? context)
    {
        if (!TryReadContextValue(context, "resolvedAnchors", out var raw) || raw is null)
        {
            return CreateEmptyAnchorSet();
        }

        if (raw is IEnumerable<string> strings)
        {
            return CreateAnchorSet(strings);
        }

        if (raw is JsonArray jsonArray)
        {
            return CreateAnchorSet(jsonArray.Select(node => node?.GetValue<string>()));
        }

        if (raw is string serialized)
        {
            return DeserializeAnchorSet(serialized);
        }

        return CreateEmptyAnchorSet();
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
        merged["capabilityMapReasonCode"] = capability.Metadata.SourceReasonCode;
        merged["capabilityMapState"] = capability.Metadata.SourceState;
        merged["capabilityDeclaredAvailable"] = capability.Metadata.DeclaredAvailable;

        return merged;
    }

    private static string? ReadContextString(IReadOnlyDictionary<string, object?>? context, string key)
    {
        if (!TryReadContextValue(context, key, out var value) || value is null)
        {
            return null;
        }

        return value as string ?? value.ToString();
    }

    private static int? ReadContextInt(IReadOnlyDictionary<string, object?>? context, string key)
    {
        if (!TryReadContextValue(context, key, out var value) || value is null)
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

    private static bool TryReadContextValue(
        IReadOnlyDictionary<string, object?>? context,
        string key,
        out object? value)
    {
        value = null;
        if (context is null)
        {
            return false;
        }

        if (!context.TryGetValue(key, out var raw))
        {
            return false;
        }

        value = raw;
        return true;
    }

    private static IReadOnlySet<string> CreateAnchorSet(IEnumerable<string?> rawValues)
    {
        return new HashSet<string>(
            rawValues
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> DeserializeAnchorSet(string serialized)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(serialized) ?? Array.Empty<string>();
            return CreateAnchorSet(values);
        }
        catch
        {
            return CreateEmptyAnchorSet();
        }
    }

    private static IReadOnlySet<string> CreateEmptyAnchorSet()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
