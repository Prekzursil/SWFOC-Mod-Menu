using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Catalog.Config;
using SwfocTrainer.Catalog.Services;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Catalog;

public sealed class CatalogServiceTests
{
    [Fact]
    public async Task LoadCatalogAsync_ShouldEmitBuildingAndEntityCatalogs_FromPrebuiltCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileId = "test_profile";
            var profileCatalogDir = Path.Combine(root, profileId);
            Directory.CreateDirectory(profileCatalogDir);

            await WriteCatalogFixtureAsync(profileCatalogDir);

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(CreateProfile(profileId)),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync(profileId, CancellationToken.None);
            AssertCatalogContainsDerivedEntries(catalog);
            catalog.Should().ContainKey("action_constraints");
            catalog["action_constraints"].Should().Contain("spawn_tactical_entity");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadCatalogAsync_ShouldParseXmlSources_WhenPrebuiltMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-xml-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileId = "xml_profile";
            var dataRoot = Path.Combine(root, "data");
            Directory.CreateDirectory(dataRoot);
            var xmlPath = Path.Combine(dataRoot, "Objects.xml");
            await File.WriteAllTextAsync(
                xmlPath,
                """
                <Root>
                  <LandUnit Name="EMPIRE_STORMTROOPER_SQUAD" />
                  <Structure ID="EMPIRE_BARRACKS" />
                  <Hero Object_Name="HERO_VADER" />
                  <Planet Type="PLANET_CORUSCANT" />
                  <Faction Name="EMPIRE" />
                </Root>
                """);

            var profile = CreateProfile(
                profileId,
                catalogSources:
                [
                    new CatalogSource("file", xmlPath, Required: false),
                    new CatalogSource("xml", xmlPath, Required: true)
                ]);

            var service = new CatalogService(
                new CatalogOptions
                {
                    CatalogRootPath = root,
                    MaxParsedXmlFiles = 10
                },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync(profileId, CancellationToken.None);

            catalog["unit_catalog"].Should().Contain("EMPIRE_STORMTROOPER_SQUAD");
            catalog["building_catalog"].Should().Contain("EMPIRE_BARRACKS");
            catalog["hero_catalog"].Should().Contain("HERO_VADER");
            catalog["planet_catalog"].Should().Contain("PLANET_CORUSCANT");
            catalog["faction_catalog"].Should().Contain("EMPIRE");
            catalog["entity_catalog"].Should().Contain("Building|EMPIRE_BARRACKS");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task WriteCatalogFixtureAsync(string profileCatalogDir)
    {
        var catalogPath = Path.Combine(profileCatalogDir, "catalog.json");
        await File.WriteAllTextAsync(
            catalogPath,
            """
            {
              "unit_catalog": [
                "EMPIRE_STORMTROOPER_SQUAD",
                "EMPIRE_BARRACKS",
                "REBEL_LIGHT_FACTORY"
              ],
              "planet_catalog": [
                "PLANET_CORUSCANT"
              ],
              "hero_catalog": [
                "HERO_VADER"
              ],
              "faction_catalog": [
                "EMPIRE",
                "REBEL"
              ]
            }
            """);
    }

    private static void AssertCatalogContainsDerivedEntries(IReadOnlyDictionary<string, IReadOnlyList<string>> catalog)
    {
        catalog.Should().ContainKey("building_catalog");
        catalog["building_catalog"].Should().Contain("EMPIRE_BARRACKS");
        catalog["building_catalog"].Should().Contain("REBEL_LIGHT_FACTORY");

        catalog.Should().ContainKey("entity_catalog");
        catalog["entity_catalog"].Should().Contain("Unit|EMPIRE_STORMTROOPER_SQUAD");
        catalog["entity_catalog"].Should().Contain("Building|EMPIRE_BARRACKS");
        catalog["entity_catalog"].Should().Contain("Planet|PLANET_CORUSCANT");
        catalog["entity_catalog"].Should().Contain("Hero|HERO_VADER");
    }

    private static TrainerProfile CreateProfile(
        string profileId,
        IReadOnlyList<CatalogSource>? catalogSources = null)
    {
        var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["spawn_tactical_entity"] = new ActionSpec(
                Id: "spawn_tactical_entity",
                Category: ActionCategory.Global,
                Mode: RuntimeMode.AnyTactical,
                ExecutionKind: ExecutionKind.Helper,
                PayloadSchema: new System.Text.Json.Nodes.JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0)
        };

        return new TrainerProfile(
            Id: profileId,
            DisplayName: "test profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: actions,
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: catalogSources ?? Array.Empty<CatalogSource>(),
            SaveSchemaId: "test_schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>());
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
            return Task.FromResult(new ProfileManifest(
                Version: "1.0",
                PublishedAt: DateTimeOffset.UtcNow,
                Profiles: new[]
                {
                    new ProfileManifestEntry(_profile.Id, "1.0", "hash", "url", "1.0")
                }));
        }

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_profile);
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
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult((IReadOnlyList<string>)new[] { _profile.Id });
        }
    }
}

