namespace SwfocTrainer.Core.Models;

/// <summary>
/// Input contract for support bundle export.
/// </summary>
/// <param name="OutputDirectory">Required. Where the bundle archive is written.</param>
/// <param name="ProfileId">Optional profile id to embed in the bundle.</param>
/// <param name="Notes">Optional free-form notes recorded in the manifest.</param>
/// <param name="MaxRecentRuns">How many recent repro-bundle runs to include.</param>
/// <param name="WorkingDirectoryOverride">
/// Optional explicit working directory used to locate <c>TestResults/runs</c>.
/// When null, falls back to <see cref="System.IO.Directory.GetCurrentDirectory"/>.
/// Tests use this to avoid contention on process-global current directory state.
/// </param>
public sealed record SupportBundleRequest(
    string OutputDirectory,
    string? ProfileId = null,
    string? Notes = null,
    int MaxRecentRuns = 5,
    string? WorkingDirectoryOverride = null);

/// <summary>
/// Result of support bundle export.
/// </summary>
public sealed record SupportBundleResult(
    bool Succeeded,
    string BundlePath,
    string ManifestPath,
    IReadOnlyList<string> IncludedFiles,
    IReadOnlyList<string> Warnings);
