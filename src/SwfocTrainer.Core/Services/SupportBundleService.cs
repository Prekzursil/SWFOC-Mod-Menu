using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class SupportBundleService : ISupportBundleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IRuntimeAdapter _runtime;
    private readonly ITelemetrySnapshotService _telemetry;

    public SupportBundleService(IRuntimeAdapter runtime, ITelemetrySnapshotService telemetry)
    {
        _runtime = runtime;
        _telemetry = telemetry;
    }

    public async Task<SupportBundleResult> ExportAsync(SupportBundleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new InvalidDataException("Output directory is required.");
        }

        Directory.CreateDirectory(request.OutputDirectory);

        var paths = BuildBundlePaths(request.OutputDirectory);

        var included = new List<string>();
        var warnings = new List<string>();

        if (Directory.Exists(paths.StagingRoot))
        {
            Directory.Delete(paths.StagingRoot, recursive: true);
        }

        Directory.CreateDirectory(paths.StagingRoot);

        try
        {
            CopyLogs(paths.StagingRoot, included, warnings);
            CopyCalibrationArtifacts(paths.StagingRoot, included, warnings);
            CopyRecentReproBundles(paths.StagingRoot, included, warnings, request.MaxRecentRuns);
            await WriteRuntimeSnapshotAsync(paths.StagingRoot, included, warnings, request.ProfileId, request.Notes, cancellationToken);
            await WriteTelemetrySnapshotAsync(paths.StagingRoot, included, cancellationToken);

            await WriteManifestFilesAsync(paths, request, included, warnings, cancellationToken);
            included.Add("manifest.json");

            if (File.Exists(paths.BundlePath))
            {
                File.Delete(paths.BundlePath);
            }

            ZipFile.CreateFromDirectory(paths.StagingRoot, paths.BundlePath, CompressionLevel.Optimal, includeBaseDirectory: false);

            return new SupportBundleResult(
                Succeeded: true,
                BundlePath: paths.BundlePath,
                ManifestPath: paths.ManifestPath,
                IncludedFiles: included.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                Warnings: warnings);
        }
        finally
        {
            if (Directory.Exists(paths.StagingRoot))
            {
                Directory.Delete(paths.StagingRoot, recursive: true);
            }
        }
    }

    public Task<SupportBundleResult> ExportAsync(SupportBundleRequest request)
    {
        return ExportAsync(request, CancellationToken.None);
    }

    private static void CopyLogs(string stagingRoot, List<string> included, List<string> warnings)
    {
        var logsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwfocTrainer",
            "logs");

        if (!Directory.Exists(logsRoot))
        {
            warnings.Add("Log directory not found in LocalAppData.");
            return;
        }

        var targetRoot = Path.Combine(stagingRoot, "logs");
        Directory.CreateDirectory(targetRoot);
        foreach (var path in Directory
                     .GetFiles(logsRoot, "*.jsonl", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(10))
        {
            var fileName = Path.GetFileName(path);
            var dest = Path.Combine(targetRoot, fileName);
            File.Copy(path, dest, overwrite: true);
            included.Add($"logs/{fileName}");
        }
    }

    private static void CopyCalibrationArtifacts(string stagingRoot, List<string> included, List<string> warnings)
    {
        var calibrationRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwfocTrainer",
            "calibration");

        if (!Directory.Exists(calibrationRoot))
        {
            warnings.Add("Calibration artifact directory not found in LocalAppData.");
            return;
        }

        var targetRoot = Path.Combine(stagingRoot, "calibration");
        Directory.CreateDirectory(targetRoot);
        foreach (var path in Directory
                     .GetFiles(calibrationRoot, "*.json", SearchOption.AllDirectories)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(8))
        {
            var fileName = Path.GetFileName(path);
            var dest = Path.Combine(targetRoot, fileName);
            File.Copy(path, dest, overwrite: true);
            included.Add($"calibration/{fileName}");
        }
    }

    private static void CopyRecentReproBundles(string stagingRoot, List<string> included, List<string> warnings, int maxRecentRuns)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var runRoot = Path.Combine(repoRoot, "TestResults", "runs");
        if (!Directory.Exists(runRoot))
        {
            warnings.Add("TestResults/runs directory not found.");
            return;
        }

        var runDirs = Directory.GetDirectories(runRoot)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .Take(Math.Max(maxRecentRuns, 1))
            .ToArray();

        var targetRoot = Path.Combine(stagingRoot, "runs");
        Directory.CreateDirectory(targetRoot);

        foreach (var runDir in runDirs)
        {
            var runId = Path.GetFileName(runDir);
            var targetRun = Path.Combine(targetRoot, runId);
            Directory.CreateDirectory(targetRun);

            foreach (var name in new[] { "repro-bundle.json", "repro-bundle.md" })
            {
                var source = Path.Combine(runDir, name);
                if (!File.Exists(source))
                {
                    continue;
                }

                var dest = Path.Combine(targetRun, name);
                File.Copy(source, dest, overwrite: true);
                included.Add($"runs/{runId}/{name}");
            }
        }
    }

    private async Task WriteRuntimeSnapshotAsync(
        string stagingRoot,
        List<string> included,
        List<string> warnings,
        string? profileId,
        string? notes,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(stagingRoot, "runtime-snapshot.json");

        if (_runtime.CurrentSession is null)
        {
            await WriteRuntimeSnapshotJsonAsync(path, BuildDetachedSnapshot(profileId, notes), cancellationToken);
            included.Add("runtime-snapshot.json");
            warnings.Add("Runtime adapter is not attached; snapshot contains no live process state.");
            return;
        }

        var session = _runtime.CurrentSession;
        var symbolSummary = session.Symbols.Symbols.Values
            .GroupBy(x => x.HealthStatus)
            .ToDictionary(x => x.Key.ToString(), x => x.Count(), StringComparer.OrdinalIgnoreCase);
        await WriteRuntimeSnapshotJsonAsync(
            path,
            BuildAttachedSnapshot(session, profileId, notes, symbolSummary),
            cancellationToken);
        included.Add("runtime-snapshot.json");
    }

    private async Task WriteTelemetrySnapshotAsync(string stagingRoot, List<string> included, CancellationToken cancellationToken)
    {
        var telemetryDir = Path.Combine(stagingRoot, "telemetry");
        Directory.CreateDirectory(telemetryDir);
        var telemetryPath = await _telemetry.ExportSnapshotAsync(telemetryDir, cancellationToken);
        included.Add($"telemetry/{Path.GetFileName(telemetryPath)}");
    }

    private static BundlePaths BuildBundlePaths(string outputDirectory)
    {
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        return new BundlePaths(
            Path.Combine(outputDirectory, $"support-bundle-{runId}"),
            Path.Combine(outputDirectory, $"support-bundle-{runId}.zip"),
            Path.Combine(outputDirectory, $"support-bundle-{runId}.manifest.json"));
    }

    private async Task WriteManifestFilesAsync(
        BundlePaths paths,
        SupportBundleRequest request,
        IReadOnlyList<string> included,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        var manifestPayload = new
        {
            schemaVersion = "1.1",
            generatedAtUtc = DateTimeOffset.UtcNow,
            profileId = request.ProfileId,
            notes = request.Notes,
            includedFiles = included.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings
        };

        var manifestJson = JsonSerializer.Serialize(manifestPayload, JsonOptions);
        await File.WriteAllTextAsync(paths.ManifestPath, manifestJson, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(paths.StagingRoot, "manifest.json"), manifestJson, cancellationToken);
    }

    private static object BuildDetachedSnapshot(string? profileId, string? notes)
    {
        return new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            profileId,
            notes,
            attached = false
        };
    }

    private static object BuildAttachedSnapshot(
        AttachSession session,
        string? profileId,
        string? notes,
        IReadOnlyDictionary<string, int> symbolSummary)
    {
        return new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            profileId = profileId ?? session.ProfileId,
            notes,
            attached = true,
            process = new
            {
                session.Process.ProcessId,
                session.Process.ProcessName,
                session.Process.ProcessPath,
                launchKind = session.Process.LaunchContext?.LaunchKind.ToString() ?? "Unknown",
                launchReasonCode = session.Process.LaunchContext?.Recommendation.ReasonCode ?? "unknown",
                launchConfidence = session.Process.LaunchContext?.Recommendation.Confidence ?? 0.0d
            },
            runtimeMode = session.Process.Mode.ToString(),
            symbolHealthSummary = symbolSummary
        };
    }

    private static Task WriteRuntimeSnapshotJsonAsync(string path, object payload, CancellationToken cancellationToken)
    {
        return File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
    }

    private sealed record BundlePaths(string StagingRoot, string BundlePath, string ManifestPath);
}
