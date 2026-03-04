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
    private const string MutationIntentSpawnEntity = "spawn_entity";

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

    private static readonly IReadOnlyDictionary<string, string> DefaultOperationPolicies =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionSpawnContextEntity] = "tactical_ephemeral_zero_pop",
            [ActionSpawnTacticalEntity] = "tactical_ephemeral_zero_pop",
            [ActionSpawnGalacticEntity] = "galactic_persistent_spawn",
            [ActionPlacePlanetBuilding] = "galactic_building_safe_rules",
            [ActionTransferFleetSafe] = "fleet_transfer_safe",
            [ActionFlipPlanetOwner] = "planet_flip_transactional",
            [ActionSwitchPlayerFaction] = "switch_player_faction",
            [ActionEditHeroState] = "hero_state_adaptive",
            [ActionCreateHeroVariant] = "hero_variant_patch_mod"
        };

    private static readonly IReadOnlyDictionary<string, string> DefaultMutationIntents =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionSpawnUnitHelper] = MutationIntentSpawnEntity,
            [ActionSpawnContextEntity] = MutationIntentSpawnEntity,
            [ActionSpawnTacticalEntity] = MutationIntentSpawnEntity,
            [ActionSpawnGalacticEntity] = MutationIntentSpawnEntity,
            [ActionPlacePlanetBuilding] = "place_building",
            [ActionSetContextAllegiance] = "set_context_allegiance",
            [ActionSetContextFaction] = "set_context_allegiance",
            [ActionSetHeroStateHelper] = "edit_hero_state",
            [ActionEditHeroState] = "edit_hero_state",
            [ActionToggleRoeRespawnHelper] = "toggle_respawn_policy",
            [ActionTransferFleetSafe] = "transfer_fleet_safe",
            [ActionFlipPlanetOwner] = "flip_planet_owner",
            [ActionSwitchPlayerFaction] = "switch_player_faction",
            [ActionCreateHeroVariant] = "create_hero_variant"
        };

    private static readonly IReadOnlyDictionary<string, HelperBridgeOperationKind> DefaultOperationKinds =
        new Dictionary<string, HelperBridgeOperationKind>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionSpawnUnitHelper] = HelperBridgeOperationKind.SpawnUnitHelper,
            [ActionSpawnContextEntity] = HelperBridgeOperationKind.SpawnContextEntity,
            [ActionSpawnTacticalEntity] = HelperBridgeOperationKind.SpawnTacticalEntity,
            [ActionSpawnGalacticEntity] = HelperBridgeOperationKind.SpawnGalacticEntity,
            [ActionPlacePlanetBuilding] = HelperBridgeOperationKind.PlacePlanetBuilding,
            [ActionSetContextAllegiance] = HelperBridgeOperationKind.SetContextAllegiance,
            [ActionSetContextFaction] = HelperBridgeOperationKind.SetContextAllegiance,
            [ActionTransferFleetSafe] = HelperBridgeOperationKind.TransferFleetSafe,
            [ActionFlipPlanetOwner] = HelperBridgeOperationKind.FlipPlanetOwner,
            [ActionSwitchPlayerFaction] = HelperBridgeOperationKind.SwitchPlayerFaction,
            [ActionEditHeroState] = HelperBridgeOperationKind.EditHeroState,
            [ActionCreateHeroVariant] = HelperBridgeOperationKind.CreateHeroVariant,
            [ActionSetHeroStateHelper] = HelperBridgeOperationKind.SetHeroStateHelper,
            [ActionToggleRoeRespawnHelper] = HelperBridgeOperationKind.ToggleRoeRespawnHelper
        };

    private static readonly IReadOnlyDictionary<string, string> DefaultHelperEntryPoints =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionSpawnContextEntity] = "SWFOC_Trainer_Spawn_Context",
            [ActionSpawnTacticalEntity] = "SWFOC_Trainer_Spawn_Context",
            [ActionSpawnGalacticEntity] = "SWFOC_Trainer_Spawn_Context",
            [ActionPlacePlanetBuilding] = "SWFOC_Trainer_Place_Building",
            [ActionSetContextAllegiance] = "SWFOC_Trainer_Set_Context_Allegiance",
            [ActionSetContextFaction] = "SWFOC_Trainer_Set_Context_Allegiance",
            [ActionTransferFleetSafe] = "SWFOC_Trainer_Transfer_Fleet_Safe",
            [ActionFlipPlanetOwner] = "SWFOC_Trainer_Flip_Planet_Owner",
            [ActionSwitchPlayerFaction] = "SWFOC_Trainer_Switch_Player_Faction",
            [ActionEditHeroState] = "SWFOC_Trainer_Edit_Hero_State",
            [ActionCreateHeroVariant] = "SWFOC_Trainer_Create_Hero_Variant"
        };

    private static readonly IReadOnlyDictionary<string, Action<JsonObject>> ActionSpecificPayloadDefaults =
        new Dictionary<string, Action<JsonObject>>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionSpawnContextEntity] = static payload =>
            {
                ApplySpawnDefaults(payload, populationPolicy: "ForceZeroTactical", persistencePolicy: "EphemeralBattleOnly");
                payload[PayloadPlacementMode] ??= "reinforcement_zone";
            },
            [ActionSpawnTacticalEntity] = static payload =>
            {
                ApplySpawnDefaults(payload, populationPolicy: "ForceZeroTactical", persistencePolicy: "EphemeralBattleOnly");
                payload[PayloadPlacementMode] ??= "reinforcement_zone";
            },
            [ActionSpawnGalacticEntity] = static payload =>
            {
                ApplySpawnDefaults(payload, populationPolicy: "Normal", persistencePolicy: "PersistentGalactic");
            },
            [ActionPlacePlanetBuilding] = static payload =>
            {
                payload[PayloadPlacementMode] ??= "safe_rules";
                payload[PayloadForceOverride] ??= false;
                payload[PayloadAllowCrossFaction] ??= true;
            },
            [ActionTransferFleetSafe] = static payload =>
            {
                payload[PayloadAllowCrossFaction] ??= true;
                payload[PayloadPlacementMode] ??= "safe_transfer";
                payload[PayloadForceOverride] ??= false;
            },
            [ActionFlipPlanetOwner] = static payload =>
            {
                payload[PayloadAllowCrossFaction] ??= true;
                payload["planetFlipMode"] ??= "convert_everything";
                payload[PayloadForceOverride] ??= false;
            },
            [ActionSwitchPlayerFaction] = static payload => payload[PayloadAllowCrossFaction] ??= true,
            [ActionEditHeroState] = static payload =>
            {
                payload["heroStatePolicy"] ??= "mod_adaptive";
                payload[PayloadAllowCrossFaction] ??= true;
            },
            [ActionCreateHeroVariant] = static payload =>
            {
                payload["variantGenerationMode"] ??= "patch_mod_overlay";
                payload[PayloadAllowCrossFaction] ??= true;
            }
        };

    private readonly IExecutionBackend _backend;

    public NamedPipeHelperBridgeBackend(IExecutionBackend backend)
    {
        _backend = backend;
    }

    public async Task<HelperBridgeProbeResult> ProbeAsync(HelperBridgeProbeRequest request, CancellationToken cancellationToken)
    {
        var safeRequest = request ?? throw new ArgumentNullException(nameof(request));
        var process = safeRequest.Process ?? throw new ArgumentNullException(nameof(request.Process));

        if (process.ProcessId <= 0)
        {
            return CreateProcessUnavailableProbeResult(process.ProcessId);
        }

        var capabilityReport = await _backend.ProbeCapabilitiesAsync(safeRequest.ProfileId, process, cancellationToken);
        var availableFeatures = HelperFeatureIds
            .Where(featureId => capabilityReport.IsFeatureAvailable(featureId))
            .ToArray();

        return availableFeatures.Length == 0
            ? CreateCapabilityUnavailableProbeResult(capabilityReport)
            : CreateReadyProbeResult(capabilityReport, availableFeatures);
    }

    public async Task<HelperBridgeExecutionResult> ExecuteAsync(HelperBridgeRequest request, CancellationToken cancellationToken)
    {
        var safeRequest = request ?? throw new ArgumentNullException(nameof(request));
        var process = safeRequest.Process ?? throw new ArgumentNullException(nameof(request.Process));
        _ = safeRequest.ActionRequest ?? throw new ArgumentNullException(nameof(request.ActionRequest));

        var probe = await ProbeForExecutionAsync(safeRequest, cancellationToken) ??
                    CreateProcessUnavailableProbeResult(process.ProcessId);
        if (!probe.Available)
        {
            return CreateProbeFailureExecutionResult(probe);
        }

        var operation = ResolveOperationContext(safeRequest);
        var payload = BuildPayload(safeRequest, operation);
        var actionRequest = BuildActionRequest(safeRequest, payload, operation);

        var capabilityReport = await _backend.ProbeCapabilitiesAsync(actionRequest.ProfileId, process, cancellationToken);
        var executionResult = await _backend.ExecuteAsync(actionRequest, capabilityReport, cancellationToken);
        var diagnostics = BuildExecutionDiagnostics(safeRequest, executionResult, operation);

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

        if (!ValidateVerificationContract(safeRequest, diagnostics, out var verificationMessage))
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
        ArgumentNullException.ThrowIfNull(capabilityReport);

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
        ArgumentNullException.ThrowIfNull(capabilityReport);
        ArgumentNullException.ThrowIfNull(availableFeatures);

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
        var safeRequest = request ?? throw new ArgumentNullException(nameof(request));
        var process = safeRequest.Process ?? throw new ArgumentNullException(nameof(request.Process));
        var actionRequest = safeRequest.ActionRequest ?? throw new ArgumentNullException(nameof(request.ActionRequest));

        var hooks = safeRequest.Hook is null ? Array.Empty<HelperHookSpec>() : new[] { safeRequest.Hook };
        var probeRequest = new HelperBridgeProbeRequest(actionRequest.ProfileId, process, hooks);
        return await ProbeAsync(probeRequest, cancellationToken) ?? CreateProcessUnavailableProbeResult(process.ProcessId);
    }

    private static HelperBridgeExecutionResult CreateProbeFailureExecutionResult(HelperBridgeProbeResult probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        return new HelperBridgeExecutionResult(
            Succeeded: false,
            ReasonCode: probe.ReasonCode,
            Message: probe.Message,
            Diagnostics: probe.Diagnostics);
    }

    private static HelperOperationContext ResolveOperationContext(HelperBridgeRequest request)
    {
        var safeRequest = request ?? throw new ArgumentNullException(nameof(request));
        var actionRequest = safeRequest.ActionRequest ?? throw new ArgumentNullException(nameof(request.ActionRequest));

        var operationKind = safeRequest.OperationKind == HelperBridgeOperationKind.Unknown
            ? ResolveOperationKind(actionRequest.Action.Id)
            : safeRequest.OperationKind;

        var operationToken = string.IsNullOrWhiteSpace(safeRequest.OperationToken)
            ? Guid.NewGuid().ToString("N")
            : safeRequest.OperationToken.Trim();

        return new HelperOperationContext(operationKind, operationToken);
    }

    private static JsonObject BuildPayload(HelperBridgeRequest request, HelperOperationContext operation)
    {
        var safeRequest = request ?? throw new ArgumentNullException(nameof(request));
        var actionRequest = safeRequest.ActionRequest ?? throw new ArgumentNullException(nameof(request.ActionRequest));

        var payload = actionRequest.Payload.DeepClone() as JsonObject ?? new JsonObject();
        payload[PayloadOperationKind] ??= operation.OperationKind.ToString();
        payload[PayloadOperationToken] ??= operation.OperationToken;
        payload[PayloadHelperInvocationContractVersion] ??= safeRequest.InvocationContractVersion;
        payload[PayloadOperationPolicy] ??= safeRequest.OperationPolicy ?? ResolveDefaultOperationPolicy(actionRequest.Action.Id);
        payload[PayloadTargetContext] ??= safeRequest.TargetContext ?? actionRequest.RuntimeMode.ToString();
        payload[PayloadMutationIntent] ??= safeRequest.MutationIntent ?? ResolveDefaultMutationIntent(actionRequest.Action.Id);
        payload[PayloadVerificationContractVersion] ??= safeRequest.VerificationContractVersion;

        ApplyActionSpecificDefaults(actionRequest.Action.Id, payload);
        ApplyHookPayload(safeRequest, payload);
        return payload;
    }

    private static void ApplyHookPayload(HelperBridgeRequest request, JsonObject payload)
    {
        var safeRequest = request ?? throw new ArgumentNullException(nameof(request));
        var actionRequest = safeRequest.ActionRequest ?? throw new ArgumentNullException(nameof(request.ActionRequest));

        var hook = safeRequest.Hook;
        if (hook is null)
        {
            return;
        }

        ApplyHookIdentity(payload, hook);
        ApplyHookEntryPoint(payload, actionRequest.Action.Id, hook.EntryPoint);
        ApplyHookScript(payload, hook.Script);
        ApplyHookArgContract(payload, hook.ArgContract);
        ApplyHookVerifyContract(payload, safeRequest.VerificationContract, hook.VerifyContract);
    }

    private static ActionExecutionRequest BuildActionRequest(
        HelperBridgeRequest request,
        JsonObject payload,
        HelperOperationContext operation)
    {
        var safeRequest = request ?? throw new ArgumentNullException(nameof(request));
        var process = safeRequest.Process ?? throw new ArgumentNullException(nameof(request.Process));
        var actionRequest = safeRequest.ActionRequest ?? throw new ArgumentNullException(nameof(request.ActionRequest));

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (actionRequest.Context is not null)
        {
            foreach (var kv in actionRequest.Context)
            {
                context[kv.Key] = kv.Value;
            }
        }

        context[DiagnosticProcessId] = process.ProcessId;
        context[DiagnosticProcessName] = process.ProcessName;
        context[DiagnosticProcessPath] = process.ProcessPath;
        context[DiagnosticHelperInvocationSource] = InvocationSourceNativeBridge;
        context[DiagnosticOperationKind] = operation.OperationKind.ToString();
        context[DiagnosticOperationToken] = operation.OperationToken;
        context[PayloadOperationPolicy] = safeRequest.OperationPolicy ?? string.Empty;
        context[PayloadTargetContext] = safeRequest.TargetContext ?? actionRequest.RuntimeMode.ToString();
        context[PayloadMutationIntent] = safeRequest.MutationIntent ?? string.Empty;
        context[PayloadVerificationContractVersion] = safeRequest.VerificationContractVersion;

        return actionRequest with
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
        return DefaultOperationPolicies.TryGetValue(actionId, out var policy)
            ? policy
            : "helper_operation_default";
    }

    private static string ResolveDefaultMutationIntent(string actionId)
    {
        return DefaultMutationIntents.TryGetValue(actionId, out var intent)
            ? intent
            : "unknown";
    }

    private static HelperBridgeOperationKind ResolveOperationKind(string actionId)
    {
        return DefaultOperationKinds.TryGetValue(actionId, out var kind)
            ? kind
            : HelperBridgeOperationKind.Unknown;
    }

    private static void ApplyActionSpecificDefaults(string actionId, JsonObject payload)
    {
        if (ActionSpecificPayloadDefaults.TryGetValue(actionId, out var applyDefaults))
        {
            applyDefaults(payload);
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
        if (DefaultHelperEntryPoints.TryGetValue(actionId, out var entryPoint))
        {
            return entryPoint;
        }

        return string.IsNullOrWhiteSpace(hookEntryPoint) ? string.Empty : hookEntryPoint;
    }

    private readonly record struct HelperOperationContext(
        HelperBridgeOperationKind OperationKind,
        string OperationToken);
}
