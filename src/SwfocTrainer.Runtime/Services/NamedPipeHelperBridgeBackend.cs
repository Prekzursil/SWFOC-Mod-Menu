using System.Text.Json;
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class NamedPipeHelperBridgeBackend : IHelperBridgeBackend
{
    private const string ActionSpawnUnitHelper = "spawn_unit_helper";
    private const string ActionSpawnContextEntity = "spawn_context_entity";
    private const string ActionSpawnTacticalEntity = "spawn_tactical_entity";
    private const string ActionSpawnGalacticEntity = "spawn_galactic_entity";
    private const string ActionPlacePlanetBuilding = "place_planet_building";
    private const string ActionSetHeroStateHelper = "set_hero_state_helper";
    private const string ActionToggleRoeRespawnHelper = "toggle_roe_respawn_helper";
    private const string ActionSetContextAllegiance = "set_context_allegiance";
    private const string ActionSetContextFaction = "set_context_faction";

    private const string DiagnosticHelperBridgeState = "helperBridgeState";
    private const string DiagnosticProbeReasonCode = "probeReasonCode";
    private const string DiagnosticAvailableFeatures = "availableFeatures";
    private const string DiagnosticCapabilityCount = "capabilityCount";
    private const string DiagnosticHelperInvocationSource = "helperInvocationSource";
    private const string DiagnosticHelperEntryPoint = "helperEntryPoint";
    private const string DiagnosticHelperHookId = "helperHookId";
    private const string DiagnosticHelperVerifyState = "helperVerifyState";
    private const string DiagnosticOperationKind = "operationKind";
    private const string DiagnosticOperationToken = "operationToken";
    private const string DiagnosticProcessId = "processId";
    private const string DiagnosticProcessName = "processName";
    private const string DiagnosticProcessPath = "processPath";

    private const string PayloadOperationKind = "operationKind";
    private const string PayloadOperationToken = "operationToken";
    private const string PayloadHelperInvocationContractVersion = "helperInvocationContractVersion";
    private const string PayloadHelperHookId = "helperHookId";
    private const string PayloadHelperEntryPoint = "helperEntryPoint";
    private const string PayloadHelperScript = "helperScript";
    private const string PayloadHelperArgContract = "helperArgContract";
    private const string PayloadHelperVerifyContract = "helperVerifyContract";

    private const string InvocationSourceNativeBridge = "native_bridge";

    private static readonly string[] HelperFeatureIds =
    [
        ActionSpawnUnitHelper,
        ActionSpawnContextEntity,
        ActionSpawnTacticalEntity,
        ActionSpawnGalacticEntity,
        ActionPlacePlanetBuilding,
        ActionSetHeroStateHelper,
        ActionToggleRoeRespawnHelper,
        ActionSetContextAllegiance
    ];

    private readonly IExecutionBackend _backend;

    public NamedPipeHelperBridgeBackend(IExecutionBackend backend)
    {
        _backend = backend;
    }

    public async Task<HelperBridgeProbeResult> ProbeAsync(HelperBridgeProbeRequest request, CancellationToken cancellationToken)
    {
        if (request.Process.ProcessId <= 0)
        {
            return CreateProcessUnavailableProbeResult(request.Process.ProcessId);
        }

        var capabilityReport = await _backend.ProbeCapabilitiesAsync(request.ProfileId, request.Process, cancellationToken);
        var availableFeatures = HelperFeatureIds
            .Where(featureId => capabilityReport.IsFeatureAvailable(featureId))
            .ToArray();

        return availableFeatures.Length == 0
            ? CreateCapabilityUnavailableProbeResult(capabilityReport)
            : CreateReadyProbeResult(capabilityReport, availableFeatures);
    }

    public async Task<HelperBridgeExecutionResult> ExecuteAsync(HelperBridgeRequest request, CancellationToken cancellationToken)
    {
        var probe = await ProbeForExecutionAsync(request, cancellationToken);
        if (!probe.Available)
        {
            return CreateProbeFailureExecutionResult(probe);
        }

        var operation = ResolveOperationContext(request);
        var payload = BuildPayload(request, operation);
        var actionRequest = BuildActionRequest(request, payload, operation);

        var capabilityReport = await _backend.ProbeCapabilitiesAsync(actionRequest.ProfileId, request.Process, cancellationToken);
        var executionResult = await _backend.ExecuteAsync(actionRequest, capabilityReport, cancellationToken);
        var diagnostics = BuildExecutionDiagnostics(request, executionResult, operation);

        if (!executionResult.Succeeded)
        {
            return CreateExecutionResult(
                succeeded: false,
                reasonCode: RuntimeReasonCode.HELPER_INVOCATION_FAILED,
                message: executionResult.Message,
                diagnostics: diagnostics);
        }

        if (!TryValidateOperationToken(executionResult.Diagnostics, operation.OperationToken, diagnostics, out var tokenFailureMessage))
        {
            return CreateVerificationFailureResult(tokenFailureMessage, diagnostics, "failed_operation_token");
        }

        if (!ValidateVerificationContract(request.Hook, diagnostics, out var verificationMessage))
        {
            return CreateVerificationFailureResult(verificationMessage, diagnostics, "failed_contract");
        }

        return CreateExecutionResult(
            succeeded: true,
            reasonCode: RuntimeReasonCode.HELPER_EXECUTION_APPLIED,
            message: executionResult.Message,
            diagnostics: diagnostics);
    }

    private static HelperBridgeProbeResult CreateProcessUnavailableProbeResult(int processId)
    {
        return new HelperBridgeProbeResult(
            Available: false,
            ReasonCode: RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE,
            Message: "Helper bridge probe failed: process is not attached.",
            Diagnostics: new Dictionary<string, object?>
            {
                [DiagnosticHelperBridgeState] = "unavailable",
                [DiagnosticProcessId] = processId
            });
    }

    private static HelperBridgeProbeResult CreateCapabilityUnavailableProbeResult(CapabilityReport capabilityReport)
    {
        return new HelperBridgeProbeResult(
            Available: false,
            ReasonCode: RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE,
            Message: "Helper bridge capabilities are unavailable for this attachment.",
            Diagnostics: new Dictionary<string, object?>
            {
                [DiagnosticHelperBridgeState] = "unavailable",
                [DiagnosticProbeReasonCode] = capabilityReport.ProbeReasonCode.ToString(),
                [DiagnosticAvailableFeatures] = string.Empty,
                [DiagnosticCapabilityCount] = capabilityReport.Capabilities.Count
            });
    }

    private static HelperBridgeProbeResult CreateReadyProbeResult(CapabilityReport capabilityReport, IReadOnlyCollection<string> availableFeatures)
    {
        return new HelperBridgeProbeResult(
            Available: true,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Message: "Helper bridge probe passed.",
            Diagnostics: new Dictionary<string, object?>
            {
                [DiagnosticHelperBridgeState] = "ready",
                [DiagnosticProbeReasonCode] = capabilityReport.ProbeReasonCode.ToString(),
                [DiagnosticAvailableFeatures] = string.Join(",", availableFeatures),
                [DiagnosticCapabilityCount] = capabilityReport.Capabilities.Count
            });
    }

    private async Task<HelperBridgeProbeResult> ProbeForExecutionAsync(HelperBridgeRequest request, CancellationToken cancellationToken)
    {
        var hooks = request.Hook is null ? Array.Empty<HelperHookSpec>() : new[] { request.Hook };
        var probeRequest = new HelperBridgeProbeRequest(request.ActionRequest.ProfileId, request.Process, hooks);
        return await ProbeAsync(probeRequest, cancellationToken);
    }

    private static HelperBridgeExecutionResult CreateProbeFailureExecutionResult(HelperBridgeProbeResult probe)
    {
        return new HelperBridgeExecutionResult(
            Succeeded: false,
            ReasonCode: probe.ReasonCode,
            Message: probe.Message,
            Diagnostics: probe.Diagnostics);
    }

    private static HelperOperationContext ResolveOperationContext(HelperBridgeRequest request)
    {
        var operationKind = request.OperationKind == HelperBridgeOperationKind.Unknown
            ? ResolveOperationKind(request.ActionRequest.Action.Id)
            : request.OperationKind;

        var operationToken = string.IsNullOrWhiteSpace(request.OperationToken)
            ? Guid.NewGuid().ToString("N")
            : request.OperationToken.Trim();

        return new HelperOperationContext(operationKind, operationToken);
    }

    private static JsonObject BuildPayload(HelperBridgeRequest request, HelperOperationContext operation)
    {
        var payload = request.ActionRequest.Payload.DeepClone() as JsonObject ?? new JsonObject();
        payload[PayloadOperationKind] ??= operation.OperationKind.ToString();
        payload[PayloadOperationToken] ??= operation.OperationToken;
        payload[PayloadHelperInvocationContractVersion] ??= request.InvocationContractVersion;

        ApplyActionSpecificDefaults(request.ActionRequest.Action.Id, payload);
        ApplyHookPayload(request, payload);
        return payload;
    }

    private static void ApplyHookPayload(HelperBridgeRequest request, JsonObject payload)
    {
        var hook = request.Hook;
        if (hook is null)
        {
            return;
        }

        payload[PayloadHelperHookId] ??= hook.Id;

        var defaultEntryPoint = ResolveDefaultHelperEntryPoint(request.ActionRequest.Action.Id, hook.EntryPoint);
        if (!string.IsNullOrWhiteSpace(defaultEntryPoint))
        {
            payload[PayloadHelperEntryPoint] ??= defaultEntryPoint;
        }

        payload[PayloadHelperScript] ??= hook.Script;

        if (hook.ArgContract is { Count: > 0 })
        {
            payload[PayloadHelperArgContract] ??= JsonSerializer.SerializeToNode(hook.ArgContract);
        }

        var verifyContract = request.VerificationContract
            ?? hook.VerifyContract
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (verifyContract.Count > 0)
        {
            payload[PayloadHelperVerifyContract] ??= JsonSerializer.SerializeToNode(verifyContract);
        }
    }

    private static ActionExecutionRequest BuildActionRequest(
        HelperBridgeRequest request,
        JsonObject payload,
        HelperOperationContext operation)
    {
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (request.ActionRequest.Context is not null)
        {
            foreach (var kv in request.ActionRequest.Context)
            {
                context[kv.Key] = kv.Value;
            }
        }

        context[DiagnosticProcessId] = request.Process.ProcessId;
        context[DiagnosticProcessName] = request.Process.ProcessName;
        context[DiagnosticProcessPath] = request.Process.ProcessPath;
        context[DiagnosticHelperInvocationSource] = InvocationSourceNativeBridge;
        context[DiagnosticOperationKind] = operation.OperationKind.ToString();
        context[DiagnosticOperationToken] = operation.OperationToken;

        return request.ActionRequest with
        {
            Payload = payload,
            Context = context
        };
    }

    private static Dictionary<string, object?> BuildExecutionDiagnostics(
        HelperBridgeRequest request,
        ActionExecutionResult executionResult,
        HelperOperationContext operation)
    {
        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [DiagnosticHelperBridgeState] = executionResult.Succeeded ? "applied" : "failed",
            [DiagnosticHelperInvocationSource] = InvocationSourceNativeBridge,
            [DiagnosticHelperEntryPoint] = request.Hook?.EntryPoint ?? string.Empty,
            [DiagnosticHelperHookId] = request.Hook?.Id ?? string.Empty,
            [DiagnosticHelperVerifyState] = executionResult.Succeeded ? "applied" : "failed",
            [DiagnosticOperationKind] = operation.OperationKind.ToString(),
            [DiagnosticOperationToken] = operation.OperationToken
        };

        if (executionResult.Diagnostics is null)
        {
            return diagnostics;
        }

        foreach (var kv in executionResult.Diagnostics)
        {
            diagnostics[kv.Key] = kv.Value;
        }

        return diagnostics;
    }

    private static bool TryValidateOperationToken(
        IReadOnlyDictionary<string, object?>? backendDiagnostics,
        string expectedOperationToken,
        IDictionary<string, object?> diagnostics,
        out string failureMessage)
    {
        failureMessage = string.Empty;
        if (!TryGetStringDiagnostic(backendDiagnostics, DiagnosticOperationToken, out var backendOperationToken))
        {
            failureMessage = "Helper verification failed: operation token was not returned by backend diagnostics.";
            return false;
        }

        if (string.Equals(backendOperationToken, expectedOperationToken, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        failureMessage =
            $"Helper verification failed: operation token mismatch. expected='{expectedOperationToken}', actual='{backendOperationToken ?? string.Empty}'.";
        return false;
    }

    private static HelperBridgeExecutionResult CreateVerificationFailureResult(
        string message,
        Dictionary<string, object?> diagnostics,
        string verifyState)
    {
        diagnostics[DiagnosticHelperVerifyState] = verifyState;
        return CreateExecutionResult(
            succeeded: false,
            reasonCode: RuntimeReasonCode.HELPER_VERIFICATION_FAILED,
            message: message,
            diagnostics: diagnostics);
    }

    private static HelperBridgeExecutionResult CreateExecutionResult(
        bool succeeded,
        RuntimeReasonCode reasonCode,
        string message,
        IReadOnlyDictionary<string, object?> diagnostics)
    {
        return new HelperBridgeExecutionResult(
            Succeeded: succeeded,
            ReasonCode: reasonCode,
            Message: message,
            Diagnostics: diagnostics);
    }

    private static bool TryGetStringDiagnostic(
        IReadOnlyDictionary<string, object?>? diagnostics,
        string key,
        out string? value)
    {
        value = null;
        if (diagnostics is null ||
            !diagnostics.TryGetValue(key, out var rawValue) ||
            rawValue is null)
        {
            return false;
        }

        value = rawValue.ToString();
        return true;
    }

    private static HelperBridgeOperationKind ResolveOperationKind(string actionId)
    {
        return actionId switch
        {
            var value when value.Equals(ActionSpawnUnitHelper, StringComparison.OrdinalIgnoreCase) => HelperBridgeOperationKind.SpawnUnitHelper,
            var value when value.Equals(ActionSpawnContextEntity, StringComparison.OrdinalIgnoreCase) => HelperBridgeOperationKind.SpawnContextEntity,
            var value when value.Equals(ActionSpawnTacticalEntity, StringComparison.OrdinalIgnoreCase) => HelperBridgeOperationKind.SpawnTacticalEntity,
            var value when value.Equals(ActionSpawnGalacticEntity, StringComparison.OrdinalIgnoreCase) => HelperBridgeOperationKind.SpawnGalacticEntity,
            var value when value.Equals(ActionPlacePlanetBuilding, StringComparison.OrdinalIgnoreCase) => HelperBridgeOperationKind.PlacePlanetBuilding,
            var value when value.Equals(ActionSetContextAllegiance, StringComparison.OrdinalIgnoreCase) ||
                       value.Equals(ActionSetContextFaction, StringComparison.OrdinalIgnoreCase) => HelperBridgeOperationKind.SetContextAllegiance,
            var value when value.Equals(ActionSetHeroStateHelper, StringComparison.OrdinalIgnoreCase) => HelperBridgeOperationKind.SetHeroStateHelper,
            var value when value.Equals(ActionToggleRoeRespawnHelper, StringComparison.OrdinalIgnoreCase) => HelperBridgeOperationKind.ToggleRoeRespawnHelper,
            _ => HelperBridgeOperationKind.Unknown
        };
    }

    private static void ApplyActionSpecificDefaults(string actionId, JsonObject payload)
    {
        if (actionId.Equals(ActionSpawnContextEntity, StringComparison.OrdinalIgnoreCase) ||
            actionId.Equals(ActionSpawnTacticalEntity, StringComparison.OrdinalIgnoreCase))
        {
            ApplySpawnDefaults(payload, populationPolicy: "ForceZeroTactical", persistencePolicy: "EphemeralBattleOnly");
            return;
        }

        if (actionId.Equals(ActionSpawnGalacticEntity, StringComparison.OrdinalIgnoreCase))
        {
            ApplySpawnDefaults(payload, populationPolicy: "Normal", persistencePolicy: "PersistentGalactic");
            return;
        }

        if (actionId.Equals(ActionPlacePlanetBuilding, StringComparison.OrdinalIgnoreCase))
        {
            payload["placementMode"] ??= "safe_rules";
            payload["forceOverride"] ??= false;
            payload["allowCrossFaction"] ??= true;
        }
    }

    private static void ApplySpawnDefaults(JsonObject payload, string populationPolicy, string persistencePolicy)
    {
        payload["populationPolicy"] ??= populationPolicy;
        payload["persistencePolicy"] ??= persistencePolicy;
        payload["allowCrossFaction"] ??= true;
    }

    private static bool ValidateVerificationContract(
        HelperHookSpec? hook,
        IReadOnlyDictionary<string, object?> diagnostics,
        out string failureMessage)
    {
        var verifyContract = hook?.VerifyContract;
        if (verifyContract is null || verifyContract.Count == 0)
        {
            failureMessage = string.Empty;
            return true;
        }

        foreach (var contract in verifyContract)
        {
            if (ValidateVerificationEntry(contract.Key, contract.Value, diagnostics, out failureMessage))
            {
                continue;
            }

            return false;
        }

        failureMessage = string.Empty;
        return true;
    }

    private static bool ValidateVerificationEntry(
        string key,
        string? expected,
        IReadOnlyDictionary<string, object?> diagnostics,
        out string failureMessage)
    {
        var normalizedExpected = expected ?? string.Empty;
        var actual = diagnostics.TryGetValue(key, out var raw)
            ? raw?.ToString() ?? string.Empty
            : string.Empty;

        if (normalizedExpected.StartsWith("required:", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(actual))
            {
                failureMessage = string.Empty;
                return true;
            }

            failureMessage = $"Helper verification failed: required diagnostic '{key}' was not populated.";
            return false;
        }

        if (string.Equals(actual, normalizedExpected, StringComparison.OrdinalIgnoreCase))
        {
            failureMessage = string.Empty;
            return true;
        }

        failureMessage =
            $"Helper verification failed: diagnostic '{key}' expected '{normalizedExpected}' but was '{actual}'.";
        return false;
    }

    private static string ResolveDefaultHelperEntryPoint(string actionId, string? hookEntryPoint)
    {
        if (actionId.Equals(ActionSpawnContextEntity, StringComparison.OrdinalIgnoreCase) ||
            actionId.Equals(ActionSpawnTacticalEntity, StringComparison.OrdinalIgnoreCase) ||
            actionId.Equals(ActionSpawnGalacticEntity, StringComparison.OrdinalIgnoreCase))
        {
            return "SWFOC_Trainer_Spawn_Context";
        }

        if (actionId.Equals(ActionPlacePlanetBuilding, StringComparison.OrdinalIgnoreCase))
        {
            return "SWFOC_Trainer_Place_Building";
        }

        return string.IsNullOrWhiteSpace(hookEntryPoint) ? string.Empty : hookEntryPoint;
    }

    private readonly record struct HelperOperationContext(
        HelperBridgeOperationKind OperationKind,
        string OperationToken);
}
