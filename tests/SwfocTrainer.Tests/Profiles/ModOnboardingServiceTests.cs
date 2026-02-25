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
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-onboarding-{Guid.NewGuid():N}");
        try
        {
            var (service, _) = await CreateServiceAsync(tempRoot);

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
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldGenerateDraftsAndAutoDiscoveryMetadata()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-onboarding-batch-{Guid.NewGuid():N}");
        try
        {
            var (service, options) = await CreateServiceAsync(tempRoot);
            var request = new ModOnboardingSeedBatchRequest(
                Seeds:
                [
                    BuildSeed(
                        workshopId: "1397421866",
                        title: "Awakening of the Rebellion",
                        candidateBaseProfile: "base_swfoc",
                        sourceRunId: "run-seed-001",
                        confidence: 0.93,
                        riskLevel: "medium",
                        modPathHints: ["aotr", "awakening_of_the_rebellion"]),
                    BuildSeed(
                        workshopId: "3664004146",
                        title: "Enhancement Pack",
                        candidateBaseProfile: "base_swfoc",
                        sourceRunId: "run-seed-001",
                        confidence: 0.62,
                        riskLevel: "low",
                        modPathHints: ["enhancement_pack"])
                ],
                NamespaceRoot: "custom",
                FallbackBaseProfileId: "base_swfoc");

            var result = await service.ScaffoldDraftProfilesFromSeedsAsync(request, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            result.Total.Should().Be(2);
            result.Generated.Should().Be(2);
            result.Failed.Should().Be(0);
            result.Items.Should().OnlyContain(item => item.Succeeded);

            var firstProfilePath = Path.Combine(
                Directory.GetParent(options.ProfilesRootPath)!.FullName,
                "custom",
                "profiles",
                "custom_awakening_of_the_rebellion_1397421866_swfoc.json");
            File.Exists(firstProfilePath).Should().BeTrue();
            var firstProfileJson = await File.ReadAllTextAsync(firstProfilePath);
            var firstProfile = JsonProfileSerializer.Deserialize<TrainerProfile>(firstProfileJson);
            firstProfile.Should().NotBeNull();
            firstProfile!.RequiredCapabilities.Should().Contain("set_credits");
            firstProfile.RequiredCapabilities.Should().Contain("toggle_instant_build_patch");
            firstProfile.Metadata.Should().NotBeNull();
            firstProfile.Metadata!["origin"].Should().Be("auto_discovery");
            firstProfile.Metadata["sourceRunId"].Should().Be("run-seed-001");
            firstProfile.Metadata["parentProfile"].Should().Be("base_swfoc");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ShouldReturnPartialFailure_WhenOneSeedIsInvalid()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-onboarding-batch-invalid-{Guid.NewGuid():N}");
        try
        {
            var (service, _) = await CreateServiceAsync(tempRoot);
            var request = new ModOnboardingSeedBatchRequest(
                Seeds:
                [
                    BuildSeed(
                        workshopId: "1397421866",
                        title: "Awakening of the Rebellion",
                        candidateBaseProfile: "base_swfoc",
                        sourceRunId: "run-seed-002",
                        confidence: 0.92,
                        riskLevel: "medium",
                        modPathHints: ["aotr"]),
                    BuildSeed(
                        workshopId: "3009221569",
                        title: "   ",
                        candidateBaseProfile: "base_swfoc",
                        sourceRunId: "run-seed-002",
                        confidence: 0.50,
                        riskLevel: "high",
                        modPathHints: ["broken_seed"])
                ],
                NamespaceRoot: "custom",
                FallbackBaseProfileId: "base_swfoc");

            var result = await service.ScaffoldDraftProfilesFromSeedsAsync(request, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Total.Should().Be(2);
            result.Generated.Should().Be(1);
            result.Failed.Should().Be(1);
            result.Items.Should().ContainSingle(item => !item.Succeeded);
            result.Items.Single(item => !item.Succeeded).Error.Should().NotBeNullOrWhiteSpace();
            result.Items.Single(item => item.Succeeded).ProfileId.Should().Contain("custom_awakening_of_the_rebellion");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static async Task<(ModOnboardingService Service, ProfileRepositoryOptions Options)> CreateServiceAsync(string tempRoot)
    {
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
            SignatureSets:
            [
                new SignatureSet("base", "test", Array.Empty<SignatureSpec>())
            ],
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
        return (service, options);
    }

    private static GeneratedProfileSeed BuildSeed(
        string workshopId,
        string title,
        string candidateBaseProfile,
        string sourceRunId,
        double confidence,
        string riskLevel,
        IReadOnlyList<string> modPathHints)
    {
        return new GeneratedProfileSeed(
            WorkshopId: workshopId,
            Title: title,
            CandidateBaseProfile: candidateBaseProfile,
            LaunchHints: new GeneratedLaunchHints(
                SteamModIds: [workshopId],
                ModPathHints: modPathHints),
            ParentDependencies: Array.Empty<string>(),
            RequiredCapabilities:
            [
                "set_credits",
                "freeze_timer",
                "toggle_fog_reveal",
                "toggle_ai",
                "set_unit_cap",
                "toggle_instant_build_patch"
            ],
            SourceRunId: sourceRunId,
            Confidence: confidence,
            RiskLevel: riskLevel);
    }
}
