namespace SwfocTrainer.Core.Models;

/// <summary>
/// Input contract for support bundle export.
/// </summary>
public sealed record SupportBundleRequest(
    string OutputDirectory,
    string? ProfileId = null,
    string? Notes = null,
    int MaxRecentRuns = 5);

/// <summary>
/// Result of support bundle export.
/// </summary>
public sealed record SupportBundleResult(
    bool Succeeded,
    string BundlePath,
    string ManifestPath,
    IReadOnlyList<string> IncludedFiles,
    IReadOnlyList<string> Warnings);
