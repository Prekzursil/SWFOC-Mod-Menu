using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Helper.Config;
using SwfocTrainer.Helper.Services;
using Xunit;
using System.Text.Json;

namespace SwfocTrainer.Tests.Helper;

public sealed class HelperModServiceTests
{
    [Fact]
    public async Task DeployAsync_ShouldCopyDeclaredScripts()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var scriptPath = WriteScript(sourceRoot, "common/spawn_bridge.lua", "-- script body");
            var profile = BuildProfile("base_swfoc", [new HelperHookSpec("spawn", "common/spawn_bridge.lua", "1.0.0")]);
            var service = BuildService(profile, sourceRoot, installRoot);

            var deployedRoot = await service.DeployAsync("base_swfoc", CancellationToken.None);

            deployedRoot.Should().Be(Path.Combine(installRoot, "base_swfoc"));
            var copiedScript = Path.Combine(deployedRoot, "Data", "Scripts", "Library", "common", "spawn_bridge.lua");
            File.Exists(copiedScript).Should().BeTrue();
            File.ReadAllText(copiedScript).Should().Be(File.ReadAllText(scriptPath));
            File.Exists(Path.Combine(deployedRoot, "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua")).Should().BeTrue();
            File.Exists(Path.Combine(deployedRoot, "helper-deployment.json")).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldGenerateBootstrapLoader_WithRequireEntriesForEachHook()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- spawn");
            WriteScript(sourceRoot, "scripts/roe/respawn_bridge.lua", "-- respawn");
            var hooks = new[]
            {
                new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn"),
                new HelperHookSpec("roe_respawn_bridge", "scripts/roe/respawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Toggle_Respawn")
            };

            var service = BuildService(BuildProfile("base_swfoc", hooks), sourceRoot, installRoot);

            var deployedRoot = await service.DeployAsync("base_swfoc", CancellationToken.None);
            var bootstrapPath = Path.Combine(deployedRoot, "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua");

            var bootstrap = File.ReadAllText(bootstrapPath);
            bootstrap.Should().Contain("SWFOC_TRAINER_HELPER_PROFILE = \"base_swfoc\"");
            bootstrap.Should().Contain("SWFOC_TRAINER_HELPER_HOOK_COUNT = 2");
            bootstrap.Should().Contain("SWFOC_TRAINER_HELPER_HOOKS = {");
            bootstrap.Should().Contain("requirePath = \"common.spawn_bridge\"");
            bootstrap.Should().Contain("requirePath = \"roe.respawn_bridge\"");
            bootstrap.Should().Contain("entryPoint = \"SWFOC_Trainer_Spawn\"");
            bootstrap.Should().Contain("entryPoint = \"SWFOC_Trainer_Toggle_Respawn\"");
            bootstrap.Should().Contain("function SwfocTrainer_Helper_Bootstrap_LoadAll()");
            bootstrap.Should().Contain("pcall(require, hook.requirePath)");
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldWriteManifest_WithHookMetadataAndHashes()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var scriptBody = "-- deployed helper";
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", scriptBody);
            var hooks = new[]
            {
                new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.2.3", EntryPoint: "SWFOC_Trainer_Spawn")
            };

            var service = BuildService(BuildProfile("base_swfoc", hooks), sourceRoot, installRoot);

