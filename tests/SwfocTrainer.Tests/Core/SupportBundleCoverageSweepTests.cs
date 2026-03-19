using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class SupportBundleCoverageSweepTests
{
    [Fact]
    public async Task ExportAsync_ShouldThrow_WhenOutputDirectoryMissing()
    {
        var service = new SupportBundleService(new StubRuntimeAdapter(), new TelemetrySnapshotService());

        var act = async () => await service.ExportAsync(new SupportBundleRequest(OutputDirectory: ""));

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("Output directory is required.");
    }

    [Fact]
    public async Task ExportAsync_ShouldWriteDetachedSnapshotAndCleanStagingDirectory()
    {
        using var outputDir = new TempDirectory("swfoc-support-bundle");
        using var runFixture = await RunFixture.CreateAsync();
        var telemetry = new TelemetrySnapshotService();
        telemetry.RecordAction("spawn_tactical_entity", AddressSource.Signature, succeeded: true);
        var service = new SupportBundleService(new StubRuntimeAdapter(), telemetry);

        var result = await service.ExportAsync(new SupportBundleRequest(
            OutputDirectory: outputDir.Path,
            ProfileId: "custom_profile",
            Notes: "detached",
            MaxRecentRuns: 1));

        result.Succeeded.Should().BeTrue();
        result.Warnings.Should().Contain(x => x.Contains("not attached", StringComparison.OrdinalIgnoreCase));
        result.IncludedFiles.Should().Contain("runtime-snapshot.json");
        result.IncludedFiles.Should().Contain("manifest.json");
        result.IncludedFiles.Should().Contain(path => path.EndsWith("/repro-bundle.json", StringComparison.OrdinalIgnoreCase));
        result.IncludedFiles.Should().Contain(path => path.EndsWith("/repro-bundle.md", StringComparison.OrdinalIgnoreCase));
        Directory.GetDirectories(outputDir.Path, "support-bundle-*").Should().BeEmpty();

        using var archive = ZipFile.OpenRead(result.BundlePath);
        archive.Entries.Should().Contain(x => x.FullName == "runtime-snapshot.json");
        archive.Entries.Should().Contain(x => x.FullName.EndsWith("/repro-bundle.json", StringComparison.OrdinalIgnoreCase));
        archive.Entries.Should().Contain(x => x.FullName.EndsWith("/repro-bundle.md", StringComparison.OrdinalIgnoreCase));

        using var runtimeStream = archive.GetEntry("runtime-snapshot.json")!.Open();
        var runtimeJson = await JsonSerializer.DeserializeAsync<JsonElement>(runtimeStream);
        runtimeJson.GetProperty("attached").GetBoolean().Should().BeFalse();
    }

    private sealed class StubRuntimeAdapter : IRuntimeAdapter
    {
        public bool IsAttached => false;

        public AttachSession? CurrentSession => null;

        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged
        {
            _ = symbol;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged
        {
            _ = symbol;
            _ = value;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task DetachAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class RunFixture : IDisposable
    {
        private RunFixture(string runRoot, string runId)
        {
            RunRoot = runRoot;
            RunId = runId;
        }

        public string RunRoot { get; }

        public string RunId { get; }

        public static async Task<RunFixture> CreateAsync()
        {
            var runId = $"support-sweep-{Guid.NewGuid():N}";
            var runRoot = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "TestResults", "runs", runId);
            Directory.CreateDirectory(runRoot);
            await File.WriteAllTextAsync(System.IO.Path.Combine(runRoot, "repro-bundle.json"), "{\"schemaVersion\":\"1.1\"}");
            await File.WriteAllTextAsync(System.IO.Path.Combine(runRoot, "repro-bundle.md"), "# repro");
            return new RunFixture(runRoot, runId);
        }

        public void Dispose()
        {
            if (Directory.Exists(RunRoot))
            {
                Directory.Delete(RunRoot, recursive: true);
            }
        }
    }
}
