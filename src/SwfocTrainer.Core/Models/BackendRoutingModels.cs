using System.Text.Json.Nodes;

namespace SwfocTrainer.Core.Models;

/// <summary>
/// Execution backend identity for tiered routing.
/// </summary>
public enum ExecutionBackendKind
{
    Unknown = 0,
    Extender,
    Helper,
    Memory,
    Save
}

public enum CapabilityConfidenceState
{
    Unknown = 0,
    Experimental,
    Verified
}

public sealed record BackendCapability(
    string FeatureId,
    bool Available,
    CapabilityConfidenceState Confidence,
    RuntimeReasonCode ReasonCode,
    string? Notes = null);

public sealed record CapabilityReport(
    string ProfileId,
    DateTimeOffset ProbedAtUtc,
    IReadOnlyDictionary<string, BackendCapability> Capabilities,
    RuntimeReasonCode ProbeReasonCode,
    IReadOnlyDictionary<string, object?>? Diagnostics = null)
{
    public static CapabilityReport Unknown(string profileId)
    {
        return Unknown(profileId, RuntimeReasonCode.CAPABILITY_UNKNOWN);
    }

    public static CapabilityReport Unknown(string profileId, RuntimeReasonCode reasonCode)
    {
        return new CapabilityReport(
            profileId,
            DateTimeOffset.UtcNow,
            new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase),
            reasonCode);
    }

    public bool IsFeatureAvailable(string featureId)
    {
        return Capabilities.TryGetValue(featureId, out var capability) && capability.Available;
    }
}

public sealed record BackendHealth(
    string BackendId,
    ExecutionBackendKind Backend,
    bool IsHealthy,
    RuntimeReasonCode ReasonCode,
    string Message,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);

public sealed record BackendRouteDecision(
    bool Allowed,
    ExecutionBackendKind Backend,
    RuntimeReasonCode ReasonCode,
    string Message,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);

public sealed record ExtenderCommand(
    string CommandId,
    string FeatureId,
    string ProfileId,
    RuntimeMode Mode,
    JsonObject Payload,
    int ProcessId,
    string ProcessName,
    JsonObject ResolvedAnchors,
    string RequestedBy,
    DateTimeOffset TimestampUtc);

public sealed record ExtenderResult(
    string CommandId,
    bool Succeeded,
    RuntimeReasonCode ReasonCode,
    string Backend,
    string HookState,
    string Message,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);