            var deployedRoot = await service.DeployAsync("base_swfoc", CancellationToken.None);
            var manifestPath = Path.Combine(deployedRoot, "helper-deployment.json");
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));

            var root = document.RootElement;
            root.GetProperty("profileId").GetString().Should().Be("base_swfoc");
            root.GetProperty("bootstrapScript").GetString().Should().Be("Data/Scripts/Library/SwfocTrainer_HelperBootstrap.lua");
            var hooksElement = root.GetProperty("hooks");
            hooksElement.GetArrayLength().Should().Be(1);
            var hook = hooksElement[0];
            hook.GetProperty("id").GetString().Should().Be("spawn_bridge");
            hook.GetProperty("script").GetString().Should().Be("scripts/common/spawn_bridge.lua");
            hook.GetProperty("deployedScript").GetString().Should().Be("Data/Scripts/Library/common/spawn_bridge.lua");
            hook.GetProperty("requirePath").GetString().Should().Be("common.spawn_bridge");
            hook.GetProperty("entryPoint").GetString().Should().Be("SWFOC_Trainer_Spawn");
            hook.GetProperty("version").GetString().Should().Be("1.2.3");
            hook.GetProperty("sha256").GetString().Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldThrow_WhenHookSourceMissing()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var profile = BuildProfile("base_swfoc", [new HelperHookSpec("spawn", "missing.lua", "1.0.0")]);
            var service = BuildService(profile, sourceRoot, installRoot);

            var act = () => service.DeployAsync("base_swfoc", CancellationToken.None);

            await act.Should().ThrowAsync<FileNotFoundException>();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnFalse_WhenHookFileMissing()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var profile = BuildProfile("base_swfoc", [new HelperHookSpec("spawn", "common/spawn_bridge.lua", "1.0.0")]);
            var service = BuildService(profile, sourceRoot, installRoot);

            var verified = await service.VerifyAsync("base_swfoc", CancellationToken.None);

            verified.Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnFalse_WhenDeploymentReportIsMissing()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var profile = BuildProfile("base_swfoc", [new HelperHookSpec("spawn", "common/spawn_bridge.lua", "1.0.0")]);
            var targetPath = Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "common", "spawn_bridge.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, "-- deployed script");
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua"), "-- bootstrap");
            var service = BuildService(profile, sourceRoot, installRoot);

            var verified = await service.VerifyAsync("base_swfoc", CancellationToken.None);

            verified.Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnTrue_WhenHookExistsWithoutHashMetadata()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var script = "common/spawn_bridge.lua";
            var profile = BuildProfile("base_swfoc", [new HelperHookSpec("spawn", script, "1.0.0")]);
            var targetPath = Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "common", "spawn_bridge.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, "-- deployed script");
            Directory.CreateDirectory(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library"));
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua"), "-- bootstrap");
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "helper-deployment.json"), """{"profileId":"base_swfoc"}""");
            var service = BuildService(profile, sourceRoot, installRoot);

            var verified = await service.VerifyAsync("base_swfoc", CancellationToken.None);

            verified.Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnFalse_WhenHashDoesNotMatch()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var script = "common/spawn_bridge.lua";
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sha256"] = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            };
            var hook = new HelperHookSpec("spawn", script, "1.0.0", Metadata: metadata);
            var profile = BuildProfile("base_swfoc", [hook]);
            var targetPath = Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "common", "spawn_bridge.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, "-- deployed script");
            Directory.CreateDirectory(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library"));
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua"), "-- bootstrap");
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "helper-deployment.json"), """{"profileId":"base_swfoc"}""");
            var service = BuildService(profile, sourceRoot, installRoot);

            var verified = await service.VerifyAsync("base_swfoc", CancellationToken.None);

            verified.Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnTrue_WhenHashMatches()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var content = "-- deployed script";
            var targetPath = Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "common", "spawn_bridge.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, content);
            Directory.CreateDirectory(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library"));
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua"), "-- bootstrap");
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "helper-deployment.json"), """{"profileId":"base_swfoc"}""");
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)))
                .ToLowerInvariant();

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sha256"] = sha
            };
            var hook = new HelperHookSpec("spawn", "common/spawn_bridge.lua", "1.0.0", Metadata: metadata);
            var profile = BuildProfile("base_swfoc", [hook]);
            var service = BuildService(profile, sourceRoot, installRoot);

            var verified = await service.VerifyAsync("base_swfoc");

            verified.Should().BeTrue();
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

    private static string WriteScript(string sourceRoot, string relativePath, string content)
    {
        var fullPath = Path.Combine(sourceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "swfoctrainer-helper-tests", Guid.NewGuid().ToString("N"));
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
