using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Profiles.Validation;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

/// <summary>
/// Wave 8 coverage: remaining branches in GitHubProfileUpdateService, ModOnboardingService,
/// ProfileValidator — constructor null guards, CheckForUpdates edge cases,
/// InstallProfile failures, rollback paths, ScaffoldDraftProfile validation, ProfileValidator rules.
/// </summary>
public sealed class ProfilesWave8CoverageTests
{
    #region GitHubProfileUpdateService — constructor null guards

    [Fact]
    public void Constructor_ShouldThrow_WhenHttpClientIsNull()
    {
        using var temp = new TempRoot();
        var act = () => new GitHubProfileUpdateService(
            null!,
            CreateOptions(temp),
            new StubProfileRepo());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenOptionsIsNull()
    {
        var act = () => new GitHubProfileUpdateService(
            new HttpClient(),
            null!,
            new StubProfileRepo());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenRepositoryIsNull()
    {
        using var temp = new TempRoot();
        var act = () => new GitHubProfileUpdateService(
            new HttpClient(),
            CreateOptions(temp),
            null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region GitHubProfileUpdateService — CheckForUpdates

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldReturnEmpty_WhenRemoteManifestUrlIsNull()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo(), remoteManifestUrl: null);

        var updates = await service.CheckForUpdatesAsync(CancellationToken.None);
        updates.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldReturnEmpty_WhenRemoteManifestUrlIsWhitespace()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo(), remoteManifestUrl: "   ");

        var updates = await service.CheckForUpdatesAsync(CancellationToken.None);
        updates.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldReturnUpdates_WhenRemoteHasNewerVersion()
    {
        using var temp = new TempRoot();
        var manifest = new ProfileManifest(
            "1.0",
            DateTimeOffset.UtcNow,
            new[] { new ProfileManifestEntry("base_swfoc", "2.0", "abc", "http://dl", "1.0") });
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var handler = ManifestHandler(json);
        var repo = new StubProfileRepo(manifestVersion: "1.0");
        var service = CreateUpdateService(temp, handler, repo);

        var updates = await service.CheckForUpdatesAsync(CancellationToken.None);
        updates.Should().Contain("base_swfoc");
    }

    #endregion

    #region GitHubProfileUpdateService — InstallProfile

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFail_WhenNoRemoteManifestUrl()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo(), remoteManifestUrl: null);

        var result = await service.InstallProfileTransactionalAsync("base_swfoc", CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("remote_manifest_not_configured");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFail_WhenManifestFetchThrows()
    {
        using var temp = new TempRoot();
        var handler = new FailingHandler();
        var service = CreateUpdateService(temp, handler, new StubProfileRepo());

        var result = await service.InstallProfileTransactionalAsync("base_swfoc", CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("manifest_fetch_failed");
    }

    [Fact]
    public async Task InstallProfileAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo());

        var act = async () => await service.InstallProfileAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo());

        var act = async () => await service.InstallProfileTransactionalAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region GitHubProfileUpdateService — Rollback

    [Fact]
    public async Task RollbackLastInstallAsync_ShouldReturnNotRestored_WhenNoBackupExists()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo());

