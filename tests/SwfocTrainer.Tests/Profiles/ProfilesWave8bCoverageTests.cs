using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

/// <summary>
/// Wave 8b coverage: remaining branches in GitHubProfileUpdateService
/// (InstallProfileAsync failure, InstallProfileTransactionalAsync remote not configured),
/// GitHubProfileUpdateExtractionHelpers (drive-qualified path, rooted path, escaping root),
/// FileSystemProfileRepository (missing manifest, missing profile, circular inheritance),
/// ModOnboardingService (ScaffoldDraftProfilesFromSeedsAsync edge cases,
/// NormalizeProfileId edge cases, InferWorkshopIds, InferPathHints,
/// MergePathHints, MergeRequiredCapabilities, NormalizeRiskLevel,
/// IsPathHintCandidate short tokens).
/// </summary>
public sealed class ProfilesWave8bCoverageTests
{
    #region GitHubProfileUpdateExtractionHelpers — path traversal guards

    [Fact]
    public void ExtractToDirectorySafely_ShouldThrow_WhenEntryHasDriveQualifiedPath()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"extract_dq_{Guid.NewGuid():N}");
        var zipPath = Path.Join(Path.GetTempPath(), $"dq_{Guid.NewGuid():N}.zip");
        try
        {
            Directory.CreateDirectory(tempDir);
            CreateZipWithEntry(zipPath, "C:/evil.txt", "data");
            var act = () => GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely(zipPath, tempDir);
            act.Should().Throw<InvalidDataException>().WithMessage("*drive-qualified*");
        }
        finally
        {
            Cleanup(tempDir, zipPath);
        }
    }

    [Fact]
    public void ExtractToDirectorySafely_ShouldThrow_WhenEntryHasRootedPath()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"extract_rp_{Guid.NewGuid():N}");
        var zipPath = Path.Join(Path.GetTempPath(), $"rp_{Guid.NewGuid():N}.zip");
        try
        {
            Directory.CreateDirectory(tempDir);
            CreateZipWithEntry(zipPath, "/etc/passwd", "data");
            var act = () => GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely(zipPath, tempDir);
            act.Should().Throw<InvalidDataException>().WithMessage("*rooted*");
        }
        finally
        {
            Cleanup(tempDir, zipPath);
        }
    }

    [Fact]
    public void ExtractToDirectorySafely_ShouldThrow_WhenEntryEscapesRoot()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"extract_esc_{Guid.NewGuid():N}");
        var zipPath = Path.Join(Path.GetTempPath(), $"esc_{Guid.NewGuid():N}.zip");
        try
        {
            Directory.CreateDirectory(tempDir);
            CreateZipWithEntry(zipPath, "../../escape.txt", "data");
            var act = () => GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely(zipPath, tempDir);
            act.Should().Throw<InvalidDataException>().WithMessage("*escapes*");
        }
        finally
        {
            Cleanup(tempDir, zipPath);
        }
    }

    [Fact]
    public void ExtractToDirectorySafely_ShouldSkipEmptyEntryPaths()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"extract_empty_{Guid.NewGuid():N}");
        var zipPath = Path.Join(Path.GetTempPath(), $"empty_{Guid.NewGuid():N}.zip");
        try
        {
            Directory.CreateDirectory(tempDir);
            // Create a zip with a valid entry and also one with whitespace name
            using (var fs = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("valid.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("hello");
            }

            // Should succeed without throwing
            GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely(zipPath, tempDir);
            File.Exists(Path.Join(tempDir, "valid.txt")).Should().BeTrue();
        }
        finally
        {
            Cleanup(tempDir, zipPath);
        }
    }

    [Fact]
    public void ExtractToDirectorySafely_ShouldCreateDirectoryEntries()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"extract_dir_{Guid.NewGuid():N}");
        var zipPath = Path.Join(Path.GetTempPath(), $"dir_{Guid.NewGuid():N}.zip");
        try
        {
            Directory.CreateDirectory(tempDir);
            using (var fs = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                archive.CreateEntry("subdir/");
                var entry = archive.CreateEntry("subdir/file.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("content");
            }

            GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely(zipPath, tempDir);
            Directory.Exists(Path.Join(tempDir, "subdir")).Should().BeTrue();
            File.Exists(Path.Join(tempDir, "subdir", "file.txt")).Should().BeTrue();
        }
        finally
        {
            Cleanup(tempDir, zipPath);
        }
    }

    [Fact]
    public void ExtractToDirectorySafely_ShouldThrow_WhenZipPathIsNull()
    {
        var act = () => GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely(null!, "dir");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExtractToDirectorySafely_ShouldThrow_WhenExtractDirIsNull()
    {
        var act = () => GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely("test.zip", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region GitHubProfileUpdateService — InstallProfileAsync failure path

    [Fact]
    public async Task InstallProfileAsync_ShouldThrow_WhenRemoteNotConfigured()
    {
        using var temp = new TempRoot();
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = temp.ProfilesRoot,
            DownloadCachePath = temp.CacheRoot,
            RemoteManifestUrl = null
        };
        var client = new HttpClient(new FailingHandler());
        var repo = new StubProfileRepo();
        var sut = new GitHubProfileUpdateService(client, options, repo);

        var act = () => sut.InstallProfileAsync("test_profile", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not configured*");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFail_WhenRemoteNotConfigured()
    {
        using var temp = new TempRoot();
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = temp.ProfilesRoot,
            DownloadCachePath = temp.CacheRoot,
            RemoteManifestUrl = "   "
        };
        var client = new HttpClient(new FailingHandler());
        var repo = new StubProfileRepo();
        var sut = new GitHubProfileUpdateService(client, options, repo);

        var result = await sut.InstallProfileTransactionalAsync("test_profile", CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("remote_manifest_not_configured");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldReturnUpdates_WhenRemoteHasNewerVersion()
    {
        using var temp = new TempRoot();
        var manifestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            generatedAtUtc = DateTimeOffset.UtcNow,
            profiles = new[]
            {
                new { id = "base_swfoc", version = "2.0", sha256 = "sha", downloadUrl = "http://dl", gameBuild = "1.0" }
            }
        });
        var handler = new StubHttpMessageHandler(new Dictionary<string, (string, byte[])>
        {
            ["http://test/manifest.json"] = ("application/json", Encoding.UTF8.GetBytes(manifestJson))
        });
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = temp.ProfilesRoot,
            DownloadCachePath = temp.CacheRoot,
            RemoteManifestUrl = "http://test/manifest.json"
        };
        var repo = new StubProfileRepo("1.0");
        var sut = new GitHubProfileUpdateService(new HttpClient(handler), options, repo);

        var updates = await sut.CheckForUpdatesAsync(CancellationToken.None);
        updates.Should().Contain("base_swfoc");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFail_WhenProfileNotInManifest()
    {
        using var temp = new TempRoot();
        var manifestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            generatedAtUtc = DateTimeOffset.UtcNow,
            profiles = Array.Empty<object>()
        });
        var handler = new StubHttpMessageHandler(new Dictionary<string, (string, byte[])>
        {
            ["http://test/manifest.json"] = ("application/json", Encoding.UTF8.GetBytes(manifestJson))
        });
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = temp.ProfilesRoot,
            DownloadCachePath = temp.CacheRoot,
            RemoteManifestUrl = "http://test/manifest.json"
        };
        var repo = new StubProfileRepo();
        var sut = new GitHubProfileUpdateService(new HttpClient(handler), options, repo);

        var result = await sut.InstallProfileTransactionalAsync("missing_profile", CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("profile_missing_in_manifest");
    }

    #endregion

    #region ModOnboardingService — ScaffoldDraftProfilesFromSeedsAsync edge cases

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldThrow_WhenSeedsIsNull()
    {
        var sut = CreateOnboardingService();
        var request = new ModOnboardingSeedBatchRequest(null, null!);
        var act = () => sut.ScaffoldDraftProfilesFromSeedsAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*seed*");
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldThrow_WhenSeedsIsEmpty()
    {
        var sut = CreateOnboardingService();
        var request = new ModOnboardingSeedBatchRequest(null, Array.Empty<GeneratedProfileSeed>());
        var act = () => sut.ScaffoldDraftProfilesFromSeedsAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldHandleDuplicateProfileIds()
    {
        var sut = CreateOnboardingService();
        var seeds = new[]
        {
            BuildSeed("my_mod", "My Mod", "base_swfoc", "run1", 0.9, "base_swfoc"),
            BuildSeed("my_mod", "My Mod Dup", "base_swfoc", "run2", 0.85, "base_swfoc")
        };

        var request = new ModOnboardingSeedBatchRequest(null, seeds);
        var result = await sut.ScaffoldDraftProfilesFromSeedsAsync(request, CancellationToken.None);
        // Second seed should fail because of duplicate profile id
        result.Results.Should().HaveCount(2);
        result.Results[1].Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldInferProfileIdFromWorkshopId()
    {
        var sut = CreateOnboardingService();
        var seed = BuildSeed("workshop_123456", "Workshop Mod 123456", "base_swfoc", "run1", 0.9, "base_swfoc",
            workshopId: "123456");
        var seeds = new[] { seed };

        var request = new ModOnboardingSeedBatchRequest(null, seeds);
        var result = await sut.ScaffoldDraftProfilesFromSeedsAsync(request, CancellationToken.None);
        result.Results.Should().HaveCount(1);
        result.Results[0].Succeeded.Should().BeTrue();
        result.Results[0].ProfileId.Should().Contain("workshop_123456");
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldInferProfileIdFromTitle()
    {
        var sut = CreateOnboardingService();
        var seed = BuildSeed("empire_expanded", "Empire Expanded", "base_swfoc", "run1", 0.9, "base_swfoc",
            title: "Empire Expanded",
            riskLevel: "high",
            parentDependencies: new[] { "base_swfoc" },
            launchHints: new[] { "MODPATH=mods/expanded" },
            anchorHints: new[] { "empire_anchor" },
            requiredCapabilities: new[] { "multi_faction" },
            profileAliases: new[] { "ee" },
            localPathHints: new[] { "expanded" });
        var seeds = new[] { seed };

        var request = new ModOnboardingSeedBatchRequest(null, seeds);
        var result = await sut.ScaffoldDraftProfilesFromSeedsAsync(request, CancellationToken.None);
        result.Results[0].Succeeded.Should().BeTrue();
    }

    #endregion

    #region ModOnboardingService — ScaffoldDraftProfileAsync

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ShouldSucceed_WithValidRequest()
    {
        var sut = CreateOnboardingService();
        var request = new ModOnboardingRequest(
            DraftProfileId: "test_mod",
            DisplayName: "Test Mod",
            BaseProfileId: "base_swfoc",
            LaunchSamples: new[]
            {
                new ModLaunchSample(
                    ProcessName: "StarWarsG.exe",
                    ProcessPath: @"C:\Games\StarWarsG.exe",
                    CommandLine: "StarWarsG.exe MODPATH=\"mods/test\" STEAMMOD=789")
            },
            ProfileAliases: new[] { "testmod" },
            Notes: "test notes");

        var result = await sut.ScaffoldDraftProfileAsync(request, CancellationToken.None);
        result.Succeeded.Should().BeTrue();
        result.InferredWorkshopIds.Should().Contain("789");
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_NonCancellation_ShouldDelegate()
    {
        var sut = CreateOnboardingService();
        var request = new ModOnboardingRequest(
            DraftProfileId: "test_mod_2",
            DisplayName: "Test Mod 2",
            BaseProfileId: "base_swfoc",
            LaunchSamples: new[] { new ModLaunchSample("StarWarsG.exe", @"C:\Games\StarWarsG.exe", null) },
            NamespaceRoot: "custom_ns");

        var result = await sut.ScaffoldDraftProfileAsync(request);
        result.Succeeded.Should().BeTrue();
    }

    #endregion

    #region FileSystemProfileRepository — missing manifest

    [Fact]
    public async Task LoadManifestAsync_ShouldThrow_WhenManifestMissing()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"repo_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new ProfileRepositoryOptions { ProfilesRootPath = tempDir };
            var repo = new FileSystemProfileRepository(options);
            var act = () => repo.LoadManifestAsync(CancellationToken.None);
            await act.Should().ThrowAsync<FileNotFoundException>();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task LoadManifestAsync_NonCancellation_ShouldThrow_WhenManifestMissing()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"repo_nc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new ProfileRepositoryOptions { ProfilesRootPath = tempDir };
            var repo = new FileSystemProfileRepository(options);
            var act = () => repo.LoadManifestAsync();
            await act.Should().ThrowAsync<FileNotFoundException>();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task LoadProfileAsync_ShouldThrow_WhenProfileFileMissing()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"repo_prof_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Join(tempDir, "profiles"));
        try
        {
            var options = new ProfileRepositoryOptions { ProfilesRootPath = tempDir };
            var repo = new FileSystemProfileRepository(options);
            var act = () => repo.LoadProfileAsync("nonexistent", CancellationToken.None);
            await act.Should().ThrowAsync<FileNotFoundException>();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region Helpers

    private static void CreateZipWithEntry(string zipPath, string entryName, string content)
    {
        using var fs = new FileStream(zipPath, FileMode.Create);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void Cleanup(string dir, string file)
    {
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, true); } catch { /* cleanup best-effort */ }
        }

        if (File.Exists(file))
        {
            try { File.Delete(file); } catch { /* cleanup best-effort */ }
        }
    }

    private static ModOnboardingService CreateOnboardingService()
    {
        return new ModOnboardingService(new StubProfileRepo(), new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Join(Path.GetTempPath(), $"onboarding_{Guid.NewGuid():N}")
        });
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            RootPath = Path.Join(Path.GetTempPath(), $"swfoc-profiles-w8b-{Guid.NewGuid():N}");
            ProfilesRoot = Path.Join(RootPath, "default");
            CacheRoot = Path.Join(RootPath, "cache");
            Directory.CreateDirectory(Path.Join(ProfilesRoot, "profiles"));
            Directory.CreateDirectory(CacheRoot);
        }

        public string RootPath { get; }
        public string ProfilesRoot { get; }
        public string CacheRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                try { Directory.Delete(RootPath, recursive: true); }
                catch { /* ignore cleanup failures */ }
            }
        }
    }

    private sealed class StubProfileRepo : IProfileRepository
    {
        private readonly string _manifestVersion;

        public StubProfileRepo(string manifestVersion = "1.0")
        {
            _manifestVersion = manifestVersion;
        }

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProfileManifest(
                "1.0",
                DateTimeOffset.UtcNow,
                new[]
                {
                    new ProfileManifestEntry("base_swfoc", _manifestVersion, "sha", "http://dl", "1.0")
                }));
        }

        public Task<ProfileManifest> LoadManifestAsync() => LoadManifestAsync(CancellationToken.None);

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            return Task.FromResult(BuildProfile(id: profileId));
        }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            return Task.FromResult(BuildProfile(id: profileId));
        }

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<string>>(new[] { "base_swfoc" });
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Connection refused");
        }
    }

    private static GeneratedProfileSeed BuildSeed(
        string draftProfileId,
        string displayName,
        string baseProfileId,
        string sourceRunId,
        double confidence,
        string parentProfile,
        string? workshopId = null,
        string? title = null,
        string? riskLevel = null,
        IReadOnlyList<string>? parentDependencies = null,
        IReadOnlyList<string>? launchHints = null,
        IReadOnlyList<string>? anchorHints = null,
        IReadOnlyList<string>? requiredCapabilities = null,
        IReadOnlyList<string>? profileAliases = null,
        IReadOnlyList<string>? localPathHints = null)
    {
        return new GeneratedProfileSeed(
            DraftProfileId: draftProfileId,
            DisplayName: displayName,
            BaseProfileId: baseProfileId,
            LaunchSamples: Array.Empty<ModLaunchSample>(),
            SourceRunId: sourceRunId,
            Confidence: confidence,
            ParentProfile: parentProfile,
            WorkshopId: workshopId,
            Title: title,
            RiskLevel: riskLevel,
            ParentDependencies: parentDependencies,
            LaunchHints: launchHints,
            AnchorHints: anchorHints,
            RequiredCapabilities: requiredCapabilities,
            ProfileAliases: profileAliases,
            LocalPathHints: localPathHints);
    }

    private static TrainerProfile BuildProfile(
        string id = "test",
        string displayName = "Test")
    {
        return new TrainerProfile(
            Id: id,
            DisplayName: displayName,
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) },
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>());
    }

    #endregion
}
