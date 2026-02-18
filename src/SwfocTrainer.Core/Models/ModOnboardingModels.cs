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
