using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class BackendRouter : IBackendRouter
{
    public BackendRouteDecision Resolve(
        ActionExecutionRequest request,
        TrainerProfile profile,
        ProcessMetadata process,
        CapabilityReport capabilityReport)
    {
        var defaultBackend = MapDefaultBackend(request.Action.ExecutionKind);
        var preferredBackend = ResolvePreferredBackend(profile.BackendPreference, defaultBackend);
        var isMutating = IsMutating(request.Action.Id);
        var profileRequiredCapabilities = profile.RequiredCapabilities ?? Array.Empty<string>();
        var requiredCapabilities = ResolveRequiredCapabilitiesForAction(profileRequiredCapabilities, request.Action.Id);
        var missingRequired = requiredCapabilities
            .Where(featureId => !capabilityReport.IsFeatureAvailable(featureId))
            .ToArray();
        var diagnostics = BuildDiagnostics(
            request,
            profile,
            process,
            capabilityReport,
            defaultBackend,
            preferredBackend,
            profileRequiredCapabilities,
            requiredCapabilities,
            missingRequired);

        var requiredCapabilityDecision = TryResolveRequiredCapabilityContract(
            preferredBackend,
            profile.BackendPreference,
            isMutating,
            missingRequired,
            diagnostics);
        if (requiredCapabilityDecision is not null)
        {
            return requiredCapabilityDecision;
        }

        if (preferredBackend != ExecutionBackendKind.Extender)
        {
            return CreateRoutedDecision(preferredBackend, diagnostics);
        }

        return ResolveExtenderRoute(request, profile, capabilityReport, isMutating, diagnostics);
    }

    private static Dictionary<string, object?> BuildDiagnostics(
        ActionExecutionRequest request,
        TrainerProfile profile,
        ProcessMetadata process,
        CapabilityReport capabilityReport,
        ExecutionBackendKind defaultBackend,
        ExecutionBackendKind preferredBackend,
        IReadOnlyList<string> profileRequiredCapabilities,
        IReadOnlyList<string> requiredCapabilities,
        IReadOnlyList<string> missingRequired)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["requestedExecutionKind"] = request.Action.ExecutionKind.ToString(),
            ["defaultBackend"] = defaultBackend.ToString(),
            ["preferredBackend"] = preferredBackend.ToString(),
            ["profileBackendPreference"] = profile.BackendPreference,
            ["hostRole"] = process.HostRole.ToString(),
            ["probeReasonCode"] = capabilityReport.ProbeReasonCode.ToString(),
            ["profileRequiredCapabilities"] = profileRequiredCapabilities,
            ["requiredCapabilities"] = requiredCapabilities,
            ["missingRequiredCapabilities"] = missingRequired
        };
    }

    private static BackendRouteDecision? TryResolveRequiredCapabilityContract(
        ExecutionBackendKind preferredBackend,
        string? backendPreference,
        bool isMutating,
        IReadOnlyCollection<string> missingRequired,
        IReadOnlyDictionary<string, object?> diagnostics)
    {
        var enforceCapabilityContract = preferredBackend == ExecutionBackendKind.Extender ||
                                        IsHardExtenderPreference(backendPreference);
        if (missingRequired.Count == 0 || !isMutating || !enforceCapabilityContract)
        {
            return null;
        }

        return new BackendRouteDecision(
            Allowed: false,
            Backend: preferredBackend,
            ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
            Message: $"Blocked by required capability contract: {string.Join(", ", missingRequired)}.",
            Diagnostics: diagnostics);
    }

    private static BackendRouteDecision CreateRoutedDecision(
        ExecutionBackendKind backend,
        IReadOnlyDictionary<string, object?> diagnostics)
    {
        return new BackendRouteDecision(
            Allowed: true,
            Backend: backend,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Message: $"Routed to backend '{backend}'.",
            Diagnostics: diagnostics);
    }

    private static BackendRouteDecision ResolveExtenderRoute(
        ActionExecutionRequest request,
        TrainerProfile profile,
        CapabilityReport capabilityReport,
        bool isMutating,
        Dictionary<string, object?> diagnostics)
    {
        if (capabilityReport.IsFeatureAvailable(request.Action.Id))
        {
            return new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Extender,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "Routed to extender backend.",
                Diagnostics: diagnostics);
        }

        diagnostics["requestedFeatureId"] = request.Action.Id;
        if (!isMutating)
        {
            return CreateExperimentalReadOnlyDecision(diagnostics);
        }

        return ResolveMutatingExtenderRoute(profile.BackendPreference, request.Action.ExecutionKind, diagnostics);
    }

    private static BackendRouteDecision CreateExperimentalReadOnlyDecision(IReadOnlyDictionary<string, object?> diagnostics)
    {
        return new BackendRouteDecision(
            Allowed: true,
            Backend: ExecutionBackendKind.Extender,
            ReasonCode: RuntimeReasonCode.CAPABILITY_FEATURE_EXPERIMENTAL,
            Message: "Read-only operation allowed with experimental extender capability state.",
            Diagnostics: diagnostics);
    }

    private static BackendRouteDecision ResolveMutatingExtenderRoute(
        string? backendPreference,
        ExecutionKind executionKind,
        Dictionary<string, object?> diagnostics)
    {
        if (IsHardExtenderPreference(backendPreference))
        {
            return new BackendRouteDecision(
                Allowed: false,
                Backend: ExecutionBackendKind.Extender,
                ReasonCode: RuntimeReasonCode.SAFETY_FAIL_CLOSED,
                Message: "Extender capability is not proven for this mutating operation (fail-closed).",
                Diagnostics: diagnostics);
        }

        var fallback = ResolveAutoFallbackBackend(executionKind);
        if (fallback == ExecutionBackendKind.Unknown)
        {
            return new BackendRouteDecision(
                Allowed: false,
                Backend: ExecutionBackendKind.Extender,
                ReasonCode: RuntimeReasonCode.SAFETY_MUTATION_BLOCKED,
                Message: "No safe fallback backend is available for this mutating operation. Ensure SwfocExtender host bridge is running for extender-routed actions.",
                Diagnostics: diagnostics);
        }

        diagnostics["fallbackBackend"] = fallback.ToString();
        return new BackendRouteDecision(
            Allowed: true,
            Backend: fallback,
            ReasonCode: RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE,
            Message: $"Extender capability unavailable; routed to fallback backend '{fallback}'.",
            Diagnostics: diagnostics);
    }

    private static bool IsHardExtenderPreference(string? backendPreference)
    {
        return string.Equals(backendPreference, "extender", StringComparison.OrdinalIgnoreCase);
    }

    private static ExecutionBackendKind ResolvePreferredBackend(string? backendPreference, ExecutionBackendKind defaultBackend)
    {
        if (string.Equals(backendPreference, "extender", StringComparison.OrdinalIgnoreCase))
        {
            return ExecutionBackendKind.Extender;
        }

        if (string.Equals(backendPreference, "helper", StringComparison.OrdinalIgnoreCase))
        {
            return defaultBackend == ExecutionBackendKind.Save
                ? ExecutionBackendKind.Save
                : ExecutionBackendKind.Helper;
        }

        if (string.Equals(backendPreference, "memory", StringComparison.OrdinalIgnoreCase))
        {
            return defaultBackend == ExecutionBackendKind.Save
                ? ExecutionBackendKind.Save
                : ExecutionBackendKind.Memory;
        }

        // auto preference keeps execution-kind intent, except SDK which defaults to extender.
        return defaultBackend;
    }

    private static ExecutionBackendKind ResolveAutoFallbackBackend(ExecutionKind executionKind)
    {
        return executionKind switch
        {
            ExecutionKind.Helper => ExecutionBackendKind.Helper,
            ExecutionKind.Memory => ExecutionBackendKind.Memory,
            ExecutionKind.CodePatch => ExecutionBackendKind.Memory,
            ExecutionKind.Freeze => ExecutionBackendKind.Memory,
            ExecutionKind.Save => ExecutionBackendKind.Save,
            _ => ExecutionBackendKind.Unknown
        };
    }

    private static ExecutionBackendKind MapDefaultBackend(ExecutionKind executionKind)
    {
        return executionKind switch
        {
            ExecutionKind.Helper => ExecutionBackendKind.Helper,
            ExecutionKind.Save => ExecutionBackendKind.Save,
            ExecutionKind.Memory => ExecutionBackendKind.Memory,
            ExecutionKind.CodePatch => ExecutionBackendKind.Memory,
            ExecutionKind.Freeze => ExecutionBackendKind.Memory,
            ExecutionKind.Sdk => ExecutionBackendKind.Extender,
            _ => ExecutionBackendKind.Memory
        };
    }

    private static bool IsMutating(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return true;
        }

        return !(actionId.StartsWith("read_", StringComparison.OrdinalIgnoreCase) ||
                 actionId.StartsWith("list_", StringComparison.OrdinalIgnoreCase) ||
                 actionId.StartsWith("get_", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] ResolveRequiredCapabilitiesForAction(
        IReadOnlyList<string> profileRequiredCapabilities,
        string actionId)
    {
        if (profileRequiredCapabilities.Count == 0 || string.IsNullOrWhiteSpace(actionId))
        {
            return Array.Empty<string>();
        }

        return profileRequiredCapabilities
            .Where(featureId => featureId.Equals(actionId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
