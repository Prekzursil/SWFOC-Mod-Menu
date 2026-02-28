namespace SwfocTrainer.Core.Models;

/// <summary>
/// One launch sample captured from a mod session.
/// </summary>
public sealed record ModLaunchSample(
    string? ProcessName,
    string? ProcessPath,
    string? CommandLine);

/// <summary>
/// Input contract for generating a draft custom-mod profile.
/// </summary>
public sealed record ModOnboardingRequest(
    string DraftProfileId,
    string DisplayName,
    string BaseProfileId,
    IReadOnlyList<ModLaunchSample> LaunchSamples,
    IReadOnlyList<string>? ProfileAliases = null,
    string? NamespaceRoot = null,
    string? Notes = null);

/// <summary>
/// Scaffold result for custom-mod onboarding.
/// </summary>
public sealed record ModOnboardingResult(
    bool Succeeded,
    string ProfileId,
    string OutputPath,
    IReadOnlyList<string> InferredWorkshopIds,
    IReadOnlyList<string> InferredPathHints,
    IReadOnlyList<string> InferredAliases,
    IReadOnlyList<string> Warnings);

/// <summary>
/// One generated seed from discovery or telemetry used to scaffold a profile draft.
/// </summary>
public sealed record GeneratedProfileSeed(
    string DraftProfileId,
    string DisplayName,
    string BaseProfileId,
    IReadOnlyList<ModLaunchSample> LaunchSamples,
    string SourceRunId,
    double Confidence,
    string ParentProfile,
    IReadOnlyList<string>? RequiredWorkshopIds = null,
    IReadOnlyList<string>? ProfileAliases = null,
    IReadOnlyList<string>? LocalPathHints = null,
    string? Notes = null,
    string? WorkshopId = null,
    IReadOnlyList<string>? RequiredCapabilities = null,
    IReadOnlyList<string>? AnchorHints = null,
    string? RiskLevel = null,
    IReadOnlyList<string>? ParentDependencies = null,
    IReadOnlyList<string>? LaunchHints = null,
    string? Title = null,
    string? CandidateBaseProfile = null);

/// <summary>
/// Batch onboarding request for generated profile seeds.
/// </summary>
public sealed record ModOnboardingSeedBatchRequest(
    string? TargetNamespaceRoot,
    IReadOnlyList<GeneratedProfileSeed> Seeds);

/// <summary>
/// Per-seed onboarding result for fail-soft batch ingestion.
/// </summary>
public sealed record ModOnboardingBatchItemResult(
    int Index,
    string SeedProfileId,
    bool Succeeded,
    string? ProfileId,
    string? OutputPath,
    IReadOnlyList<string> InferredWorkshopIds,
    IReadOnlyList<string> InferredPathHints,
    IReadOnlyList<string> InferredAliases,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

/// <summary>
/// Aggregate onboarding summary for generated seed batches.
/// </summary>
public sealed record ModOnboardingBatchResult(
    bool Succeeded,
    int Attempted,
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<ModOnboardingBatchItemResult> Results);

/// <summary>
/// Input contract for writing calibration artifacts.
/// </summary>
public sealed record ModCalibrationArtifactRequest(
    string ProfileId,
    string OutputDirectory,
    AttachSession? Session,
    string? OperatorNotes = null);

/// <summary>
/// One candidate symbol entry inside a calibration artifact.
/// </summary>
public sealed record CalibrationCandidate(
    string Symbol,
    string Source,
    string HealthStatus,
    double Confidence,
    string? Notes = null);

/// <summary>
/// Result of calibration artifact export.
/// </summary>
public sealed record ModCalibrationArtifactResult(
    bool Succeeded,
    string ArtifactPath,
    string ModuleFingerprint,
    IReadOnlyList<CalibrationCandidate> Candidates,
    IReadOnlyList<string> Warnings);
