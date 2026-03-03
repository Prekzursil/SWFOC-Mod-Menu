using System.Text.Json;
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class NamedPipeHelperBridgeBackend : IHelperBridgeBackend
{
    private static readonly string[] HelperFeatureIds =
    [
        "spawn_unit_helper",
        "spawn_context_entity",
        "spawn_tactical_entity",
        "spawn_galactic_entity",
        "place_planet_building",
        "set_hero_state_helper",
        "toggle_roe_respawn_helper",
        "set_context_allegiance"
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
            return new HelperBridgeProbeResult(
                Available: false,
                ReasonCode: RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE,
                Message: "Helper bridge probe failed: process is not attached.",
                Diagnostics: new Dictionary<string, object?>
                {
                    ["helperBridgeState"] = "unavailable",
                    ["processId"] = request.Process.ProcessId
                });
        }

        var capabilityReport = await _backend.ProbeCapabilitiesAsync(request.ProfileId, request.Process, cancellationToken);
        var availableFeatures = HelperFeatureIds
            .Where(featureId => capabilityReport.IsFeatureAvailable(featureId))
            .ToArray();

        if (availableFeatures.Length == 0)
        {
            return new HelperBridgeProbeResult(
                Available: false,
                ReasonCode: RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE,
                Message: "Helper bridge capabilities are unavailable for this attachment.",
                Diagnostics: new Dictionary<string, object?>
                {
                    ["helperBridgeState"] = "unavailable",
                    ["probeReasonCode"] = capabilityReport.ProbeReasonCode.ToString(),
                    ["availableFeatures"] = string.Empty,
                    ["capabilityCount"] = capabilityReport.Capabilities.Count
                });
        }

        return new HelperBridgeProbeResult(
            Available: true,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Message: "Helper bridge probe passed.",
            Diagnostics: new Dictionary<string, object?>
            {
                ["helperBridgeState"] = "ready",
                ["probeReasonCode"] = capabilityReport.ProbeReasonCode.ToString(),
                ["availableFeatures"] = string.Join(",", availableFeatures),
                ["capabilityCount"] = capabilityReport.Capabilities.Count
            });
    }

    public async Task<HelperBridgeExecutionResult> ExecuteAsync(HelperBridgeRequest request, CancellationToken cancellationToken)
    {
        var probe = await ProbeAsync(
            new HelperBridgeProbeRequest(request.ActionRequest.ProfileId, request.Process, request.Hook is null ? [] : [request.Hook]),
            cancellationToken);
        if (!probe.Available)
        {
            return new HelperBridgeExecutionResult(
                Succeeded: false,
                ReasonCode: probe.ReasonCode,
                Message: probe.Message,
                Diagnostics: probe.Diagnostics);
        }

        var operationKind = request.OperationKind == HelperBridgeOperationKind.Unknown
            ? ResolveOperationKind(request.ActionRequest.Action.Id)
            : request.OperationKind;
        var operationToken = string.IsNullOrWhiteSpace(request.OperationToken)
            ? Guid.NewGuid().ToString("N")
            : request.OperationToken.Trim();

        var payload = request.ActionRequest.Payload.DeepClone() as JsonObject ?? new JsonObject();
        payload["operationKind"] ??= operationKind.ToString();
        payload["operationToken"] ??= operationToken;
        payload["helperInvocationContractVersion"] ??= request.InvocationContractVersion;
        ApplyActionSpecificDefaults(request.ActionRequest.Action.Id, payload);
        if (request.Hook is not null)
        {
            payload["helperHookId"] ??= request.Hook.Id;
            var defaultEntryPoint = ResolveDefaultHelperEntryPoint(request.ActionRequest.Action.Id, request.Hook.EntryPoint);
            if (!string.IsNullOrWhiteSpace(defaultEntryPoint))
            {
                payload["helperEntryPoint"] ??= defaultEntryPoint;
            }

            payload["helperScript"] ??= request.Hook.Script;
            if (request.Hook.ArgContract is not null && request.Hook.ArgContract.Count > 0)
            {
                payload["helperArgContract"] ??= JsonSerializer.SerializeToNode(request.Hook.ArgContract);
            }

            var verifyContract = request.VerificationContract
                ?? request.Hook.VerifyContract
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (verifyContract.Count > 0)
            {
                payload["helperVerifyContract"] ??= JsonSerializer.SerializeToNode(verifyContract);
            }
        }

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (request.ActionRequest.Context is not null)
        {
            foreach (var kv in request.ActionRequest.Context)
            {
                context[kv.Key] = kv.Value;
            }
        }

        context["processId"] = request.Process.ProcessId;
        context["processName"] = request.Process.ProcessName;
        context["processPath"] = request.Process.ProcessPath;
        context["helperInvocationSource"] = "native_bridge";
        context["operationKind"] = operationKind.ToString();
        context["operationToken"] = operationToken;

        var actionRequest = request.ActionRequest with
        {
            Payload = payload,
            Context = context
        };

        var capabilityReport = await _backend.ProbeCapabilitiesAsync(actionRequest.ProfileId, request.Process, cancellationToken);
        var executionResult = await _backend.ExecuteAsync(actionRequest, capabilityReport, cancellationToken);
        var backendReportedOperationToken = TryGetStringDiagnostic(executionResult.Diagnostics, "operationToken", out var backendOperationToken);

        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["helperBridgeState"] = executionResult.Succeeded ? "applied" : "failed",
            ["helperInvocationSource"] = "native_bridge",
            ["helperEntryPoint"] = request.Hook?.EntryPoint ?? string.Empty,
            ["helperHookId"] = request.Hook?.Id ?? string.Empty,
            ["helperVerifyState"] = executionResult.Succeeded ? "applied" : "failed",
            ["operationKind"] = operationKind.ToString(),
            ["operationToken"] = operationToken
        };

        if (executionResult.Diagnostics is not null)
        {
            foreach (var kv in executionResult.Diagnostics)
            {
                diagnostics[kv.Key] = kv.Value;
            }
        }

        if (executionResult.Succeeded &&
            !backendReportedOperationToken)
        {
            diagnostics["helperVerifyState"] = "failed_operation_token";
            return new HelperBridgeExecutionResult(
                Succeeded: false,
                ReasonCode: RuntimeReasonCode.HELPER_VERIFICATION_FAILED,
                Message: "Helper verification failed: operation token was not returned by backend diagnostics.",
                Diagnostics: diagnostics);
        }

        if (executionResult.Succeeded &&
            !string.Equals(backendOperationToken, operationToken, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics["helperVerifyState"] = "failed_operation_token";
            return new HelperBridgeExecutionResult(
                Succeeded: false,
                ReasonCode: RuntimeReasonCode.HELPER_VERIFICATION_FAILED,
                Message: $"Helper verification failed: operation token mismatch. expected='{operationToken}', actual='{backendOperationToken ?? string.Empty}'.",
                Diagnostics: diagnostics);
        }

        if (executionResult.Succeeded &&
            !ValidateVerificationContract(request.Hook, diagnostics, out var verificationMessage))
        {
            diagnostics["helperVerifyState"] = "failed_contract";
            return new HelperBridgeExecutionResult(
                Succeeded: false,
                ReasonCode: RuntimeReasonCode.HELPER_VERIFICATION_FAILED,
                Message: verificationMessage,
                Diagnostics: diagnostics);
        }

        return new HelperBridgeExecutionResult(
            Succeeded: executionResult.Succeeded,
            ReasonCode: executionResult.Succeeded
                ? RuntimeReasonCode.HELPER_EXECUTION_APPLIED
                : RuntimeReasonCode.HELPER_INVOCATION_FAILED,
            Message: executionResult.Message,
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
        if (actionId.Equals("spawn_unit_helper", StringComparison.OrdinalIgnoreCase))
        {
            return HelperBridgeOperationKind.SpawnUnitHelper;
        }

        if (actionId.Equals("spawn_context_entity", StringComparison.OrdinalIgnoreCase))
        {
            return HelperBridgeOperationKind.SpawnContextEntity;
        }

        if (actionId.Equals("spawn_tactical_entity", StringComparison.OrdinalIgnoreCase))
        {
            return HelperBridgeOperationKind.SpawnTacticalEntity;
        }

        if (actionId.Equals("spawn_galactic_entity", StringComparison.OrdinalIgnoreCase))
        {
            return HelperBridgeOperationKind.SpawnGalacticEntity;
        }

        if (actionId.Equals("place_planet_building", StringComparison.OrdinalIgnoreCase))
        {
            return HelperBridgeOperationKind.PlacePlanetBuilding;
        }

        if (actionId.Equals("set_context_allegiance", StringComparison.OrdinalIgnoreCase) ||
            actionId.Equals("set_context_faction", StringComparison.OrdinalIgnoreCase))
        {
            return HelperBridgeOperationKind.SetContextAllegiance;
        }

        if (actionId.Equals("set_hero_state_helper", StringComparison.OrdinalIgnoreCase))
        {
            return HelperBridgeOperationKind.SetHeroStateHelper;
        }

        if (actionId.Equals("toggle_roe_respawn_helper", StringComparison.OrdinalIgnoreCase))
        {
            return HelperBridgeOperationKind.ToggleRoeRespawnHelper;
        }

        return HelperBridgeOperationKind.Unknown;
    }

    private static void ApplyActionSpecificDefaults(string actionId, JsonObject payload)
    {
        if (actionId.Equals("spawn_context_entity", StringComparison.OrdinalIgnoreCase) ||
            actionId.Equals("spawn_tactical_entity", StringComparison.OrdinalIgnoreCase))
        {
            payload["populationPolicy"] ??= "ForceZeroTactical";
            payload["persistencePolicy"] ??= "EphemeralBattleOnly";
            payload["allowCrossFaction"] ??= true;
        }

        if (actionId.Equals("spawn_galactic_entity", StringComparison.OrdinalIgnoreCase))
        {
            payload["populationPolicy"] ??= "Normal";
            payload["persistencePolicy"] ??= "PersistentGalactic";
            payload["allowCrossFaction"] ??= true;
        }

        if (actionId.Equals("place_planet_building", StringComparison.OrdinalIgnoreCase))
        {
            payload["placementMode"] ??= "safe_rules";
            payload["forceOverride"] ??= false;
            payload["allowCrossFaction"] ??= true;
        }
    }

    private static bool ValidateVerificationContract(
        HelperHookSpec? hook,
        IReadOnlyDictionary<string, object?> diagnostics,
        out string failureMessage)
    {
        if (hook?.VerifyContract is null || hook.VerifyContract.Count == 0)
        {
            failureMessage = string.Empty;
            return true;
        }

        foreach (var contract in hook.VerifyContract)
        {
            var key = contract.Key;
            var expected = contract.Value ?? string.Empty;
            var actual = diagnostics.TryGetValue(key, out var raw) ? raw?.ToString() ?? string.Empty : string.Empty;

            if (expected.StartsWith("required:", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(actual))
                {
                    continue;
                }

                failureMessage = $"Helper verification failed: required diagnostic '{key}' was not populated.";
                return false;
            }

            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            failureMessage =
                $"Helper verification failed: diagnostic '{key}' expected '{expected}' but was '{actual}'.";
            return false;
        }

        failureMessage = string.Empty;
        return true;
    }

    private static string ResolveDefaultHelperEntryPoint(string actionId, string? hookEntryPoint)
    {
        if (actionId.Equals("spawn_context_entity", StringComparison.OrdinalIgnoreCase) ||
            actionId.Equals("spawn_tactical_entity", StringComparison.OrdinalIgnoreCase) ||
            actionId.Equals("spawn_galactic_entity", StringComparison.OrdinalIgnoreCase))
        {
            return "SWFOC_Trainer_Spawn_Context";
        }

        if (actionId.Equals("place_planet_building", StringComparison.OrdinalIgnoreCase))
        {
            return "SWFOC_Trainer_Place_Building";
        }

        if (!string.IsNullOrWhiteSpace(hookEntryPoint))
        {
            return hookEntryPoint;
        }

        return string.Empty;
    }
}
