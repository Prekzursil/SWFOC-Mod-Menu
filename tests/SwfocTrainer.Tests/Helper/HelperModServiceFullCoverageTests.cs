using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Helper.Config;
using SwfocTrainer.Helper.Services;
using Xunit;

namespace SwfocTrainer.Tests.Helper;

/// <summary>
/// Full branch coverage for HelperModService: Deploy, Verify, null guards,
/// file-not-found, hash mismatch, no-sha metadata, convenience overloads.
/// </summary>
public sealed class HelperModServiceFullCoverageTests
{
    [Fact]
    public void Ctor_ShouldThrow_WhenProfilesIsNull()
    {
        var act = () => new HelperModService(null!, new HelperModOptions(), NullLogger<HelperModService>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_ShouldThrow_WhenOptionsIsNull()
    {
        var act = () => new HelperModService(new StubProfileRepo(), null!, NullLogger<HelperModService>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_ShouldThrow_WhenLoggerIsNull()
    {
        var act = () => new HelperModService(new StubProfileRepo(), new HelperModOptions(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task DeployAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.DeployAsync(null!));
    }

    [Fact]
    public async Task DeployAsync_ShouldCopyScriptsToTargetRoot()
    {
        var tempDir = CreateTempDir();
        try
        {
            var sourceRoot = Path.Join(tempDir, "source");
            var installRoot = Path.Join(tempDir, "install");
            Directory.CreateDirectory(sourceRoot);
            await File.WriteAllTextAsync(Path.Join(sourceRoot, "hook.lua"), "print('hello')");

            var profile = BuildProfile(new[]
            {
                new HelperHookSpec("hook1", "hook.lua", "1.0")
            });

            var service = CreateService(profile, sourceRoot, installRoot);
            var result = await service.DeployAsync("test_profile");

            result.Should().Contain("test_profile");
            File.Exists(Path.Join(installRoot, "test_profile", "hook.lua")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldThrow_WhenSourceScriptMissing()
    {
        var tempDir = CreateTempDir();
        try
        {
            var sourceRoot = Path.Join(tempDir, "source");
            var installRoot = Path.Join(tempDir, "install");
            Directory.CreateDirectory(sourceRoot);
            // Don't create the script file

            var profile = BuildProfile(new[]
            {
                new HelperHookSpec("hook1", "missing.lua", "1.0")
            });

            var service = CreateService(profile, sourceRoot, installRoot);
            await Assert.ThrowsAsync<FileNotFoundException>(() => service.DeployAsync("test_profile"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task DeployAsync_ConvenienceOverload_ShouldWork()
    {
        var tempDir = CreateTempDir();
        try
        {
            var sourceRoot = Path.Join(tempDir, "source");
            var installRoot = Path.Join(tempDir, "install");
            Directory.CreateDirectory(sourceRoot);
            await File.WriteAllTextAsync(Path.Join(sourceRoot, "hook.lua"), "print('test')");

            var profile = BuildProfile(new[]
            {
                new HelperHookSpec("hook1", "hook.lua", "1.0")
            });

            var service = CreateService(profile, sourceRoot, installRoot);
            var result = await service.DeployAsync("test_profile", CancellationToken.None);
            result.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldCreateSubdirectories_ForNestedScripts()
    {
        var tempDir = CreateTempDir();
        try
        {
            var sourceRoot = Path.Join(tempDir, "source");
            var installRoot = Path.Join(tempDir, "install");
            Directory.CreateDirectory(Path.Join(sourceRoot, "scripts"));
            await File.WriteAllTextAsync(Path.Join(sourceRoot, "scripts", "hook.lua"), "print('nested')");

            var profile = BuildProfile(new[]
            {
                new HelperHookSpec("hook1", Path.Join("scripts", "hook.lua"), "1.0")
            });

            var service = CreateService(profile, sourceRoot, installRoot);
            await service.DeployAsync("test_profile");
            File.Exists(Path.Join(installRoot, "test_profile", "scripts", "hook.lua")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.VerifyAsync(null!));
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnFalse_WhenScriptMissing()
    {
        var tempDir = CreateTempDir();
        try
        {
            var installRoot = Path.Join(tempDir, "install");

            var profile = BuildProfile(new[]
            {
                new HelperHookSpec("hook1", "missing.lua", "1.0")
            });

            var service = CreateService(profile, tempDir, installRoot);
            var result = await service.VerifyAsync("test_profile");
            result.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnTrue_WhenScriptExistsAndNoSha()
    {
        var tempDir = CreateTempDir();
        try
        {
            var installRoot = Path.Join(tempDir, "install");
            Directory.CreateDirectory(Path.Join(installRoot, "test_profile"));
            await File.WriteAllTextAsync(Path.Join(installRoot, "test_profile", "hook.lua"), "print('ok')");

            var profile = BuildProfile(new[]
            {
                new HelperHookSpec("hook1", "hook.lua", "1.0")
            });

            var service = CreateService(profile, tempDir, installRoot);
            var result = await service.VerifyAsync("test_profile");
            result.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnTrue_WhenShaMatches()
    {
        var tempDir = CreateTempDir();
        try
        {
            var installRoot = Path.Join(tempDir, "install");
            Directory.CreateDirectory(Path.Join(installRoot, "test_profile"));
            var hookContent = "print('verified')";
            await File.WriteAllTextAsync(Path.Join(installRoot, "test_profile", "hook.lua"), hookContent);

            var expectedSha = ComputeSha256(Path.Join(installRoot, "test_profile", "hook.lua"));

            var profile = BuildProfile(new[]
            {
                new HelperHookSpec("hook1", "hook.lua", "1.0",
                    Metadata: new Dictionary<string, string> { ["sha256"] = expectedSha })
            });

            var service = CreateService(profile, tempDir, installRoot);
            var result = await service.VerifyAsync("test_profile");
            result.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnFalse_WhenShaMismatch()
    {
        var tempDir = CreateTempDir();
        try
        {
            var installRoot = Path.Join(tempDir, "install");
            Directory.CreateDirectory(Path.Join(installRoot, "test_profile"));
            await File.WriteAllTextAsync(Path.Join(installRoot, "test_profile", "hook.lua"), "print('tampered')");

            var profile = BuildProfile(new[]
            {
                new HelperHookSpec("hook1", "hook.lua", "1.0",
                    Metadata: new Dictionary<string, string> { ["sha256"] = "0000000000000000" })
            });

            var service = CreateService(profile, tempDir, installRoot);
            var result = await service.VerifyAsync("test_profile");
            result.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldSkipHash_WhenShaIsWhitespace()
    {
        var tempDir = CreateTempDir();
        try
        {
            var installRoot = Path.Join(tempDir, "install");
            Directory.CreateDirectory(Path.Join(installRoot, "test_profile"));
            await File.WriteAllTextAsync(Path.Join(installRoot, "test_profile", "hook.lua"), "print('ok')");

            var profile = BuildProfile(new[]
            {
                new HelperHookSpec("hook1", "hook.lua", "1.0",
                    Metadata: new Dictionary<string, string> { ["sha256"] = "  " })
            });

            var service = CreateService(profile, tempDir, installRoot);
            var result = await service.VerifyAsync("test_profile");
            result.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldSkipHash_WhenMetadataIsNull()
    {
        var tempDir = CreateTempDir();
        try
        {
            var installRoot = Path.Join(tempDir, "install");
            Directory.CreateDirectory(Path.Join(installRoot, "test_profile"));
            await File.WriteAllTextAsync(Path.Join(installRoot, "test_profile", "hook.lua"), "print('ok')");

            var profile = BuildProfile(new[]
            {
                new HelperHookSpec("hook1", "hook.lua", "1.0", Metadata: null)
            });

            var service = CreateService(profile, tempDir, installRoot);
            var result = await service.VerifyAsync("test_profile");
            result.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task VerifyAsync_ConvenienceOverload_ShouldWork()
    {
        var tempDir = CreateTempDir();
        try
        {
            var installRoot = Path.Join(tempDir, "install");
            var profile = BuildProfile(Array.Empty<HelperHookSpec>());
            var service = CreateService(profile, tempDir, installRoot);
            var result = await service.VerifyAsync("test_profile", CancellationToken.None);
            result.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static HelperModService CreateService(
        TrainerProfile? profile = null,
        string? sourceRoot = null,
        string? installRoot = null)
    {
        profile ??= BuildProfile(Array.Empty<HelperHookSpec>());
        var options = new HelperModOptions
        {
            SourceRoot = sourceRoot ?? Path.GetTempPath(),
            InstallRoot = installRoot ?? Path.GetTempPath()
        };

        return new HelperModService(
            new StubProfileRepo(profile),
            options,
            NullLogger<HelperModService>.Instance);
    }

    private static TrainerProfile BuildProfile(IReadOnlyList<HelperHookSpec> hooks)
    {
        return new TrainerProfile(
            "test_profile", "Test", null, ExeTarget.Swfoc, null,
            Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(), new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(), "test_schema", hooks);
    }

    private static string CreateTempDir()
    {
        var path = Path.Join(Path.GetTempPath(), $"helper-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ComputeSha256(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private sealed class StubProfileRepo : IProfileRepository
    {
        private readonly TrainerProfile _profile;
        public StubProfileRepo(TrainerProfile? profile = null)
        {
            _profile = profile ?? new TrainerProfile(
                "test", "Test", null, ExeTarget.Swfoc, null,
                Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
                new Dictionary<string, ActionSpec>(), new Dictionary<string, bool>(),
                Array.Empty<CatalogSource>(), "test", Array.Empty<HelperHookSpec>());
        }

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(_profile);

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(_profile);

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { _profile.Id });
    }
}
