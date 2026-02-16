using System.Text.Json.Nodes;

namespace SwfocTrainer.Core.Models;

public enum SdkOperationId
{
    ListSelected = 0,
    ListNearby,
    Spawn,
    Kill,
    SetOwner,
    Teleport,
    SetPlanetOwner,
    SetHp,
    SetShield,
    SetCooldown
}

public enum SdkCapabilityStatus
{
    Available = 0,
    Degraded = 1,
    Unavailable = 2
}

public sealed record SdkCommandRequest(
    SdkOperationId OperationId,
    JsonObject Payload,
    string ProfileId,
    RuntimeMode RuntimeMode,
    IReadOnlyDictionary<string, object?>? Context = null);

public sealed record SdkCommandResult(
    bool Succeeded,
    SdkOperationId OperationId,
    string Message,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);

public sealed record SdkOperationCapability(
    SdkOperationId OperationId,
    SdkCapabilityStatus Status,
    bool ReadOnly,
    RuntimeMode RequiredMode,
    string ReasonCode,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);

public sealed record SdkCapabilityReport(
    string ProfileId,
    RuntimeMode RuntimeMode,
    IReadOnlyList<SdkOperationCapability> Operations)
{
    public bool TryGetCapability(SdkOperationId operationId, out SdkOperationCapability? capability)
    {
        capability = Operations.FirstOrDefault(x => x.OperationId == operationId);
        return capability is not null;
    }
}

public sealed record SdkFallbackResult(
    bool Supported,
    string Mode,
    string ReasonCode,
    string? PreparedCommand = null);