        var result = await service.RollbackLastInstallAsync("nonexistent_profile", CancellationToken.None);
        result.Restored.Should().BeFalse();
        result.ReasonCode.Should().Be("backup_not_found");
    }

    [Fact]
    public async Task RollbackLastInstallAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo());

        var act = async () => await service.RollbackLastInstallAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region GitHubProfileUpdateService — parameterless overloads

    [Fact]
    public async Task CheckForUpdatesAsync_ParameterlessOverload_ShouldDelegate()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo(), remoteManifestUrl: null);

        var updates = await service.CheckForUpdatesAsync();
        updates.Should().BeEmpty();
    }

    [Fact]
    public async Task InstallProfileAsync_ParameterlessOverload_ShouldThrowOnFailure()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo(), remoteManifestUrl: null);

        var act = async () => await service.InstallProfileAsync("test");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ParameterlessOverload_ShouldDelegate()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo(), remoteManifestUrl: null);

        var result = await service.InstallProfileTransactionalAsync("test");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RollbackLastInstallAsync_ParameterlessOverload_ShouldDelegate()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo());

        var result = await service.RollbackLastInstallAsync("test");
        result.Restored.Should().BeFalse();
    }

    #endregion

    #region ModOnboardingService — constructor null guards

    [Fact]
    public void ModOnboarding_Constructor_ShouldThrow_WhenProfilesIsNull()
    {
        var act = () => new ModOnboardingService(null!, new ProfileRepositoryOptions());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ModOnboarding_Constructor_ShouldThrow_WhenOptionsIsNull()
    {
        var act = () => new ModOnboardingService(new StubProfileRepo(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ModOnboardingService — ScaffoldDraftProfile validation

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ShouldThrow_WhenRequestIsNull()
    {
        var service = CreateOnboardingService();
        var act = async () => await service.ScaffoldDraftProfileAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ShouldThrow_WhenDraftProfileIdIsWhitespace()
    {
        var service = CreateOnboardingService();
        var request = new ModOnboardingRequest(
            DraftProfileId: "   ",
            DisplayName: "Test",
            BaseProfileId: "base_swfoc",
            LaunchSamples: new[] { new ModLaunchSample("swg.exe", @"C:\Games\swg.exe", "STEAMMOD=123") });

        var act = async () => await service.ScaffoldDraftProfileAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ShouldThrow_WhenDisplayNameIsWhitespace()
    {
        var service = CreateOnboardingService();
        var request = new ModOnboardingRequest(
            DraftProfileId: "test_mod",
            DisplayName: "   ",
            BaseProfileId: "base_swfoc",
            LaunchSamples: new[] { new ModLaunchSample("swg.exe", @"C:\Games\swg.exe", "STEAMMOD=123") });

        var act = async () => await service.ScaffoldDraftProfileAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ShouldThrow_WhenLaunchSamplesIsEmpty()
    {
        var service = CreateOnboardingService();
        var request = new ModOnboardingRequest(
            DraftProfileId: "test_mod",
            DisplayName: "Test Mod",
            BaseProfileId: "base_swfoc",
            LaunchSamples: Array.Empty<ModLaunchSample>());

        var act = async () => await service.ScaffoldDraftProfileAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ShouldThrow_WhenLaunchSamplesIsNull()
    {
        var service = CreateOnboardingService();
        var request = new ModOnboardingRequest(
            DraftProfileId: "test_mod",
            DisplayName: "Test Mod",
            BaseProfileId: "base_swfoc",
            LaunchSamples: null!);

        var act = async () => await service.ScaffoldDraftProfileAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    #endregion

    #region ProfileValidator — all rules

    [Fact]
    public void Validate_ShouldThrow_WhenProfileIsNull()
    {
        var act = () => ProfileValidator.Validate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_ShouldThrow_WhenIdIsEmpty()
    {
        var profile = BuildProfile(id: "");
        var act = () => ProfileValidator.Validate(profile);
        act.Should().Throw<InvalidDataException>().WithMessage("*id*");
    }

    [Fact]
    public void Validate_ShouldThrow_WhenDisplayNameIsEmpty()
    {
        var profile = BuildProfile(displayName: "");
        var act = () => ProfileValidator.Validate(profile);
        act.Should().Throw<InvalidDataException>().WithMessage("*displayName*");
    }

    [Fact]
    public void Validate_ShouldThrow_WhenExeTargetIsUnknown()
    {
        var profile = BuildProfile(exeTarget: ExeTarget.Unknown);
        var act = () => ProfileValidator.Validate(profile);
        act.Should().Throw<InvalidDataException>().WithMessage("*exeTarget*");
    }

    [Fact]
    public void Validate_ShouldThrow_WhenNoSignatureSetsAndNoInherits()
    {
        var profile = new TrainerProfile(
            Id: "test",
            DisplayName: "Test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>());
        var act = () => ProfileValidator.Validate(profile);
        act.Should().Throw<InvalidDataException>().WithMessage("*signature*");
    }

    [Fact]
    public void Validate_ShouldThrow_WhenSaveSchemaIdIsEmpty()
    {
        var profile = BuildProfile(saveSchemaId: "");
        var act = () => ProfileValidator.Validate(profile);
        act.Should().Throw<InvalidDataException>().WithMessage("*saveSchemaId*");
    }

    [Fact]
    public void Validate_ShouldNotThrow_ForValidProfile()
    {
        var profile = BuildProfile();
        var act = () => ProfileValidator.Validate(profile);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldThrow_WhenBackendPreferenceIsInvalid()
    {
        var profile = BuildProfile(backendPreference: "invalid_backend");
        var act = () => ProfileValidator.Validate(profile);
        act.Should().Throw<InvalidDataException>().WithMessage("*backendPreference*");
    }

    [Fact]
    public void Validate_ShouldNotThrow_WhenBackendPreferenceIsValid()
    {
        var profile = BuildProfile(backendPreference: "auto");
        var act = () => ProfileValidator.Validate(profile);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldThrow_WhenHostPreferenceIsInvalid()
    {
        var profile = BuildProfile(hostPreference: "invalid_host");
        var act = () => ProfileValidator.Validate(profile);
        act.Should().Throw<InvalidDataException>().WithMessage("*hostPreference*");
    }

    [Fact]
    public void Validate_ShouldNotThrow_WhenHostPreferenceIsValid()
    {
        var profile = BuildProfile(hostPreference: "any");
        var act = () => ProfileValidator.Validate(profile);
        act.Should().NotThrow();
    }

    #endregion

    #region Helpers

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            RootPath = Path.Join(Path.GetTempPath(), $"swfoc-profiles-w8-{Guid.NewGuid():N}");
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

    private static ProfileRepositoryOptions CreateOptions(TempRoot temp, string? remoteManifestUrl = "http://test/manifest.json")
    {
        return new ProfileRepositoryOptions
        {
            ProfilesRootPath = temp.ProfilesRoot,
            DownloadCachePath = temp.CacheRoot,
            RemoteManifestUrl = remoteManifestUrl
        };
    }

    private static GitHubProfileUpdateService CreateUpdateService(
        TempRoot temp,
        HttpMessageHandler handler,
        IProfileRepository repo,
        string? remoteManifestUrl = "http://test/manifest.json")
    {
        var options = CreateOptions(temp, remoteManifestUrl);
        return new GitHubProfileUpdateService(new HttpClient(handler), options, repo);
    }

    private static ModOnboardingService CreateOnboardingService()
    {
        return new ModOnboardingService(new StubProfileRepo(), new ProfileRepositoryOptions());
    }

    private static HttpMessageHandler EmptyHandler()
    {
        return new StubHttpMessageHandler(new Dictionary<string, (string, byte[])>());
    }

    private static HttpMessageHandler ManifestHandler(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return new StubHttpMessageHandler(new Dictionary<string, (string, byte[])>
        {
            ["http://test/manifest.json"] = ("application/json", bytes)
        });
    }

    private static TrainerProfile BuildProfile(
        string id = "test",
        string displayName = "Test",
        ExeTarget exeTarget = ExeTarget.Swfoc,
        string saveSchemaId = "schema",
        string? backendPreference = null,
        string? hostPreference = null)
    {
        return new TrainerProfile(
            Id: id,
            DisplayName: displayName,
            Inherits: null,
            ExeTarget: exeTarget,
            SteamWorkshopId: null,
            SignatureSets: new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) },
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: saveSchemaId,
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            BackendPreference: backendPreference,
            HostPreference: hostPreference);
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

    #endregion
}
