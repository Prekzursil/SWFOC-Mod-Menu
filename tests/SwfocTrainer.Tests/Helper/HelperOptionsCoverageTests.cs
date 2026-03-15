using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Helper.Config;
using SwfocTrainer.Helper.Services;
using Xunit;

namespace SwfocTrainer.Tests.Helper;

public sealed class HelperOptionsCoverageTests
{
    [Fact]
    public void HelperModOptions_ShouldExposeNonEmptyDefaultRoots()
    {
        var options = new HelperModOptions();

        options.SourceRoot.Should().NotBeNullOrWhiteSpace();
        options.SourceRoot.Should().Contain("profiles");
        options.InstallRoot.Should().NotBeNullOrWhiteSpace();
        options.InstallRoot.Should().Contain("SwfocTrainer");
    }

    [Fact]
    public async Task DeployAsync_ConvenienceOverload_ShouldUseNoneCancellation()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            WriteScript(sourceRoot, "common/spawn_bridge.lua", "-- script body");
            var profile = BuildProfile("base_swfoc", [new HelperHookSpec("spawn", "common/spawn_bridge.lua", "1.0.0")]);
            var service = BuildService(profile, sourceRoot, installRoot);

            var deployedRoot = await service.DeployAsync("base_swfoc");

            deployedRoot.Should().Be(Path.Combine(installRoot, "base_swfoc"));
            File.Exists(Path.Combine(deployedRoot, "Data", "Scripts", "Library", "common", "spawn_bridge.lua")).Should().BeTrue();
            File.Exists(Path.Combine(deployedRoot, "helper-deployment.json")).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    private static HelperModService BuildService(TrainerProfile profile, string sourceRoot, string installRoot)
    {
        var repository = new StubProfileRepository(profile);
        var options = new HelperModOptions
        {
            SourceRoot = sourceRoot,
            InstallRoot = installRoot
        };

        return new HelperModService(repository, options, NullLogger<HelperModService>.Instance);
    }

    private static TrainerProfile BuildProfile(string profileId, IReadOnlyList<HelperHookSpec> hooks)
    {
        return new TrainerProfile(
            Id: profileId,
            DisplayName: profileId,
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "save-schema",
            HelperModHooks: hooks);
    }

    private static void WriteScript(string sourceRoot, string relativePath, string content)
    {
        var fullPath = Path.Combine(sourceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "swfoctrainer-helper-coverage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class StubProfileRepository : IProfileRepository
    {
        private readonly TrainerProfile _profile;

        public StubProfileRepository(TrainerProfile profile)
        {
            _profile = profile;
        }

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_profile);
        }

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
        {
            _ = profile;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            throw new NotSupportedException();
        }
    }
}
