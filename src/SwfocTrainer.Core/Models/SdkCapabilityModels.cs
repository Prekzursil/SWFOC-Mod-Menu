using System.Text.Json.Nodes;

namespace SwfocTrainer.Core.Models;

/// <summary>
/// Capability state for SDK operations after resolving anchors against a known fingerprint map.
/// </summary>
public enum SdkCapabilityStatus
{
    Available = 0,
    Degraded = 1,
    Unavailable = 2
}

/// <summary>
/// Stable reason-code set for capability resolution and execution gating decisions.
/// </summary>
public enum CapabilityReasonCode
{
    Unknown = 0,
    FeatureFlagDisabled,
    RuntimeNotAttached,
    AllRequiredAnchorsPresent,
    OptionalAnchorsMissing,
    RequiredAnchorsMissing,
    FingerprintMapMissing,
    OperationNotMapped,
    UnknownSdkOperation,
    ModeMismatch,
    RequestedProfileMismatch,
    MutationBlockedByCapabilityState,
    ReadAllowedInDegradedMode,
    ExplicitProfileSelection,
    LaunchContextRecommendation,
    FallbackExeTarget,
    FingerprintDefaultProfile,
    NoProcessDetected
}

/// <summary>
/// Derived identity for a game module build.
/// </summary>
public sealed record BinaryFingerprint(
    string FingerprintId,
    string FileSha256,
    string ModuleName,
    string? ProductVersion,
    string? FileVersion,
    DateTimeOffset TimestampUtc,
    IReadOnlyList<string> ModuleList,
    string SourcePath);

/// <summary>
/// Anchor descriptor used to map SDK operations to known binary capabilities.
/// </summary>
public sealed record CapabilityAnchor(
    string Id,
    string Kind,
    string Pattern,
    bool Required = true,
    string? Notes = null);

/// <summary>
/// Per-operation anchor requirements in a capability map.
/// </summary>
public sealed record CapabilityOperationMap(
    IReadOnlyList<string> RequiredAnchors,
    IReadOnlyList<string> OptionalAnchors);

/// <summary>
/// Optional capability-probe metadata loaded from generated symbol packs/maps.
/// </summary>
public sealed record CapabilityAvailabilityHint(
    string FeatureId,
    bool Available,
    string State,
    string ReasonCode,
    IReadOnlyList<string> RequiredAnchors);

/// <summary>
/// Persisted map keyed by binary fingerprint.
/// </summary>
public sealed record CapabilityMap(
    string SchemaVersion,
    string FingerprintId,
    string? DefaultProfileId,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyDictionary<string, CapabilityOperationMap> Operations,
    IReadOnlyDictionary<string, CapabilityAvailabilityHint> CapabilityHints);

/// <summary>
/// Result of resolving an operation for a profile/fingerprint pair.
/// </summary>
public sealed record CapabilityResolutionResult(
    string ProfileId,
    string OperationId,
    SdkCapabilityStatus State,
    CapabilityReasonCode ReasonCode,
    double Confidence,
    string FingerprintId,
    IReadOnlyList<string> MatchedAnchors,
    IReadOnlyList<string> MissingAnchors);

/// <summary>
/// Guard decision for whether an operation may execute.
/// </summary>
public sealed record SdkExecutionDecision(
    bool Allowed,
    CapabilityReasonCode ReasonCode,
    string Message);

/// <summary>
/// Universal profile resolution outcome.
/// </summary>
public sealed record ProfileVariantResolution(
    string RequestedProfileId,
    string ResolvedProfileId,
    string ReasonCode,
    double Confidence,
    string? FingerprintId = null,
    int? ProcessId = null,
    string? ProcessName = null);

/// <summary>
/// SDK operation request envelope for research-track runtime adapter routing.
/// </summary>
public sealed record SdkOperationRequest(
    string OperationId,
    JsonObject Payload,
    bool IsMutation,
    RuntimeMode RuntimeMode,
    string ProfileId,
    IReadOnlyDictionary<string, object?>? Context = null);

/// <summary>
/// SDK operation execution result envelope.
/// </summary>
public sealed record SdkOperationResult(
    bool Succeeded,
    string Message,
    CapabilityReasonCode ReasonCode,
    SdkCapabilityStatus CapabilityState,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);
