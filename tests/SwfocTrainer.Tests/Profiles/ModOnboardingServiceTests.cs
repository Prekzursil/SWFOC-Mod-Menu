using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

public sealed class ModOnboardingServiceTests
{
    [Fact]
    public async Task ScaffoldDraftProfileAsync_ShouldGenerateDraftProfileWithHints()
    {
        var (service, tempRoot) = await CreateServiceAsync();

        try
        {
            var request = new ModOnboardingRequest(
                DraftProfileId: "My Total Conversion",
                DisplayName: "My Total Conversion",
                BaseProfileId: "base_swfoc",
                LaunchSamples: new[]
                {
                    new ModLaunchSample(
                        ProcessName: "StarWarsG.exe",
                        ProcessPath: @"C:\Games\Star Wars Empire at War\corruption\StarWarsG.exe",
                        CommandLine: "StarWarsG.exe LANGUAGE=ENGLISH STEAMMOD=555000111 MODPATH=Mods\\MyTotalConversion")
                },
                ProfileAliases: new[] { "mtc", "my-total-conversion" },
                NamespaceRoot: "custom");

            var result = await service.ScaffoldDraftProfileAsync(request);

            result.Succeeded.Should().BeTrue();
            result.ProfileId.Should().Be("custom_my_total_conversion");
            result.InferredWorkshopIds.Should().Contain("555000111");
            result.InferredPathHints.Should().Contain("mytotalconversion");
            File.Exists(result.OutputPath).Should().BeTrue();

            var writtenJson = await File.ReadAllTextAsync(result.OutputPath);
            var draft = JsonProfileSerializer.Deserialize<TrainerProfile>(writtenJson);
            draft.Should().NotBeNull();
            draft!.Inherits.Should().Be("base_swfoc");
            draft.Metadata.Should().NotBeNull();
            draft.Metadata!["requiredWorkshopIds"].Should().Contain("555000111");
            draft.Metadata!["localPathHints"].Should().Contain("mytotalconversion");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldWriteTwoDrafts()
    {
        var (service, tempRoot) = await CreateServiceAsync();

        try
        {
            var result = await service.ScaffoldDraftProfilesFromSeedsAsync(CreateTwoSeedBatch());
            AssertTwoSeedBatchResult(result);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldContinueWhenOneSeedInvalid()
    {
        var (service, tempRoot) = await CreateServiceAsync();

        try
        {
            var result = await service.ScaffoldDraftProfilesFromSeedsAsync(CreatePartialFailureBatch());
            AssertPartialFailureResult(result);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldIncludeRequiredGeneratedMetadata()
    {
        var (service, tempRoot) = await CreateServiceAsync();

        try
        {
            var result = await service.ScaffoldDraftProfilesFromSeedsAsync(CreateMetadataSeedBatch());

            result.Succeeded.Should().BeTrue();
            result.SucceededCount.Should().Be(1);
            var item = result.Results.Single();
            item.Succeeded.Should().BeTrue();

            var writtenJson = await File.ReadAllTextAsync(item.OutputPath!);
            var draft = JsonProfileSerializer.Deserialize<TrainerProfile>(writtenJson);
            AssertMetadataSeedDraft(draft);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private static ModOnboardingSeedBatchRequest CreateTwoSeedBatch()
    {
        return new ModOnboardingSeedBatchRequest(
            TargetNamespaceRoot: "generated.mods",
            Seeds: new[]
            {
                CreateSeed("First Mod", "First Mod", "run-20260225-01", 0.92d, "StarWarsG.exe STEAMMOD=111000111 MODPATH=Mods\\FirstMod"),
                CreateSeed("Second Mod", "Second Mod", "run-20260225-01", 0.88d, "StarWarsG.exe STEAMMOD=222000222 MODPATH=Mods\\SecondMod")
            });
    }

    private static ModOnboardingSeedBatchRequest CreatePartialFailureBatch()
    {
        return new ModOnboardingSeedBatchRequest(
            TargetNamespaceRoot: "generated.mods",
            Seeds: new[]
            {
                CreateSeed("   ", "Broken Seed", "run-20260225-02", 0.50d, "StarWarsG.exe"),
                CreateSeed("Valid Seed", "Valid Seed", "run-20260225-02", 0.75d, "StarWarsG.exe STEAMMOD=333000333 MODPATH=Mods\\ValidSeed")
            });
    }

    private static ModOnboardingSeedBatchRequest CreateMetadataSeedBatch()
    {
        return new ModOnboardingSeedBatchRequest(
            TargetNamespaceRoot: "generated.mods",
            Seeds: new[]
            {
                new GeneratedProfileSeed(
                    DraftProfileId: "Metadata Seed",
                    DisplayName: "Metadata Seed",
                    BaseProfileId: "base_swfoc",
                    LaunchSamples: new[]
                    {
                        new ModLaunchSample(
                            ProcessName: "StarWarsG.exe",
                            ProcessPath: @"C:\Games\EmpireAtWar\corruption\StarWarsG.exe",
                            CommandLine: "StarWarsG.exe STEAMMOD=777888999 MODPATH=Mods\\MetaMod")
                    },
                    SourceRunId: "run-20260225-meta",
                    Confidence: 0.87d,
                    ParentProfile: "base_swfoc",
                    RequiredWorkshopIds: new[] { "777888999", "123123123" },
                    ProfileAliases: new[] { "meta-seed", "metadata_seed" })
            });
    }

    private static GeneratedProfileSeed CreateSeed(
        string draftProfileId,
        string displayName,
        string sourceRunId,
        double confidence,
        string commandLine)
    {
        return new GeneratedProfileSeed(
            DraftProfileId: draftProfileId,
            DisplayName: displayName,
            BaseProfileId: "base_swfoc",
            LaunchSamples: new[]
            {
                new ModLaunchSample(
                    ProcessName: "StarWarsG.exe",
                    ProcessPath: @"C:\Games\EmpireAtWar\corruption\StarWarsG.exe",
                    CommandLine: commandLine)
            },
            SourceRunId: sourceRunId,
            Confidence: confidence,
            ParentProfile: "base_swfoc");
    }

    private static void AssertTwoSeedBatchResult(ModOnboardingBatchResult result)
    {
        result.Succeeded.Should().BeTrue();
        result.Attempted.Should().Be(2);
        result.SucceededCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
        result.Results.Should().HaveCount(2);
        result.Results.Should().OnlyContain(x => x.Succeeded);
        result.Results.Select(x => x.ProfileId).Should().Contain(new[] { "custom_first_mod", "custom_second_mod" });
        result.Results.Select(x => x.OutputPath).Should().OnlyContain(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static void AssertPartialFailureResult(ModOnboardingBatchResult result)
    {
        result.Succeeded.Should().BeFalse();
        result.Attempted.Should().Be(2);
        result.SucceededCount.Should().Be(1);
        result.FailedCount.Should().Be(1);

        var failed = result.Results.Single(x => !x.Succeeded);
        failed.Errors.Should().Contain(error => error.Contains("DraftProfileId", StringComparison.OrdinalIgnoreCase));
        var succeeded = result.Results.Single(x => x.Succeeded);
        succeeded.ProfileId.Should().Be("custom_valid_seed");
        succeeded.OutputPath.Should().NotBeNullOrWhiteSpace();
        File.Exists(succeeded.OutputPath!).Should().BeTrue();
    }

    private static void AssertMetadataSeedDraft(TrainerProfile? draft)
    {
        draft.Should().NotBeNull();
        draft!.Metadata.Should().NotBeNull();
        draft.Metadata!["origin"].Should().Be("auto_discovery");
        draft.Metadata!["sourceRunId"].Should().Be("run-20260225-meta");
        draft.Metadata!["confidence"].Should().Be("0.87");
        draft.Metadata!["parentProfile"].Should().Be("base_swfoc");
        draft.Metadata!["requiredWorkshopIds"].Should().Contain("777888999");
        draft.Metadata!["profileAliases"].Should().Contain("custom_metadata_seed");
        draft.Metadata!["localPathHints"].Should().Contain("metamod");
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static async Task<(ModOnboardingService Service, string TempRoot)> CreateServiceAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-onboarding-{Guid.NewGuid():N}");
        var defaultRoot = Path.Combine(tempRoot, "default");
        var profilesDir = Path.Combine(defaultRoot, "profiles");
        Directory.CreateDirectory(profilesDir);

        await File.WriteAllTextAsync(Path.Combine(defaultRoot, "manifest.json"), """
        {
          "version": "1.0.0",
          "publishedAt": "2026-01-01T00:00:00Z",
          "profiles": [
            {
              "id": "base_swfoc",
              "version": "1.0.0",
              "sha256": "abc",
              "downloadUrl": "https://example.invalid/base_swfoc.zip",
              "minAppVersion": "1.0.0",
              "description": "base"
            }
          ]
        }
        """);

        var baseProfile = new TrainerProfile(
            Id: "base_swfoc",
            DisplayName: "Base FoC",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: new[]
            {
                new SignatureSet("base", "test", Array.Empty<SignatureSpec>())
            },
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "base_swfoc_steam_v1",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: null);

        await File.WriteAllTextAsync(
            Path.Combine(profilesDir, "base_swfoc.json"),
            JsonProfileSerializer.Serialize(baseProfile));

        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = defaultRoot,
            ManifestFileName = "manifest.json",
            DownloadCachePath = Path.Combine(tempRoot, "cache")
        };

        var repository = new FileSystemProfileRepository(options);
        var service = new ModOnboardingService(repository, options);
        return (service, tempRoot);
    }
}
