namespace SwfocTrainer.Core.Models;

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
    IReadOnlyDictionary<string, object?>? Context = null);

public sealed record HelperBridgeExecutionResult(
    bool Succeeded,
    RuntimeReasonCode ReasonCode,
    string Message,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);
