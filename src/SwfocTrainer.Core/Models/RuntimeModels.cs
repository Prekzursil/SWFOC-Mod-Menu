namespace SwfocTrainer.Core.Models;

/// <summary>
/// Runtime process descriptor discovered by <c>IProcessLocator</c>.
/// Metadata keys currently used by the app/runtime:
/// detectedVia, commandLineAvailable, isStarWarsG, steamModIdsDetected,
/// dependencyValidation, dependencyValidationMessage, dependencyDisabledActions.
/// </summary>
public sealed record ProcessMetadata(
    int ProcessId,
    string ProcessName,
    string ProcessPath,
    string? CommandLine,
    ExeTarget ExeTarget,
    RuntimeMode Mode,
    IReadOnlyDictionary<string, string>? Metadata = null,
    LaunchContext? LaunchContext = null,
    ProcessHostRole HostRole = ProcessHostRole.Unknown,
    int MainModuleSize = 0,
    int WorkshopMatchCount = 0,
    double SelectionScore = 0d);

public sealed record ProfileRecommendation(
    string? ProfileId,
    string ReasonCode,
    double Confidence);

public sealed record LaunchContext(
    LaunchKind LaunchKind,
    bool CommandLineAvailable,
    IReadOnlyList<string> SteamModIds,
    string? ModPathRaw,
    string? ModPathNormalized,
    string DetectedVia,
    ProfileRecommendation Recommendation,
    string Source = "detected");

public sealed record DependencyValidationResult(
    DependencyValidationStatus Status,
    string Message,
    IReadOnlySet<string> DisabledActionIds);

public sealed record SymbolValidationResult(
    SymbolHealthStatus Status,
    string Reason,
    double Confidence,
    bool IsCritical = false);

public sealed record AttachSession(
    string ProfileId,
    ProcessMetadata Process,
    ProfileBuild Build,
    SymbolMap Symbols,
    DateTimeOffset AttachedAt);
