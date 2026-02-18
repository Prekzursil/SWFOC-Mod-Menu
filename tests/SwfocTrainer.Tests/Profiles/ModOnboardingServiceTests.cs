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
        var defaultRoot = Path.Combine(tempRoot, "default");
        var profilesDir = Path.Combine(defaultRoot, "profiles");
        Directory.CreateDirectory(profilesDir);

        try
        {
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
}
