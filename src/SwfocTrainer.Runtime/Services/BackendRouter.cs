using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class BackendRouter : IBackendRouter
{
    private static readonly HashSet<string> PromotedExtenderActionIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "freeze_timer",
        "toggle_fog_reveal",
        "toggle_ai",
        "set_unit_cap",
        "toggle_instant_build_patch"
    };

    public BackendRouteDecision Resolve(
        ActionExecutionRequest request,
        TrainerProfile profile,
        ProcessMetadata process,
        CapabilityReport capabilityReport)
    {
        var state = CreateRouteResolutionState(request, profile, process, capabilityReport);

        var requiredCapabilityDecision = TryResolveRequiredCapabilityContract(
            state.PreferredBackend,
            profile.BackendPreference,
            state.MutatingAction,
            state.MissingRequired,
            state.Diagnostics,
            state.PromotedExtenderAction);
        if (requiredCapabilityDecision is not null)
        {
            return requiredCapabilityDecision;
        }

        var promotedGateDecision = TryResolvePromotedCapabilityVerificationContract(
            request.Action.Id,
            state.PreferredBackend,
            capabilityReport,
            state.Diagnostics,
            state.PromotedExtenderAction);
        if (promotedGateDecision is not null)
        {
            return promotedGateDecision;
        }

        if (state.PreferredBackend != ExecutionBackendKind.Extender)
        {
            return CreateRoutedDecision(state.PreferredBackend, state.Diagnostics);
        }

        return ResolveExtenderRoute(request, profile, capabilityReport, state.MutatingAction, state.Diagnostics);
    }

    private static RouteResolutionState CreateRouteResolutionState(
        ActionExecutionRequest request,
        TrainerProfile profile,
        ProcessMetadata process,
        CapabilityReport capabilityReport)
    {
        var isPromotedExtenderAction = IsPromotedExtenderAction(request.Action.Id, request.Action.ExecutionKind, profile, process);
        var defaultBackend = MapDefaultBackend(request.Action.ExecutionKind, isPromotedExtenderAction);
        var preferredBackend = ResolvePreferredBackend(profile.BackendPreference, defaultBackend, isPromotedExtenderAction);
        var isMutating = IsMutating(request.Action.Id);
        var profileRequiredCapabilities = profile.RequiredCapabilities ?? Array.Empty<string>();
        var requiredCapabilities = ResolveRequiredCapabilitiesForAction(
            profileRequiredCapabilities,
            request.Action.Id,
            isPromotedExtenderAction);
        var missingRequired = requiredCapabilities
            .Where(featureId => !capabilityReport.IsFeatureAvailable(featureId))
            .ToArray();
        var diagnostics = BuildDiagnostics(new BackendDiagnosticsContext(
            request,
            profile,
            process,
            capabilityReport,
            defaultBackend,
            preferredBackend,
            profileRequiredCapabilities,
            requiredCapabilities,
            missingRequired,
            isPromotedExtenderAction));
        return new RouteResolutionState(
            PreferredBackend: preferredBackend,
            MutatingAction: isMutating,
            MissingRequired: missingRequired,
            Diagnostics: diagnostics,
            PromotedExtenderAction: isPromotedExtenderAction);
    }

    private static Dictionary<string, object?> BuildDiagnostics(BackendDiagnosticsContext context)
    {
        var capabilityMapReasonCode = ReadContextString(context.Request.Context, "capabilityMapReasonCode");
        var capabilityMapState = ReadContextString(context.Request.Context, "capabilityMapState");
        var capabilityDeclaredAvailable = ReadContextBool(context.Request.Context, "capabilityDeclaredAvailable");
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["requestedExecutionKind"] = context.Request.Action.ExecutionKind.ToString(),
            ["defaultBackend"] = context.DefaultBackend.ToString(),
            ["preferredBackend"] = context.PreferredBackend.ToString(),
            ["profileBackendPreference"] = context.Profile.BackendPreference,
            ["hostRole"] = context.Process.HostRole.ToString(),
            ["probeReasonCode"] = context.CapabilityReport.ProbeReasonCode.ToString(),
            ["profileRequiredCapabilities"] = context.ProfileRequiredCapabilities,
            ["requiredCapabilities"] = context.RequiredCapabilities,
            ["missingRequiredCapabilities"] = context.MissingRequired,
            ["hybridExecution"] = false,
            ["promotedExtenderAction"] = context.PromotedExtenderAction,
            ["capabilityMapReasonCode"] = capabilityMapReasonCode ?? string.Empty,
            ["capabilityMapState"] = capabilityMapState ?? string.Empty,
            ["capabilityDeclaredAvailable"] = capabilityDeclaredAvailable
        };
    }

    private static string? ReadContextString(IReadOnlyDictionary<string, object?>? context, string key)
    {
        if (context is null || !context.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw as string ?? raw.ToString();
    }

    private static bool? ReadContextBool(IReadOnlyDictionary<string, object?>? context, string key)
    {
        if (context is null || !context.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is bool boolValue)
        {
            return boolValue;
        }

        return bool.TryParse(raw.ToString(), out var parsed) ? parsed : null;
    }

    private static BackendRouteDecision? TryResolveRequiredCapabilityContract(
        ExecutionBackendKind preferredBackend,
        string? backendPreference,
        bool isMutating,
        IReadOnlyCollection<string> missingRequired,
        IReadOnlyDictionary<string, object?> diagnostics,
        bool isPromotedExtenderAction)
    {
        var enforceCapabilityContract = preferredBackend == ExecutionBackendKind.Extender ||
                                        IsHardExtenderPreference(backendPreference) ||
                                        isPromotedExtenderAction;
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

    private static BackendRouteDecision? TryResolvePromotedCapabilityVerificationContract(
        string actionId,
        ExecutionBackendKind preferredBackend,
        CapabilityReport capabilityReport,
        Dictionary<string, object?> diagnostics,
        bool isPromotedExtenderAction)
    {
        if (!isPromotedExtenderAction || preferredBackend != ExecutionBackendKind.Extender)
        {
            return null;
        }

        if (!capabilityReport.Capabilities.TryGetValue(actionId, out var capability))
        {
            diagnostics["requestedFeatureId"] = actionId;
            diagnostics["promotedCapabilityGate"] = "missing";
            return new BackendRouteDecision(
                Allowed: false,
                Backend: ExecutionBackendKind.Extender,
                ReasonCode: RuntimeReasonCode.SAFETY_FAIL_CLOSED,
                Message: "Promoted extender capability is missing for this mutating operation (fail-closed).",
                Diagnostics: diagnostics);
        }

        if (capability.Available && capability.Confidence == CapabilityConfidenceState.Verified)
        {
            return null;
        }

        diagnostics["requestedFeatureId"] = actionId;
        diagnostics["promotedCapabilityGate"] = "unverified";
        diagnostics["promotedCapabilityAvailable"] = capability.Available;
        diagnostics["promotedCapabilityConfidence"] = capability.Confidence.ToString();
        diagnostics["promotedCapabilityReasonCode"] = capability.ReasonCode.ToString();
        return new BackendRouteDecision(
            Allowed: false,
            Backend: ExecutionBackendKind.Extender,
            ReasonCode: RuntimeReasonCode.SAFETY_FAIL_CLOSED,
            Message: "Promoted extender capability is not verified for this mutating operation (fail-closed).",
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

    private static ExecutionBackendKind ResolvePreferredBackend(
        string? backendPreference,
        ExecutionBackendKind defaultBackend,
        bool forceExtenderGate)
    {
        if (forceExtenderGate)
        {
            return ExecutionBackendKind.Extender;
        }

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

    private static ExecutionBackendKind MapDefaultBackend(ExecutionKind executionKind, bool forceExtenderGate)
    {
        if (forceExtenderGate)
        {
            return ExecutionBackendKind.Extender;
        }

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
        string actionId,
        bool isPromotedExtenderAction)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return Array.Empty<string>();
        }

        var required = profileRequiredCapabilities
            .Where(featureId => featureId.Equals(actionId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (isPromotedExtenderAction &&
            !required.Contains(actionId, StringComparer.OrdinalIgnoreCase))
        {
            required.Add(actionId);
        }

        return required.ToArray();
    }

    private static bool IsPromotedExtenderAction(
        string actionId,
        ExecutionKind executionKind,
        TrainerProfile profile,
        ProcessMetadata process)
    {
        return executionKind == ExecutionKind.Sdk &&
               IsFoCContext(profile, process) &&
               !string.IsNullOrWhiteSpace(actionId) &&
               PromotedExtenderActionIds.Contains(actionId);
    }

    private static bool IsFoCContext(TrainerProfile profile, ProcessMetadata process)
    {
        return profile.ExeTarget == ExeTarget.Swfoc ||
               process.ExeTarget == ExeTarget.Swfoc;
    }

    private readonly record struct BackendDiagnosticsContext(
        ActionExecutionRequest Request,
        TrainerProfile Profile,
        ProcessMetadata Process,
        CapabilityReport CapabilityReport,
        ExecutionBackendKind DefaultBackend,
        ExecutionBackendKind PreferredBackend,
        IReadOnlyList<string> ProfileRequiredCapabilities,
        IReadOnlyList<string> RequiredCapabilities,
        IReadOnlyList<string> MissingRequired,
        bool PromotedExtenderAction);

    private readonly record struct RouteResolutionState(
        ExecutionBackendKind PreferredBackend,
        bool MutatingAction,
        IReadOnlyList<string> MissingRequired,
        Dictionary<string, object?> Diagnostics,
        bool PromotedExtenderAction);
}
