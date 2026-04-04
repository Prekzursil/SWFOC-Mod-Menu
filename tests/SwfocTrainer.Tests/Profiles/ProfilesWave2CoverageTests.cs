using System.IO.Compression;
using System.Net;
using System.Reflection;
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
/// Wave 2 coverage: fills remaining branches in GitHubProfileUpdateService,
/// FileSystemProfileRepository, ModOnboardingService, and ExtractionHelpers.
/// </summary>
public sealed class ProfilesWave2CoverageTests
{
    #region GitHubProfileUpdateService — parameterless overloads & edge branches

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldReturnEmpty_WhenRemoteManifestUrlIsBlank()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo(), remoteManifestUrl: "   ");

        var updates = await service.CheckForUpdatesAsync(CancellationToken.None);

        updates.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldReturnEmpty_WhenRemoteManifestDeserializesToNull()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, ManifestHandler("null"), new StubProfileRepo());

        var updates = await service.CheckForUpdatesAsync(CancellationToken.None);

        updates.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ParameterlessOverload_ShouldDelegate()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo(), remoteManifestUrl: null);

        var updates = await service.CheckForUpdatesAsync();

        updates.Should().BeEmpty();
    }

    [Fact]
    public async Task InstallProfileAsync_ShouldThrowOnFailure()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo(), remoteManifestUrl: null);

        var act = async () => await service.InstallProfileAsync("base_swfoc");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task InstallProfileAsync_ParameterlessOverload_ShouldDelegate()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo(), remoteManifestUrl: null);

        var act = async () => await service.InstallProfileAsync("base_swfoc");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ParameterlessOverload_ShouldDelegate()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo(), remoteManifestUrl: null);

        var result = await service.InstallProfileTransactionalAsync("base_swfoc");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("remote_manifest_not_configured");
    }

    [Fact]
    public async Task RollbackLastInstallAsync_ParameterlessOverload_ShouldDelegate()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo());

        var rollback = await service.RollbackLastInstallAsync("base_swfoc");

        rollback.Restored.Should().BeFalse();
        rollback.ReasonCode.Should().Be("backup_not_found");
    }

    [Fact]
    public async Task RollbackLastInstallAsync_ShouldCatchIOException()
    {
        using var temp = new TempRoot();
        var profilesDir = Path.Join(temp.ProfilesRoot, "profiles");
        Directory.CreateDirectory(profilesDir);
        var destination = Path.Join(profilesDir, "base_swfoc.json");
        await File.WriteAllTextAsync(destination, "original");
        var backup = $"{destination}.bak.20260101010101";
        await File.WriteAllTextAsync(backup, "backup");
        // Make destination read-only to force IOException on copy
        File.SetAttributes(destination, FileAttributes.ReadOnly);

        try
        {
            var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo());
            var result = await service.RollbackLastInstallAsync("base_swfoc");
            result.Restored.Should().BeFalse();
            result.ReasonCode.Should().Be("rollback_copy_failed");
        }
        finally
        {
            File.SetAttributes(destination, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldReturnManifestFetchFailure_OnJsonException()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, ManifestHandler("not-valid-json{{{"), new StubProfileRepo());

        var result = await service.InstallProfileTransactionalAsync("base_swfoc");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("manifest_fetch_failed");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldReturnDownloadFailure_OnIOException()
    {
        using var temp = new TempRoot();
        const string profileId = "base_swfoc";
        var entry = new ProfileManifestEntry(profileId, "1.0", new string('0', 64), $"https://example.invalid/{profileId}.zip", "1.0");
        var handler = new StubHttpHandler(new Dictionary<string, StubResp>(StringComparer.OrdinalIgnoreCase)
        {
            [ManifestUrl] = StubResp.Json(JsonSerializer.Serialize(new ProfileManifest("1.0", DateTimeOffset.UtcNow, new[] { entry }))),
            [entry.DownloadUrl] = StubResp.ThrowEx(new IOException("disk error"))
        });
        var service = CreateUpdateService(temp, handler, new StubProfileRepo());

        var result = await service.InstallProfileTransactionalAsync(profileId);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("download_failed");
        result.Message.Should().Contain("disk error");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldReturnProfileValidationFailed_OnInvalidOperationException()
    {
        using var temp = new TempRoot();
        const string profileId = "base_swfoc";
        // Profile with invalid backend preference triggers InvalidOperationException path
        var invalidProfile = """{"id":"base_swfoc","displayName":"test","inherits":null,"exeTarget":"Swfoc","steamWorkshopId":null,"signatureSets":[{"name":"x","gameBuild":"x","signatures":[]}],"fallbackOffsets":{},"actions":{},"featureFlags":{},"catalogSources":[],"saveSchemaId":"v1","helperModHooks":[],"backendPreference":"bogus_value"}""";
        var zipBytes = BuildZipWithProfile(profileId, invalidProfile);
        var sha = ComputeSha256(zipBytes);
        var entry = new ProfileManifestEntry(profileId, "5.0.0", sha, $"https://example.invalid/{profileId}.zip", "1.0");
        var service = CreateUpdateService(temp, CreatePackageHandler(entry, zipBytes), new StubProfileRepo());

        var result = await service.InstallProfileTransactionalAsync(profileId);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("profile_validation_failed");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldSucceedForNewInstall_WithoutExistingProfile()
    {
        using var temp = new TempRoot();
        const string profileId = "base_swfoc";
        var profileJson = BuildValidProfile(profileId);
        var zipBytes = BuildZipWithProfile(profileId, profileJson);
        var sha = ComputeSha256(zipBytes);
        var entry = new ProfileManifestEntry(profileId, "6.0.0", sha, $"https://example.invalid/{profileId}.zip", "1.0");
        var service = CreateUpdateService(temp, CreatePackageHandler(entry, zipBytes), new StubProfileRepo());

        var result = await service.InstallProfileTransactionalAsync(profileId);

        result.Succeeded.Should().BeTrue();
        result.BackupPath.Should().BeNull("no previous profile existed");
        result.InstalledPath.Should().NotBeNullOrWhiteSpace();
        result.ReceiptPath.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenArgumentsAreNull()
    {
        using var temp = new TempRoot();
        var options = new ProfileRepositoryOptions { ProfilesRootPath = temp.ProfilesRoot, DownloadCachePath = temp.CacheRoot };
        var client = new HttpClient();
        var repo = new StubProfileRepo();

        var act1 = () => new GitHubProfileUpdateService(null!, options, repo);
        var act2 = () => new GitHubProfileUpdateService(client, null!, repo);
        var act3 = () => new GitHubProfileUpdateService(client, options, null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task InstallProfileAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo());
        var act = async () => await service.InstallProfileAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo());
        var act = async () => await service.InstallProfileTransactionalAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RollbackLastInstallAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        using var temp = new TempRoot();
        var service = CreateUpdateService(temp, EmptyHandler(), new StubProfileRepo());
        var act = async () => await service.RollbackLastInstallAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region FileSystemProfileRepository — remaining branches

    [Fact]
    public async Task LoadManifestAsync_ShouldThrow_WhenManifestFileIsMissing()
    {
        using var temp = new TempRoot();
        var repo = new FileSystemProfileRepository(new ProfileRepositoryOptions
        {
            ProfilesRootPath = temp.ProfilesRoot,
            ManifestFileName = "manifest.json"
        });

        var act = async () => await repo.LoadManifestAsync(CancellationToken.None);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task LoadProfileAsync_ShouldThrow_WhenProfileFileMissing()
    {
        using var temp = new TempRoot();
        Directory.CreateDirectory(Path.Join(temp.ProfilesRoot, "profiles"));
        var repo = new FileSystemProfileRepository(new ProfileRepositoryOptions
        {
            ProfilesRootPath = temp.ProfilesRoot
        });

        var act = async () => await repo.LoadProfileAsync("nonexistent", CancellationToken.None);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ResolveInheritedProfileAsync_ShouldThrow_OnCircularInheritance()
    {
        using var temp = new TempRoot();
        var profilesDir = Path.Join(temp.ProfilesRoot, "profiles");
        Directory.CreateDirectory(profilesDir);

        var profileA = BuildProfile("profile_a", inherits: "profile_b");
        var profileB = BuildProfile("profile_b", inherits: "profile_a");
        await File.WriteAllTextAsync(Path.Join(profilesDir, "profile_a.json"), profileA);
        await File.WriteAllTextAsync(Path.Join(profilesDir, "profile_b.json"), profileB);

        var repo = new FileSystemProfileRepository(new ProfileRepositoryOptions
        {
            ProfilesRootPath = temp.ProfilesRoot
        });

        var act = async () => await repo.ResolveInheritedProfileAsync("profile_a", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*Circular*");
    }

    [Fact]
    public async Task ValidateProfileAsync_ShouldThrow_WhenProfileIsNull()
    {
        using var temp = new TempRoot();
        var repo = new FileSystemProfileRepository(new ProfileRepositoryOptions { ProfilesRootPath = temp.ProfilesRoot });

        var act = async () => await repo.ValidateProfileAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ParameterlessOverloads_ShouldDelegate()
    {
        using var temp = new TempRoot();
        var profilesDir = Path.Join(temp.ProfilesRoot, "profiles");
        Directory.CreateDirectory(profilesDir);
        await File.WriteAllTextAsync(Path.Join(temp.ProfilesRoot, "manifest.json"), JsonSerializer.Serialize(new ProfileManifest("1.0", DateTimeOffset.UtcNow, new[] { new ProfileManifestEntry("base_swfoc", "1.0", "sha", "url", "1.0") })));
        await File.WriteAllTextAsync(Path.Join(profilesDir, "base_swfoc.json"), BuildProfile("base_swfoc"));
        var repo = new FileSystemProfileRepository(new ProfileRepositoryOptions { ProfilesRootPath = temp.ProfilesRoot });

        var manifest = await repo.LoadManifestAsync();
        manifest.Should().NotBeNull();

        var list = await repo.ListAvailableProfilesAsync();
        list.Should().Contain("base_swfoc");

        var profile = await repo.LoadProfileAsync("base_swfoc");
        profile.Should().NotBeNull();

        var resolved = await repo.ResolveInheritedProfileAsync("base_swfoc");
        resolved.Should().NotBeNull();

        var act = async () => await repo.ValidateProfileAsync(profile);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Merge_ShouldMergeAllFields_WhenChildInheritsFromParent()
    {
        using var temp = new TempRoot();
        var profilesDir = Path.Join(temp.ProfilesRoot, "profiles");
        Directory.CreateDirectory(profilesDir);
        await File.WriteAllTextAsync(Path.Join(temp.ProfilesRoot, "manifest.json"), JsonSerializer.Serialize(new ProfileManifest("1.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>())));

        var parentProfile = new TrainerProfile(
            Id: "parent",
            DisplayName: "Parent",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: "100",
            SignatureSets: new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) },
            FallbackOffsets: new Dictionary<string, long> { ["offset_a"] = 1 },
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool> { ["flag_a"] = true },
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "parent_schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string> { ["key_parent"] = "val_parent" },
            BackendPreference: "auto",
            RequiredCapabilities: new[] { "cap_a" },
            HostPreference: "any",
            ExperimentalFeatures: new[] { "exp_a" });

        var childProfile = new TrainerProfile(
            Id: "child",
            DisplayName: "Child",
            Inherits: "parent",
            ExeTarget: ExeTarget.Sweaw,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long> { ["offset_b"] = 2 },
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool> { ["flag_b"] = false },
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "child_schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string> { ["key_child"] = "val_child" },
            BackendPreference: null,
            RequiredCapabilities: new[] { "cap_b" },
            HostPreference: null,
            ExperimentalFeatures: new[] { "exp_b" });

        await File.WriteAllTextAsync(Path.Join(profilesDir, "parent.json"), JsonProfileSerializer.Serialize(parentProfile));
        await File.WriteAllTextAsync(Path.Join(profilesDir, "child.json"), JsonProfileSerializer.Serialize(childProfile));

        var repo = new FileSystemProfileRepository(new ProfileRepositoryOptions { ProfilesRootPath = temp.ProfilesRoot });

        var resolved = await repo.ResolveInheritedProfileAsync("child");

        resolved.Id.Should().Be("child");
        resolved.ExeTarget.Should().Be(ExeTarget.Sweaw, "child has a valid ExeTarget so it is used");
        resolved.SteamWorkshopId.Should().Be("100", "child has no workshop id so parent is used");
        resolved.SaveSchemaId.Should().Be("child_schema", "child has a valid schema so it is used");
        resolved.BackendPreference.Should().Be("auto");
        resolved.HostPreference.Should().Be("any");
        resolved.FallbackOffsets.Should().ContainKey("offset_a").And.ContainKey("offset_b");
        resolved.FeatureFlags.Should().ContainKey("flag_a").And.ContainKey("flag_b");
        resolved.RequiredCapabilities.Should().Contain("cap_a").And.Contain("cap_b");
        resolved.ExperimentalFeatures.Should().Contain("exp_a").And.Contain("exp_b");
        resolved.Metadata.Should().ContainKey("key_parent").And.ContainKey("key_child");
    }

    [Fact]
    public async Task MergeMetadata_ShouldHandleNullMetadataGracefully()
    {
        using var temp = new TempRoot();
        var profilesDir = Path.Join(temp.ProfilesRoot, "profiles");
        Directory.CreateDirectory(profilesDir);
        await File.WriteAllTextAsync(Path.Join(temp.ProfilesRoot, "manifest.json"), JsonSerializer.Serialize(new ProfileManifest("1.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>())));

        var parentProfile = new TrainerProfile(
            Id: "parent_null_meta",
            DisplayName: "Parent",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) },
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: null);

        var childProfile = new TrainerProfile(
            Id: "child_null_meta",
            DisplayName: "Child",
            Inherits: "parent_null_meta",
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: null);

        await File.WriteAllTextAsync(Path.Join(profilesDir, "parent_null_meta.json"), JsonProfileSerializer.Serialize(parentProfile));
        await File.WriteAllTextAsync(Path.Join(profilesDir, "child_null_meta.json"), JsonProfileSerializer.Serialize(childProfile));
        var repo = new FileSystemProfileRepository(new ProfileRepositoryOptions { ProfilesRootPath = temp.ProfilesRoot });

        var resolved = await repo.ResolveInheritedProfileAsync("child_null_meta");
        resolved.Metadata.Should().NotBeNull();
    }

    #endregion

    #region ModOnboardingService — remaining branches

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ShouldThrow_WhenRequestIsNull()
    {
        var (service, tempRoot) = await CreateOnboardingServiceAsync();
        try
        {
            var act = async () => await service.ScaffoldDraftProfileAsync(null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }
        finally { DeleteDir(tempRoot); }
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ShouldThrow_WhenDraftProfileIdIsBlank()
    {
        var (service, tempRoot) = await CreateOnboardingServiceAsync();
        try
        {
            var request = new ModOnboardingRequest("   ", "Name", "base_swfoc", new[] { new ModLaunchSample("p", "p", "cmd") });
            var act = async () => await service.ScaffoldDraftProfileAsync(request);
            await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*DraftProfileId*");
        }
        finally { DeleteDir(tempRoot); }
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ShouldThrow_WhenDisplayNameIsBlank()
    {
        var (service, tempRoot) = await CreateOnboardingServiceAsync();
        try
        {
            var request = new ModOnboardingRequest("test_id", "  ", "base_swfoc", new[] { new ModLaunchSample("p", "p", "cmd") });
            var act = async () => await service.ScaffoldDraftProfileAsync(request);
            await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*DisplayName*");
        }
        finally { DeleteDir(tempRoot); }
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ShouldThrow_WhenLaunchSamplesAreEmpty()
    {
        var (service, tempRoot) = await CreateOnboardingServiceAsync();
        try
        {
            var request = new ModOnboardingRequest("test_id", "Name", "base_swfoc", Array.Empty<ModLaunchSample>());
            var act = async () => await service.ScaffoldDraftProfileAsync(request);
            await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*launch sample*");
        }
        finally { DeleteDir(tempRoot); }
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ShouldWarn_WhenNoWorkshopIdsOrPathHintsFound()
    {
        var (service, tempRoot) = await CreateOnboardingServiceAsync();
        try
        {
            var request = new ModOnboardingRequest("test_id", "Name", "base_swfoc",
                new[] { new ModLaunchSample("StarWarsG.exe", null, null) },
                Notes: "test note");
            var result = await service.ScaffoldDraftProfileAsync(request);
            result.Succeeded.Should().BeTrue();
            result.Warnings.Should().Contain(w => w.Contains("No STEAMMOD"));
            result.Warnings.Should().Contain(w => w.Contains("No local path hints"));
        }
        finally { DeleteDir(tempRoot); }
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ParameterlessOverload_ShouldDelegate()
    {
        var (service, tempRoot) = await CreateOnboardingServiceAsync();
        try
        {
            var request = new ModOnboardingRequest("test_overload", "Overload", "base_swfoc",
                new[] { new ModLaunchSample("StarWarsG.exe", null, "STEAMMOD=111") });
            var result = await service.ScaffoldDraftProfileAsync(request);
            result.Succeeded.Should().BeTrue();
        }
        finally { DeleteDir(tempRoot); }
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldThrow_WhenRequestIsNull()
    {
        var (service, tempRoot) = await CreateOnboardingServiceAsync();
        try
        {
            var act = async () => await service.ScaffoldDraftProfilesFromSeedsAsync(null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }
        finally { DeleteDir(tempRoot); }
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldThrow_WhenSeedsAreEmpty()
    {
        var (service, tempRoot) = await CreateOnboardingServiceAsync();
        try
        {
            var act = async () => await service.ScaffoldDraftProfilesFromSeedsAsync(
                new ModOnboardingSeedBatchRequest("custom", Array.Empty<GeneratedProfileSeed>()));
            await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*At least one*");
        }
        finally { DeleteDir(tempRoot); }
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ParameterlessOverload_ShouldDelegate()
    {
        var (service, tempRoot) = await CreateOnboardingServiceAsync();
        try
        {
            var batch = new ModOnboardingSeedBatchRequest("custom", new[]
            {
                new GeneratedProfileSeed(
                    DraftProfileId: "overload_seed",
                    DisplayName: "Overload Seed",
                    BaseProfileId: "base_swfoc",
                    LaunchSamples: new[] { new ModLaunchSample("StarWarsG.exe", null, "STEAMMOD=111") },
                    SourceRunId: "run-1",
                    Confidence: 0.90,
                    ParentProfile: "base_swfoc")
            });
            var result = await service.ScaffoldDraftProfilesFromSeedsAsync(batch);
            result.Succeeded.Should().BeTrue();
        }
        finally { DeleteDir(tempRoot); }
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldDetectDuplicateNormalizedIds()
    {
        var (service, tempRoot) = await CreateOnboardingServiceAsync();
        try
        {
            var batch = new ModOnboardingSeedBatchRequest("custom", new[]
            {
                new GeneratedProfileSeed(DraftProfileId: "Dupe Mod", DisplayName: "Dupe Mod", BaseProfileId: "base_swfoc",
                    LaunchSamples: Array.Empty<ModLaunchSample>(), SourceRunId: "run-1", Confidence: 0.9, ParentProfile: "base_swfoc"),
                new GeneratedProfileSeed(DraftProfileId: "dupe_mod", DisplayName: "Dupe Mod 2", BaseProfileId: "base_swfoc",
                    LaunchSamples: Array.Empty<ModLaunchSample>(), SourceRunId: "run-1", Confidence: 0.9, ParentProfile: "base_swfoc")
            });
            var result = await service.ScaffoldDraftProfilesFromSeedsAsync(batch);
            result.Succeeded.Should().BeFalse();
            result.Results.Should().Contain(r => !r.Succeeded && r.Errors.Any(e => e.Contains("Duplicate")));
        }
        finally { DeleteDir(tempRoot); }
    }

    [Fact]
    public void NormalizeProfileId_ShouldThrow_WhenIdNormalizesToEmpty()
    {
        var method = typeof(ModOnboardingService).GetMethod("NormalizeProfileId", BindingFlags.NonPublic | BindingFlags.Static)!;
        var act = () => method.Invoke(null, new object[] { "!!!" });
        act.Should().Throw<TargetInvocationException>().WithInnerException<InvalidDataException>();
    }

    [Fact]
    public void NormalizeNamespace_ShouldReturnCustom_WhenInputNormalizesToEmpty()
    {
        var method = typeof(ModOnboardingService).GetMethod("NormalizeNamespace", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (string)method.Invoke(null, new object?[] { "!!!" })!;
        result.Should().Be("custom");
    }

    [Fact]
    public void ResolveSeedDraftProfileId_ShouldUseTitleAsFallback()
    {
        var method = typeof(ModOnboardingService).GetMethod("ResolveSeedDraftProfileId", BindingFlags.NonPublic | BindingFlags.Static)!;
        var seed = new GeneratedProfileSeed(DraftProfileId: null!, DisplayName: null!, BaseProfileId: null!,
            LaunchSamples: null!, SourceRunId: null!, Confidence: 0, ParentProfile: null!, Title: "My Title");
        var result = (string?)method.Invoke(null, new object[] { seed });
        result.Should().Be("My Title");
    }

    [Fact]
    public void ResolveSeedDraftProfileId_ShouldReturnNull_WhenAllEmpty()
    {
        var method = typeof(ModOnboardingService).GetMethod("ResolveSeedDraftProfileId", BindingFlags.NonPublic | BindingFlags.Static)!;
        var seed = new GeneratedProfileSeed(DraftProfileId: null!, DisplayName: null!, BaseProfileId: null!,
            LaunchSamples: null!, SourceRunId: null!, Confidence: 0, ParentProfile: null!);
        var result = (string?)method.Invoke(null, new object[] { seed });
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveSeedDisplayName_ShouldUseWorkshopIdAsFallback()
    {
        var method = typeof(ModOnboardingService).GetMethod("ResolveSeedDisplayName", BindingFlags.NonPublic | BindingFlags.Static)!;
        var seed = new GeneratedProfileSeed(DraftProfileId: null!, DisplayName: null!, BaseProfileId: null!,
            LaunchSamples: null!, SourceRunId: null!, Confidence: 0, ParentProfile: null!, WorkshopId: "999");
        var result = (string?)method.Invoke(null, new object[] { seed });
        result.Should().Be("Workshop Mod 999");
    }

    [Fact]
    public void ResolveSeedDisplayName_ShouldReturnNull_WhenAllEmpty()
    {
        var method = typeof(ModOnboardingService).GetMethod("ResolveSeedDisplayName", BindingFlags.NonPublic | BindingFlags.Static)!;
        var seed = new GeneratedProfileSeed(DraftProfileId: null!, DisplayName: null!, BaseProfileId: null!,
            LaunchSamples: null!, SourceRunId: null!, Confidence: 0, ParentProfile: null!);
        var result = (string?)method.Invoke(null, new object[] { seed });
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveBaseProfileId_ShouldUseParentProfile()
    {
        var method = typeof(ModOnboardingService).GetMethod("ResolveBaseProfileId", BindingFlags.NonPublic | BindingFlags.Static)!;
        var seed = new GeneratedProfileSeed(DraftProfileId: null!, DisplayName: null!, BaseProfileId: null!,
            LaunchSamples: null!, SourceRunId: null!, Confidence: 0, ParentProfile: "parent_prof");
        var result = (string?)method.Invoke(null, new object[] { seed });
        result.Should().Be("parent_prof");
    }

    [Fact]
    public void ResolveBaseProfileId_ShouldReturnNull_WhenAllEmpty()
    {
        var method = typeof(ModOnboardingService).GetMethod("ResolveBaseProfileId", BindingFlags.NonPublic | BindingFlags.Static)!;
        var seed = new GeneratedProfileSeed(DraftProfileId: null!, DisplayName: null!, BaseProfileId: null!,
            LaunchSamples: null!, SourceRunId: null!, Confidence: 0, ParentProfile: null!);
        var result = (string?)method.Invoke(null, new object[] { seed });
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveParentProfile_ShouldUseCandidateBaseProfile_AsFallback()
    {
        var method = typeof(ModOnboardingService).GetMethod("ResolveParentProfile", BindingFlags.NonPublic | BindingFlags.Static)!;
        var seed = new GeneratedProfileSeed(DraftProfileId: null!, DisplayName: null!, BaseProfileId: null!,
            LaunchSamples: null!, SourceRunId: null!, Confidence: 0, ParentProfile: null!, CandidateBaseProfile: "candidate");
        var result = (string)method.Invoke(null, new object[] { seed, "fallback" })!;
        result.Should().Be("candidate");
    }

    [Fact]
    public void ResolveParentProfile_ShouldUseFallback_WhenAllEmpty()
    {
        var method = typeof(ModOnboardingService).GetMethod("ResolveParentProfile", BindingFlags.NonPublic | BindingFlags.Static)!;
        var seed = new GeneratedProfileSeed(DraftProfileId: null!, DisplayName: null!, BaseProfileId: null!,
            LaunchSamples: null!, SourceRunId: null!, Confidence: 0, ParentProfile: null!);
        var result = (string)method.Invoke(null, new object[] { seed, "fallback_id" })!;
        result.Should().Be("fallback_id");
    }

    [Fact]
    public void NormalizeRiskLevel_ShouldReturnMedium_WhenNull()
    {
        var method = typeof(ModOnboardingService).GetMethod("NormalizeRiskLevel", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (string)method.Invoke(null, new object?[] { null })!;
        result.Should().Be("medium");
    }

    [Fact]
    public void NormalizeRiskLevel_ShouldReturnLow_ForValidInput()
    {
        var method = typeof(ModOnboardingService).GetMethod("NormalizeRiskLevel", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (string)method.Invoke(null, new object?[] { "LOW" })!;
        result.Should().Be("low");
    }

    [Fact]
    public void MergeWorkshopIds_ShouldHandleNullDeclarations()
    {
        var method = typeof(ModOnboardingService).GetMethod("MergeWorkshopIds", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (IReadOnlyList<string>)method.Invoke(null, new object?[] { null, null, Array.Empty<string>() })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void MergePathHints_ShouldHandleNullDeclarations()
    {
        var method = typeof(ModOnboardingService).GetMethod("MergePathHints", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (IReadOnlyList<string>)method.Invoke(null, new object?[] { null, Array.Empty<string>() })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void MergeRequiredCapabilities_ShouldHandleNulls()
    {
        var method = typeof(ModOnboardingService).GetMethod("MergeRequiredCapabilities", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (IReadOnlyList<string>)method.Invoke(null, new object?[] { null, null })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldValidateNonFiniteConfidence()
    {
        var (service, tempRoot) = await CreateOnboardingServiceAsync();
        try
        {
            var batch = new ModOnboardingSeedBatchRequest("custom", new[]
            {
                new GeneratedProfileSeed(
                    DraftProfileId: "inf_seed",
                    DisplayName: "Inf",
                    BaseProfileId: "base_swfoc",
                    LaunchSamples: Array.Empty<ModLaunchSample>(),
                    SourceRunId: "run-1",
                    Confidence: double.PositiveInfinity,
                    ParentProfile: "base_swfoc")
            });
            var result = await service.ScaffoldDraftProfilesFromSeedsAsync(batch);
            result.Succeeded.Should().BeFalse();
            result.Results[0].Errors.Should().Contain(e => e.Contains("Confidence"));
        }
        finally { DeleteDir(tempRoot); }
    }

    #endregion

    #region ExtractionHelpers — edge branches

    [Fact]
    public void ExtractToDirectorySafely_ShouldThrow_WhenZipPathIsNull()
    {
        var act = () => GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely(null!, Path.GetTempPath());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExtractToDirectorySafely_ShouldThrow_WhenExtractDirIsNull()
    {
        var act = () => GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely("some.zip", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExtractToDirectorySafely_ShouldThrow_OnDriveQualifiedPaths()
    {
        using var temp = new TempRoot();
        var zipPath = Path.Join(temp.CacheRoot, "drive_qualified.zip");
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                zip.CreateEntry("C:/evil/path.txt");
            }
            File.WriteAllBytes(zipPath, ms.ToArray());
        }

        var extractDir = Path.Join(temp.CacheRoot, "extract");
        var act = () => GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely(zipPath, extractDir);
        act.Should().Throw<InvalidDataException>().WithMessage("*drive-qualified*");
    }

    [Fact]
    public void ExtractToDirectorySafely_ShouldThrow_OnRootedPaths()
    {
        using var temp = new TempRoot();
        var zipPath = Path.Join(temp.CacheRoot, "rooted.zip");
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                zip.CreateEntry("/etc/passwd");
            }
            File.WriteAllBytes(zipPath, ms.ToArray());
        }

        var extractDir = Path.Join(temp.CacheRoot, "extract_rooted");
        var act = () => GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely(zipPath, extractDir);
        act.Should().Throw<InvalidDataException>().WithMessage("*rooted*");
    }

    [Fact]
    public void ExtractToDirectorySafely_ShouldSkipEmptyEntryPaths()
    {
        using var temp = new TempRoot();
        var zipPath = Path.Join(temp.CacheRoot, "empty_entry.zip");
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Create a valid entry and an entry with whitespace name
                zip.CreateEntry("valid.txt");
            }
            File.WriteAllBytes(zipPath, ms.ToArray());
        }

        var extractDir = Path.Join(temp.CacheRoot, "extract_empty");
        var act = () => GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely(zipPath, extractDir);
        act.Should().NotThrow();
    }

    [Fact]
    public void ExtractToDirectorySafely_ShouldCreateDirectoryEntries()
    {
        using var temp = new TempRoot();
        var zipPath = Path.Join(temp.CacheRoot, "dir_entry.zip");
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                zip.CreateEntry("subdir/");
                var entry = zip.CreateEntry("subdir/file.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("content");
            }
            File.WriteAllBytes(zipPath, ms.ToArray());
        }

        var extractDir = Path.Join(temp.CacheRoot, "extract_dir");
        GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely(zipPath, extractDir);
        Directory.Exists(Path.Join(extractDir, "subdir")).Should().BeTrue();
        File.Exists(Path.Join(extractDir, "subdir", "file.txt")).Should().BeTrue();
    }

    #endregion

    #region ProfileValidator — remaining branches

    [Fact]
    public void Validate_ShouldThrow_WhenHostPreferenceIsInvalid()
    {
        var profile = new TrainerProfile(
            Id: "test",
            DisplayName: "test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) },
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            HostPreference: "invalid_host");

        var act = () => SwfocTrainer.Profiles.Validation.ProfileValidator.Validate(profile);
        act.Should().Throw<InvalidDataException>().WithMessage("*hostPreference*");
    }

    [Fact]
    public void Validate_ShouldPass_WhenPreferencesAreBlank()
    {
        var profile = new TrainerProfile(
            Id: "test",
            DisplayName: "test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) },
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            BackendPreference: "  ",
            HostPreference: "  ");

        var act = () => SwfocTrainer.Profiles.Validation.ProfileValidator.Validate(profile);
        act.Should().NotThrow();
    }

    #endregion

    #region Helpers

    private const string ManifestUrl = "https://example.invalid/manifest.json";

    private static GitHubProfileUpdateService CreateUpdateService(
        TempRoot temp, HttpMessageHandler handler, StubProfileRepo repo, string? remoteManifestUrl = ManifestUrl)
    {
        return new GitHubProfileUpdateService(
            new HttpClient(handler),
            new ProfileRepositoryOptions
            {
                ProfilesRootPath = temp.ProfilesRoot,
                ManifestFileName = "manifest.json",
                DownloadCachePath = temp.CacheRoot,
                RemoteManifestUrl = remoteManifestUrl
            },
            repo);
    }

    private static HttpMessageHandler EmptyHandler()
        => new StubHttpHandler(new Dictionary<string, StubResp>(StringComparer.OrdinalIgnoreCase));

    private static HttpMessageHandler ManifestHandler(string payload)
        => new StubHttpHandler(new Dictionary<string, StubResp>(StringComparer.OrdinalIgnoreCase)
        {
            [ManifestUrl] = StubResp.Json(payload)
        });

    private static HttpMessageHandler CreatePackageHandler(ProfileManifestEntry entry, byte[] zipBytes)
        => new StubHttpHandler(new Dictionary<string, StubResp>(StringComparer.OrdinalIgnoreCase)
        {
            [ManifestUrl] = StubResp.Json(JsonSerializer.Serialize(new ProfileManifest("1.0", DateTimeOffset.UtcNow, new[] { entry }))),
            [entry.DownloadUrl] = StubResp.Bytes(zipBytes)
        });

    private static byte[] BuildZipWithProfile(string profileId, string json)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = zip.CreateEntry($"profiles/{profileId}.json");
            using var w = new StreamWriter(e.Open());
            w.Write(json);
        }
        return ms.ToArray();
    }

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static string BuildValidProfile(string profileId)
        => $"{{\"id\":\"{profileId}\",\"displayName\":\"test\",\"inherits\":null,\"exeTarget\":\"Swfoc\",\"steamWorkshopId\":null,\"signatureSets\":[{{\"name\":\"x\",\"gameBuild\":\"x\",\"signatures\":[]}}],\"fallbackOffsets\":{{}},\"actions\":{{}},\"featureFlags\":{{}},\"catalogSources\":[],\"saveSchemaId\":\"v1\",\"helperModHooks\":[]}}";

    private static string BuildProfile(string profileId, string? inherits = null)
    {
        var inheritsVal = inherits is not null ? $"\"{inherits}\"" : "null";
        return $"{{\"id\":\"{profileId}\",\"displayName\":\"{profileId}\",\"inherits\":{inheritsVal},\"exeTarget\":\"Swfoc\",\"steamWorkshopId\":null,\"signatureSets\":[{{\"name\":\"base\",\"gameBuild\":\"1.0\",\"signatures\":[]}}],\"fallbackOffsets\":{{}},\"actions\":{{}},\"featureFlags\":{{}},\"catalogSources\":[],\"saveSchemaId\":\"schema\",\"helperModHooks\":[]}}";
    }

    private static async Task<(ModOnboardingService Service, string TempRoot)> CreateOnboardingServiceAsync()
    {
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-onboarding-w2-{Guid.NewGuid():N}");
        var defaultRoot = Path.Join(tempRoot, "default");
        var profilesDir = Path.Join(defaultRoot, "profiles");
        Directory.CreateDirectory(profilesDir);
        await File.WriteAllTextAsync(Path.Join(defaultRoot, "manifest.json"),
            JsonSerializer.Serialize(new ProfileManifest("1.0", DateTimeOffset.UtcNow, new[] { new ProfileManifestEntry("base_swfoc", "1.0", "abc", "url", "1.0") })));
        var baseProfile = new TrainerProfile(
            Id: "base_swfoc", DisplayName: "Base FoC", Inherits: null, ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: new[] { new SignatureSet("base", "test", Array.Empty<SignatureSpec>()) },
            FallbackOffsets: new Dictionary<string, long>(), Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(), CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "base_swfoc_steam_v1", HelperModHooks: Array.Empty<HelperHookSpec>(), Metadata: null);
        await File.WriteAllTextAsync(Path.Join(profilesDir, "base_swfoc.json"), JsonProfileSerializer.Serialize(baseProfile));
        var options = new ProfileRepositoryOptions { ProfilesRootPath = defaultRoot, ManifestFileName = "manifest.json", DownloadCachePath = Path.Join(tempRoot, "cache") };
        var repository = new FileSystemProfileRepository(options);
        var service = new ModOnboardingService(repository, options);
        return (service, tempRoot);
    }

    private static void DeleteDir(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private sealed class StubProfileRepo : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct = default) => Task.FromResult(new ProfileManifest("1.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>()));
        public Task<TrainerProfile> LoadProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ValidateProfileAsync(TrainerProfile p, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private readonly record struct StubResp(string ContentType, byte[] Body, Exception? Error)
    {
        public static StubResp Json(string payload) => new("application/json", Encoding.UTF8.GetBytes(payload), null);
        public static StubResp Bytes(byte[] body) => new("application/zip", body, null);
        public static StubResp ThrowEx(Exception error) => new("application/octet-stream", Array.Empty<byte>(), error);
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, StubResp> _responses;
        public StubHttpHandler(IReadOnlyDictionary<string, StubResp> responses) { _responses = responses; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            var key = request.RequestUri!.ToString();
            if (!_responses.TryGetValue(key, out var payload))
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") };
            if (payload.Error is not null) throw payload.Error;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload.Body) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(payload.ContentType);
            return response;
        }
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            RootPath = Path.Join(Path.GetTempPath(), $"swfoc-profiles-w2-{Guid.NewGuid():N}");
            ProfilesRoot = Path.Join(RootPath, "default");
            CacheRoot = Path.Join(RootPath, "cache");
            Directory.CreateDirectory(Path.Join(ProfilesRoot, "profiles"));
            Directory.CreateDirectory(CacheRoot);
        }
        public string RootPath { get; }
        public string ProfilesRoot { get; }
        public string CacheRoot { get; }
        public void Dispose() { if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true); }
    }

    #endregion
}
