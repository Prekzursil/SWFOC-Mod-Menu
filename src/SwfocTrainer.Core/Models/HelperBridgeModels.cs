namespace SwfocTrainer.Core.Models;

public enum HelperBridgeOperationKind
{
    Unknown = 0,
    SpawnUnitHelper,
    SpawnContextEntity,
    SpawnTacticalEntity,
    SpawnGalacticEntity,
    PlacePlanetBuilding,
    SetContextAllegiance,
    SetHeroStateHelper,
    ToggleRoeRespawnHelper,
    TransferFleetSafe,
    FlipPlanetOwner,
    SwitchPlayerFaction,
    EditHeroState,
    CreateHeroVariant
}

public sealed record HelperBridgeProbeRequest(
    string ProfileId,
    ProcessMetadata Process,
    IReadOnlyList<HelperHookSpec> Hooks);

public sealed record HelperBridgeProbeResult(
    bool Available,
    RuntimeReasonCode ReasonCode,
    string Message,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);

public sealed record HelperBridgeRequest(
    ActionExecutionRequest ActionRequest,
    ProcessMetadata Process,
    HelperHookSpec? Hook,
    HelperBridgeOperationKind OperationKind = HelperBridgeOperationKind.Unknown,
    string InvocationContractVersion = "1.0",
    IReadOnlyDictionary<string, string>? VerificationContract = null,
    string? OperationToken = null,
    string? OperationPolicy = null,
    string? TargetContext = null,
    string? MutationIntent = null,
    string VerificationContractVersion = "1.0",
    IReadOnlyDictionary<string, object?>? Context = null);

public sealed record HelperBridgeExecutionResult(
    bool Succeeded,
    RuntimeReasonCode ReasonCode,
    string Message,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);
