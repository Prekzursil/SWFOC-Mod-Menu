namespace SwfocTrainer.Core.Models;

/// <summary>
/// Per-action compatibility state used by mod promotion checks.
/// </summary>
public sealed record ModActionCompatibility(
    string ActionId,
    ActionReliabilityState State,
    string ReasonCode,
    double Confidence);

/// <summary>
/// Promotion readiness report for a custom mod profile.
/// </summary>
public sealed record ModCompatibilityReport(
    string ProfileId,
    DateTimeOffset GeneratedAtUtc,
    RuntimeMode RuntimeMode,
    DependencyValidationStatus DependencyStatus,
    int UnresolvedCriticalSymbols,
    bool PromotionReady,
    IReadOnlyList<ModActionCompatibility> Actions,
    IReadOnlyList<string> Notes);
