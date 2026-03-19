using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;
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
    public async Task LoadCatalogAsync_ShouldParseXmlSources_RespectMaxParsedFiles_AndMergeDerivedCatalogs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileId = "xml_profile";
            var firstXmlPath = Path.Combine(root, "catalog-first.xml");
            var secondXmlPath = Path.Combine(root, "catalog-second.xml");

            await File.WriteAllTextAsync(
                firstXmlPath,
                """
                <Root>
                  <Object Name="EMPIRE_BARRACKS" />
                  <Object ID="PLANET_CORUSCANT" />
                  <Object Type="PALPATINE_FLAGSHIP" />
                  <Object Object_Name="CIS_DROID_FACTORY" />
                  <Object Name="UNDERWORLD_MINE" />
                </Root>
                """);
            await File.WriteAllTextAsync(
                secondXmlPath,
                """
                <Root>
                  <Object Name="REBEL_SHIPYARD" />
                  <Object Name="HERO_LUKE_SKYWALKER" />
                </Root>
                """);

            var profile = CreateProfile(
                profileId,
                actions: CreateActions("z_spawn", "a_heal"),
                catalogSources:
                [
                    new CatalogSource("json", firstXmlPath),
                    new CatalogSource("xml", Path.Combine(root, "missing.xml"), Required: false),
                    new CatalogSource("xml", firstXmlPath),
                    new CatalogSource("xml", secondXmlPath)
                ]);

            var service = new CatalogService(
                new CatalogOptions
                {
                    CatalogRootPath = root,
                    MaxParsedXmlFiles = 1
                },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync(profileId, CancellationToken.None);

            catalog["unit_catalog"].Should().Contain("EMPIRE_BARRACKS");
            catalog["unit_catalog"].Should().NotContain("PLANET_CORUSCANT");
            catalog["unit_catalog"].Should().Contain("PALPATINE_FLAGSHIP");
            catalog["unit_catalog"].Should().Contain("CIS_DROID_FACTORY");
            catalog["unit_catalog"].Should().Contain("UNDERWORLD_MINE");
            catalog["unit_catalog"].Should().NotContain("REBEL_SHIPYARD");
            catalog["unit_catalog"].Should().NotContain("HERO_LUKE_SKYWALKER");

            catalog["planet_catalog"].Should().Contain("PLANET_CORUSCANT");
            catalog["hero_catalog"].Should().Contain("PALPATINE_FLAGSHIP");
            catalog["hero_catalog"].Should().NotContain("HERO_LUKE_SKYWALKER");
            catalog["faction_catalog"].Should().BeEmpty();
            catalog["building_catalog"].Should().Contain("EMPIRE_BARRACKS");
            catalog["building_catalog"].Should().Contain("CIS_DROID_FACTORY");
            catalog["building_catalog"].Should().Contain("UNDERWORLD_MINE");
            catalog["entity_catalog"].Should().Contain("Building|EMPIRE_BARRACKS");
            catalog["entity_catalog"].Should().Contain("Planet|PLANET_CORUSCANT");
            catalog["entity_catalog"].Should().Contain("Hero|PALPATINE_FLAGSHIP");
            catalog["action_constraints"].Should().Equal("a_heal", "z_spawn");
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
    public async Task LoadCatalogAsync_ShouldTreatJsonNullPrebuiltCatalog_AsFallbackToXmlSources()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileId = "null_prebuilt";
            var profileCatalogDir = Path.Combine(root, profileId);
            Directory.CreateDirectory(profileCatalogDir);
            await File.WriteAllTextAsync(Path.Combine(profileCatalogDir, "catalog.json"), "null");

            var xmlPath = Path.Combine(root, "catalog.xml");
            await File.WriteAllTextAsync(
                xmlPath,
                """
                <Root>
                  <Object Name="REBEL_SHIPYARD" />
                  <Object Name="VADER_TIE_DEFENDER" />
                </Root>
                """);

            var profile = CreateProfile(
                profileId,
                actions: CreateActions("spawn_entity"),
                catalogSources:
                [
                    new CatalogSource("xml", xmlPath)
                ]);

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync(profileId, CancellationToken.None);

            catalog["unit_catalog"].Should().Contain("REBEL_SHIPYARD");
            catalog["unit_catalog"].Should().Contain("VADER_TIE_DEFENDER");
            catalog["hero_catalog"].Should().Contain("VADER_TIE_DEFENDER");
            catalog["building_catalog"].Should().Contain("REBEL_SHIPYARD");
            catalog["action_constraints"].Should().Equal("spawn_entity");
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
    public async Task LoadTypedCatalogAsync_ShouldResolveDisplayText_AndExposeResolvedIconPathMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileId = "typed_catalog_with_text";
            var dataDir = Path.Combine(root, "Data", "XML");
            var textDir = Path.Combine(root, "Data", "Text");
            var iconDir = Path.Combine(root, "Art", "Textures", "UI");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(textDir);
            Directory.CreateDirectory(iconDir);

            var xmlPath = Path.Combine(dataDir, "ground-company.xml");
            var textPath = Path.Combine(textDir, "MasterTextFile_English.dat");
            var iconPath = Path.Combine(iconDir, "i_stormtrooper.png");

            await File.WriteAllTextAsync(
                xmlPath,
                """
                <Root>
                  <GroundCompany Name="EMPIRE_STORMTROOPER_SQUAD">
                    <Text_ID>TEXT_STORMTROOPER_SQUAD</Text_ID>
                    <Encyclopedia_Text>TEXT_STORMTROOPER_SQUAD_DESC</Encyclopedia_Text>
                    <Affiliation>EMPIRE</Affiliation>
                    <Population_Value>2</Population_Value>
                    <Build_Cost_Credits>150</Build_Cost_Credits>
                    <Icon_Name>i_stormtrooper.png</Icon_Name>
                  </GroundCompany>
                </Root>
                """);
            await File.WriteAllTextAsync(
                textPath,
                """
                TEXT_STORMTROOPER_SQUAD = "Stormtrooper Squad"
                TEXT_STORMTROOPER_SQUAD_DESC = "Imperial front-line infantry."
                """);
            await File.WriteAllBytesAsync(iconPath, [0x89, 0x50, 0x4E, 0x47]);

            var profile = CreateProfile(
                profileId,
                actions: CreateActions("spawn_context_entity"),
                catalogSources:
                [
                    new CatalogSource("xml", xmlPath)
                ]);

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var snapshot = await service.LoadTypedCatalogAsync(profileId, CancellationToken.None);

            var record = snapshot.Entities.Should().ContainSingle().Subject;
            record.DisplayNameKey.Should().Be("TEXT_STORMTROOPER_SQUAD");
            record.DisplayName.Should().Be("Stormtrooper Squad");
            record.DisplayNameSourcePath.Should().Be(textPath);
            record.VisualRef.Should().Be(iconPath);
            record.IconCachePath.Should().Be(iconPath);
            record.Metadata.Should().ContainKey("iconCachePath").WhoseValue.Should().Be(iconPath);
            record.Metadata.Should().ContainKey("displayNameSourcePath").WhoseValue.Should().Be(textPath);
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
    public async Task LoadCatalogAsync_ShouldNormalizeNullAndWhitespaceEntries_WhenMergingPrebuiltCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileId = "prebuilt_normalized";
            var profileCatalogDir = Path.Combine(root, profileId);
            Directory.CreateDirectory(profileCatalogDir);

            await File.WriteAllTextAsync(
                Path.Combine(profileCatalogDir, "catalog.json"),
                """
                {
                  "unit_catalog": [
                    " EMPIRE_BASE ",
                    "  ",
                    null,
                    "empire_base",
                    "PLANET_ALDERAAN"
                  ],
                  "planet_catalog": [
                    "PLANET_ALDERAAN",
                    " PLANET_ALDERAAN "
                  ],
                  "hero_catalog": null,
                  "building_catalog": [
                    " OLD_TURRET ",
                    null,
                    "old_turret"
                  ],
                  "entity_catalog": [
                    " Unit|Legacy ",
                    " ",
                    null
                  ],
                  "faction_catalog": [
                    " Empire ",
                    "CIS",
                    null
                  ]
                }
                """);

            var profile = CreateProfile(
                profileId,
                actions: CreateActions("set_owner", "spawn_entity"));

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync(profileId, CancellationToken.None);

            catalog["building_catalog"].Should().Contain("EMPIRE_BASE");
            catalog["building_catalog"].Should().Contain("OLD_TURRET");
            catalog["building_catalog"].Should().OnlyHaveUniqueItems();

            catalog["entity_catalog"].Should().Contain("Unit|Legacy");
            catalog["entity_catalog"].Should().Contain("Building|EMPIRE_BASE");
            catalog["entity_catalog"].Should().Contain("Building|OLD_TURRET");
            catalog["entity_catalog"].Should().Contain("Planet|PLANET_ALDERAAN");
            catalog["entity_catalog"].Should().NotContain(value => string.IsNullOrWhiteSpace(value));
            catalog["entity_catalog"].Should().NotContain(value => value.StartsWith("Hero|", StringComparison.OrdinalIgnoreCase));

            catalog["action_constraints"].Should().Equal("set_owner", "spawn_entity");
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

    private static IReadOnlyDictionary<string, ActionSpec> CreateActions(params string[] actionIds)
    {
        var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var actionId in actionIds)
        {
            actions[actionId] = new ActionSpec(
                Id: actionId,
                Category: ActionCategory.Global,
                Mode: RuntimeMode.Galactic,
                ExecutionKind: ExecutionKind.Memory,
                PayloadSchema: new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0);
        }

        return actions;
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
        IReadOnlyDictionary<string, ActionSpec>? actions = null,
        IReadOnlyList<CatalogSource>? catalogSources = null)
    {
        return new TrainerProfile(
            Id: profileId,
            DisplayName: "test profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: actions ?? new Dictionary<string, ActionSpec>(),
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
