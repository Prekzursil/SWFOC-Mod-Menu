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
    private const string ActionTransferFleetSafe = "transfer_fleet_safe";
    private const string ActionFlipPlanetOwner = "flip_planet_owner";
    private const string ActionSwitchPlayerFaction = "switch_player_faction";
    private const string ActionEditHeroState = "edit_hero_state";
    private const string ActionCreateHeroVariant = "create_hero_variant";

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
    private const string PayloadOperationPolicy = "operationPolicy";
    private const string PayloadTargetContext = "targetContext";
    private const string PayloadMutationIntent = "mutationIntent";
    private const string PayloadVerificationContractVersion = "verificationContractVersion";
    private const string PayloadAllowCrossFaction = "allowCrossFaction";
    private const string PayloadPlacementMode = "placementMode";
    private const string PayloadForceOverride = "forceOverride";
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
        ActionSetContextAllegiance,
        ActionSetContextFaction,
        ActionTransferFleetSafe,
        ActionFlipPlanetOwner,
        ActionSwitchPlayerFaction,
        ActionEditHeroState,
        ActionCreateHeroVariant
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

        if (!TryValidateOperationToken(executionResult.Diagnostics, operation.OperationToken, out var tokenFailureMessage))
        {
            return CreateVerificationFailureResult(tokenFailureMessage, diagnostics, "failed_operation_token");
        }

        if (!ValidateVerificationContract(request, diagnostics, out var verificationMessage))
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
        payload[PayloadOperationPolicy] ??= request.OperationPolicy ?? ResolveDefaultOperationPolicy(request.ActionRequest.Action.Id);
        payload[PayloadTargetContext] ??= request.TargetContext ?? request.ActionRequest.RuntimeMode.ToString();
        payload[PayloadMutationIntent] ??= request.MutationIntent ?? ResolveDefaultMutationIntent(request.ActionRequest.Action.Id);
        payload[PayloadVerificationContractVersion] ??= request.VerificationContractVersion;

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

        ApplyHookIdentity(payload, hook);
        ApplyHookEntryPoint(payload, request.ActionRequest.Action.Id, hook.EntryPoint);
        ApplyHookScript(payload, hook.Script);
        ApplyHookArgContract(payload, hook.ArgContract);
        ApplyHookVerifyContract(payload, request.VerificationContract, hook.VerifyContract);
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
        context[PayloadOperationPolicy] = request.OperationPolicy ?? string.Empty;
        context[PayloadTargetContext] = request.TargetContext ?? request.ActionRequest.RuntimeMode.ToString();
        context[PayloadMutationIntent] = request.MutationIntent ?? string.Empty;
        context[PayloadVerificationContractVersion] = request.VerificationContractVersion;

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
        var diagnostics = CreateBaseExecutionDiagnostics(request, executionResult, operation);
        MergeDiagnostics(diagnostics, executionResult.Diagnostics);
        return diagnostics;
    }

    private static void ApplyHookIdentity(JsonObject payload, HelperHookSpec hook)
    {
        payload[PayloadHelperHookId] ??= hook.Id;
    }

    private static void ApplyHookEntryPoint(JsonObject payload, string actionId, string? configuredEntryPoint)
    {
        var defaultEntryPoint = ResolveDefaultHelperEntryPoint(actionId, configuredEntryPoint);
        if (string.IsNullOrWhiteSpace(defaultEntryPoint))
        {
            return;
        }

        payload[PayloadHelperEntryPoint] ??= defaultEntryPoint;
    }

    private static void ApplyHookScript(JsonObject payload, string? script)
    {
        payload[PayloadHelperScript] ??= script;
    }

    private static void ApplyHookArgContract(JsonObject payload, IReadOnlyDictionary<string, string>? argContract)
    {
        if (argContract is null || argContract.Count == 0)
        {
            return;
        }

        payload[PayloadHelperArgContract] ??= JsonSerializer.SerializeToNode(argContract);
    }

    private static void ApplyHookVerifyContract(
        JsonObject payload,
        IReadOnlyDictionary<string, string>? requestVerifyContract,
        IReadOnlyDictionary<string, string>? hookVerifyContract)
    {
        var verifyContract = requestVerifyContract
            ?? hookVerifyContract
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (verifyContract.Count == 0)
        {
            return;
        }

        payload[PayloadHelperVerifyContract] ??= JsonSerializer.SerializeToNode(verifyContract);
    }

    private static Dictionary<string, object?> CreateBaseExecutionDiagnostics(
        HelperBridgeRequest request,
        ActionExecutionResult executionResult,
        HelperOperationContext operation)
    {
        var helperState = executionResult.Succeeded ? "applied" : "failed";
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [DiagnosticHelperBridgeState] = helperState,
            [DiagnosticHelperInvocationSource] = InvocationSourceNativeBridge,
            [DiagnosticHelperEntryPoint] = request.Hook?.EntryPoint ?? string.Empty,
            [DiagnosticHelperHookId] = request.Hook?.Id ?? string.Empty,
            [DiagnosticHelperVerifyState] = helperState,
            [DiagnosticOperationKind] = operation.OperationKind.ToString(),
            [DiagnosticOperationToken] = operation.OperationToken,
            [PayloadOperationPolicy] = request.OperationPolicy ?? string.Empty,
            [PayloadTargetContext] = request.TargetContext ?? request.ActionRequest.RuntimeMode.ToString(),
            [PayloadMutationIntent] = request.MutationIntent ?? string.Empty,
            [PayloadVerificationContractVersion] = request.VerificationContractVersion
        };
    }

    private static void MergeDiagnostics(
        IDictionary<string, object?> target,
        IReadOnlyDictionary<string, object?>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var kv in source)
        {
            target[kv.Key] = kv.Value;
        }
    }

    private static bool TryValidateOperationToken(
        IReadOnlyDictionary<string, object?>? backendDiagnostics,
        string expectedOperationToken,
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

    private static string ResolveDefaultOperationPolicy(string actionId)
    {
        return actionId switch
        {
            var value when value.Equals(ActionSpawnContextEntity, StringComparison.OrdinalIgnoreCase) ||
                       value.Equals(ActionSpawnTacticalEntity, StringComparison.OrdinalIgnoreCase) => "tactical_ephemeral_zero_pop",
            var value when value.Equals(ActionSpawnGalacticEntity, StringComparison.OrdinalIgnoreCase) => "galactic_persistent_spawn",
            var value when value.Equals(ActionPlacePlanetBuilding, StringComparison.OrdinalIgnoreCase) => "galactic_building_safe_rules",
            var value when value.Equals(ActionTransferFleetSafe, StringComparison.OrdinalIgnoreCase) => "fleet_transfer_safe",
            var value when value.Equals(ActionFlipPlanetOwner, StringComparison.OrdinalIgnoreCase) => "planet_flip_transactional",
            var value when value.Equals(ActionSwitchPlayerFaction, StringComparison.OrdinalIgnoreCase) => "switch_player_faction",
            var value when value.Equals(ActionEditHeroState, StringComparison.OrdinalIgnoreCase) => "hero_state_adaptive",
            var value when value.Equals(ActionCreateHeroVariant, StringComparison.OrdinalIgnoreCase) => "hero_variant_patch_mod",
            _ => "helper_operation_default"
        };
    }

    private static string ResolveDefaultMutationIntent(string actionId)
    {
        return actionId switch
        {
            var value when value.Equals(ActionSpawnUnitHelper, StringComparison.OrdinalIgnoreCase) ||
                       value.Equals(ActionSpawnContextEntity, StringComparison.OrdinalIgnoreCase) ||
                       value.Equals(ActionSpawnTacticalEntity, StringComparison.OrdinalIgnoreCase) ||
                       value.Equals(ActionSpawnGalacticEntity, StringComparison.OrdinalIgnoreCase) => "spawn_entity",
            var value when value.Equals(ActionPlacePlanetBuilding, StringComparison.OrdinalIgnoreCase) => "place_building",
            var value when value.Equals(ActionSetContextAllegiance, StringComparison.OrdinalIgnoreCase) ||
                       value.Equals(ActionSetContextFaction, StringComparison.OrdinalIgnoreCase) => "set_context_allegiance",
            var value when value.Equals(ActionSetHeroStateHelper, StringComparison.OrdinalIgnoreCase) ||
                       value.Equals(ActionEditHeroState, StringComparison.OrdinalIgnoreCase) => "edit_hero_state",
            var value when value.Equals(ActionToggleRoeRespawnHelper, StringComparison.OrdinalIgnoreCase) => "toggle_respawn_policy",
            var value when value.Equals(ActionTransferFleetSafe, StringComparison.OrdinalIgnoreCase) => "transfer_fleet_safe",
            var value when value.Equals(ActionFlipPlanetOwner, StringComparison.OrdinalIgnoreCase) => "flip_planet_owner",
            var value when value.Equals(ActionSwitchPlayerFaction, StringComparison.OrdinalIgnoreCase) => "switch_player_faction",
            var value when value.Equals(ActionCreateHeroVariant, StringComparison.OrdinalIgnoreCase) => "create_hero_variant",
            _ => "unknown"
        };
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
            var value when value.Equals(ActionTransferFleetSafe, StringComparison.OrdinalIgnoreCase) => HelperBridgeOperationKind.TransferFleetSafe,
            var value when value.Equals(ActionFlipPlanetOwner, StringComparison.OrdinalIgnoreCase) => HelperBridgeOperationKind.FlipPlanetOwner,
            var value when value.Equals(ActionSwitchPlayerFaction, StringComparison.OrdinalIgnoreCase) => HelperBridgeOperationKind.SwitchPlayerFaction,
            var value when value.Equals(ActionEditHeroState, StringComparison.OrdinalIgnoreCase) => HelperBridgeOperationKind.EditHeroState,
            var value when value.Equals(ActionCreateHeroVariant, StringComparison.OrdinalIgnoreCase) => HelperBridgeOperationKind.CreateHeroVariant,
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
            payload[PayloadPlacementMode] ??= "reinforcement_zone";
            return;
        }

        if (actionId.Equals(ActionSpawnGalacticEntity, StringComparison.OrdinalIgnoreCase))
        {
            ApplySpawnDefaults(payload, populationPolicy: "Normal", persistencePolicy: "PersistentGalactic");
            return;
        }

        if (actionId.Equals(ActionPlacePlanetBuilding, StringComparison.OrdinalIgnoreCase))
        {
            payload[PayloadPlacementMode] ??= "safe_rules";
            payload[PayloadForceOverride] ??= false;
            payload[PayloadAllowCrossFaction] ??= true;
            return;
        }

        if (actionId.Equals(ActionTransferFleetSafe, StringComparison.OrdinalIgnoreCase))
        {
            payload[PayloadAllowCrossFaction] ??= true;
            payload[PayloadPlacementMode] ??= "safe_transfer";
            payload[PayloadForceOverride] ??= false;
            return;
        }

        if (actionId.Equals(ActionFlipPlanetOwner, StringComparison.OrdinalIgnoreCase))
        {
            payload[PayloadAllowCrossFaction] ??= true;
            payload["planetFlipMode"] ??= "convert_everything";
            payload[PayloadForceOverride] ??= false;
            return;
        }

        if (actionId.Equals(ActionSwitchPlayerFaction, StringComparison.OrdinalIgnoreCase))
        {
            payload[PayloadAllowCrossFaction] ??= true;
            return;
        }

        if (actionId.Equals(ActionEditHeroState, StringComparison.OrdinalIgnoreCase))
        {
            payload["heroStatePolicy"] ??= "mod_adaptive";
            payload[PayloadAllowCrossFaction] ??= true;
            return;
        }

        if (actionId.Equals(ActionCreateHeroVariant, StringComparison.OrdinalIgnoreCase))
        {
            payload["variantGenerationMode"] ??= "patch_mod_overlay";
            payload[PayloadAllowCrossFaction] ??= true;
        }
    }

    private static void ApplySpawnDefaults(JsonObject payload, string populationPolicy, string persistencePolicy)
    {
        payload["populationPolicy"] ??= populationPolicy;
        payload["persistencePolicy"] ??= persistencePolicy;
        payload[PayloadAllowCrossFaction] ??= true;
    }

    private static bool ValidateVerificationContract(
        HelperBridgeRequest request,
        IReadOnlyDictionary<string, object?> diagnostics,
        out string failureMessage)
    {
        var verifyContract = request.VerificationContract ?? request.Hook?.VerifyContract;
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

        if (actionId.Equals(ActionSetContextAllegiance, StringComparison.OrdinalIgnoreCase) ||
            actionId.Equals(ActionSetContextFaction, StringComparison.OrdinalIgnoreCase))
        {
            return "SWFOC_Trainer_Set_Context_Allegiance";
        }

        if (actionId.Equals(ActionTransferFleetSafe, StringComparison.OrdinalIgnoreCase))
        {
            return "SWFOC_Trainer_Transfer_Fleet_Safe";
        }

        if (actionId.Equals(ActionFlipPlanetOwner, StringComparison.OrdinalIgnoreCase))
        {
            return "SWFOC_Trainer_Flip_Planet_Owner";
        }

        if (actionId.Equals(ActionSwitchPlayerFaction, StringComparison.OrdinalIgnoreCase))
        {
            return "SWFOC_Trainer_Switch_Player_Faction";
        }

        if (actionId.Equals(ActionEditHeroState, StringComparison.OrdinalIgnoreCase))
        {
            return "SWFOC_Trainer_Edit_Hero_State";
        }

        if (actionId.Equals(ActionCreateHeroVariant, StringComparison.OrdinalIgnoreCase))
        {
            return "SWFOC_Trainer_Create_Hero_Variant";
        }

        return string.IsNullOrWhiteSpace(hookEntryPoint) ? string.Empty : hookEntryPoint;
    }

    private readonly record struct HelperOperationContext(
        HelperBridgeOperationKind OperationKind,
        string OperationToken);
}
