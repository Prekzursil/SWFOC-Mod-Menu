using System.Text.Json.Nodes;

namespace SwfocTrainer.Core.Models;

public sealed record ActionSpec(
    string Id,
    ActionCategory Category,
    RuntimeMode Mode,
    ExecutionKind ExecutionKind,
    JsonObject PayloadSchema,
    bool VerifyReadback,
    int CooldownMs,
    string? Description = null);

public sealed record ActionExecutionRequest(
    ActionSpec Action,
    JsonObject Payload,
    string ProfileId,
    RuntimeMode RuntimeMode,
    IReadOnlyDictionary<string, object?>? Context = null);

public sealed record ActionExecutionResult(
    bool Succeeded,
    string Message,
    AddressSource AddressSource,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);
