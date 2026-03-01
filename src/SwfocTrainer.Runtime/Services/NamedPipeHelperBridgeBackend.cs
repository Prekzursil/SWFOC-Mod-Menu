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
        "set_hero_state_helper",
        "toggle_roe_respawn_helper"
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

        var payload = request.ActionRequest.Payload.DeepClone() as JsonObject ?? new JsonObject();
        if (request.Hook is not null)
        {
            payload["helperHookId"] ??= request.Hook.Id;
            if (!string.IsNullOrWhiteSpace(request.Hook.EntryPoint))
            {
                payload["helperEntryPoint"] ??= request.Hook.EntryPoint;
            }

            payload["helperScript"] ??= request.Hook.Script;
            if (request.Hook.ArgContract is not null && request.Hook.ArgContract.Count > 0)
            {
                payload["helperArgContract"] ??= JsonSerializer.SerializeToNode(request.Hook.ArgContract);
            }

            if (request.Hook.VerifyContract is not null && request.Hook.VerifyContract.Count > 0)
            {
                payload["helperVerifyContract"] ??= JsonSerializer.SerializeToNode(request.Hook.VerifyContract);
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

        var actionRequest = request.ActionRequest with
        {
            Payload = payload,
            Context = context
        };

        var capabilityReport = await _backend.ProbeCapabilitiesAsync(actionRequest.ProfileId, request.Process, cancellationToken);
        var executionResult = await _backend.ExecuteAsync(actionRequest, capabilityReport, cancellationToken);

        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["helperBridgeState"] = executionResult.Succeeded ? "applied" : "failed",
            ["helperInvocationSource"] = "native_bridge",
            ["helperEntryPoint"] = request.Hook?.EntryPoint ?? string.Empty,
            ["helperHookId"] = request.Hook?.Id ?? string.Empty,
            ["helperVerifyState"] = executionResult.Succeeded ? "applied" : "failed"
        };

        if (executionResult.Diagnostics is not null)
        {
            foreach (var kv in executionResult.Diagnostics)
            {
                diagnostics[kv.Key] = kv.Value;
            }
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
}
