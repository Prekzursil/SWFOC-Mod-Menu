using System.IO.Compression;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class SupportBundleServiceTests
{
    [Fact]
    public async Task SupportBundleService_ShouldExportZipAndManifest()
    {
        var runtime = CreateAttachedRuntime();
        var telemetry = CreateTelemetry();
        var runId = await CreateRunFixtureAsync();
        var outputDir = Path.Combine(Path.GetTempPath(), $"swfoc-support-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        try
        {
            var service = new SupportBundleService(runtime, telemetry);
            var result = await service.ExportAsync(new SupportBundleRequest(
                OutputDirectory: outputDir,
                ProfileId: "custom_test",
                Notes: "test",
                MaxRecentRuns: 1));

            AssertBundleResult(result);
        }
        finally
        {
            DeleteDirectoryIfExists(outputDir);
            DeleteDirectoryIfExists(Path.Combine(Directory.GetCurrentDirectory(), "TestResults", "runs", runId));
        }
    }

    private static StubRuntimeAdapter CreateAttachedRuntime()
    {
        return new StubRuntimeAdapter
        {
            CurrentSession = new AttachSession(
                "custom_test",
                new ProcessMetadata(999, "StarWarsG.exe", @"C:\Games\StarWarsG.exe", "STEAMMOD=555000111", ExeTarget.Swfoc, RuntimeMode.Galactic),
                new ProfileBuild("custom_test", "test", @"C:\Games\StarWarsG.exe", ExeTarget.Swfoc, "STEAMMOD=555000111", 999),
                new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["credits"] = new("credits", 0x1111, SymbolValueType.Int32, AddressSource.Signature, "ok", 0.9, SymbolHealthStatus.Healthy, "healthy", DateTimeOffset.UtcNow)
                }),
                DateTimeOffset.UtcNow)
        };
    }

    private static TelemetrySnapshotService CreateTelemetry()
    {
        var telemetry = new TelemetrySnapshotService();
        telemetry.RecordAction("set_credits", AddressSource.Signature, true);
        return telemetry;
    }

    private static async Task<string> CreateRunFixtureAsync()
    {
        var runId = $"support-bundle-test-{Guid.NewGuid():N}";
        var runRoot = Path.Combine(Directory.GetCurrentDirectory(), "TestResults", "runs", runId);
        Directory.CreateDirectory(runRoot);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "repro-bundle.json"), "{\"schemaVersion\":\"1.1\"}");
        await File.WriteAllTextAsync(Path.Combine(runRoot, "repro-bundle.md"), "# repro");
        return runId;
    }

    private static void AssertBundleResult(SupportBundleResult result)
    {
        result.Succeeded.Should().BeTrue();
        File.Exists(result.BundlePath).Should().BeTrue();
        File.Exists(result.ManifestPath).Should().BeTrue();

        using var archive = ZipFile.OpenRead(result.BundlePath);
        archive.Entries.Should().Contain(e => e.FullName.Equals("runtime-snapshot.json", StringComparison.OrdinalIgnoreCase));
        archive.Entries.Should().Contain(e => e.FullName.Contains("telemetry", StringComparison.OrdinalIgnoreCase));
        archive.Entries.Should().Contain(e => e.FullName.Contains("runs", StringComparison.OrdinalIgnoreCase));
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class StubRuntimeAdapter : IRuntimeAdapter
    {
        public bool IsAttached => CurrentSession is not null;
        public AttachSession? CurrentSession { get; set; }
        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken = default)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(CurrentSession!);
        }

        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken = default) where T : unmanaged
        {
            _ = symbol;
            _ = cancellationToken;
            throw new NotImplementedException();
        }

        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken = default) where T : unmanaged
        {
            _ = symbol;
            _ = value;
            _ = cancellationToken;
            throw new NotImplementedException();
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotImplementedException();
        }

        public Task DetachAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
