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
    string? Notes = null,
    IReadOnlyList<string>? RequiredCapabilities = null,
    IReadOnlyDictionary<string, string>? AdditionalMetadata = null);

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
/// Discovery-derived launch hints for generated profile seeds.
/// </summary>
public sealed record GeneratedLaunchHints(
    IReadOnlyList<string> SteamModIds,
    IReadOnlyList<string> ModPathHints);

/// <summary>
/// One generated onboarding seed from workshop discovery artifacts.
/// </summary>
public sealed record GeneratedProfileSeed(
    string WorkshopId,
    string Title,
    string CandidateBaseProfile,
    GeneratedLaunchHints LaunchHints,
    IReadOnlyList<string> ParentDependencies,
    IReadOnlyList<string> RequiredCapabilities,
    string SourceRunId,
    double Confidence,
    string RiskLevel,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? AnchorHints = null);

/// <summary>
/// Batch request contract for generating onboarding drafts from discovery seeds.
/// </summary>
public sealed record ModOnboardingSeedBatchRequest(
    IReadOnlyList<GeneratedProfileSeed> Seeds,
    string NamespaceRoot = "custom",
    string FallbackBaseProfileId = "base_swfoc");

/// <summary>
/// Per-seed output result from batch onboarding generation.
/// </summary>
public sealed record ModOnboardingBatchItemResult(
    string WorkshopId,
    string ProfileId,
    string OutputPath,
    bool Succeeded,
    IReadOnlyList<string> Warnings,
    string? Error = null);

/// <summary>
/// Aggregate result for onboarding batch generation.
/// </summary>
public sealed record ModOnboardingBatchResult(
    bool Succeeded,
    int Total,
    int Generated,
    int Failed,
    IReadOnlyList<ModOnboardingBatchItemResult> Items);

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
