using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Catalog.Config;
using SwfocTrainer.Catalog.Services;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using System.Reflection;
using System.Text.Json.Nodes;
using Xunit;

namespace SwfocTrainer.Tests.Catalog;

public sealed class CatalogServiceTests
{
    [Fact]
    public void CatalogHelperMethods_ShouldNormalizeListValuesAndFallbackSelection()
    {
        InvokePrivateStatic<IReadOnlyList<string>>("ParseListValue", " Empire, Rebel ; Pirate  ")
            .Should().Equal("Empire", "Rebel", "Pirate");
        InvokePrivateStatic<int?>("ParseOptionalInt", " 42 ").Should().Be(42);
        InvokePrivateStatic<int?>("ParseOptionalInt", "bad").Should().BeNull();
        InvokePrivateStatic<string?>("ChooseValue", null, "incoming", "fallback").Should().Be("incoming");
        InvokePrivateStatic<string?>("ChooseValue", "existing", null, "fallback").Should().Be("existing");
        InvokePrivateStatic<CatalogEntityCompatibilityState>(
            "SelectCompatibilityState",
            CatalogEntityCompatibilityState.Unknown,
            CatalogEntityCompatibilityState.Blocked).Should().Be(CatalogEntityCompatibilityState.Blocked);
    }

    [Fact]
    public async Task LoadTypedCatalogAsync_ShouldProjectLegacyPrebuiltCatalogIntoTypedEntities()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-typed-prebuilt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileId = "typed_prebuilt_profile";
            var profileCatalogDir = Path.Combine(root, profileId);
            Directory.CreateDirectory(profileCatalogDir);

            await WriteCatalogFixtureAsync(profileCatalogDir);

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(CreateProfile(profileId)),
                NullLogger<CatalogService>.Instance);

            var snapshot = await service.LoadTypedCatalogAsync(profileId, CancellationToken.None);

            snapshot.ProfileId.Should().Be(profileId);
            snapshot.Entities.Should().Contain(record => record.EntityId == "EMPIRE_STORMTROOPER_SQUAD" && record.Kind == CatalogEntityKind.Unit);
            snapshot.Entities.Should().Contain(record => record.EntityId == "EMPIRE_BARRACKS" && record.Kind == CatalogEntityKind.Building);
            snapshot.Entities.Should().Contain(record => record.EntityId == "HERO_VADER" && record.Kind == CatalogEntityKind.Hero);
            snapshot.Entities.Should().Contain(record => record.EntityId == "PLANET_CORUSCANT" && record.Kind == CatalogEntityKind.Planet);
            snapshot.Entities.Should().Contain(record => record.EntityId == "EMPIRE" && record.Kind == CatalogEntityKind.Faction);
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

    [Fact]
    public async Task LoadTypedCatalogAsync_ShouldParseStructuredMetadata_WhenPrebuiltMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-typed-xml-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileId = "typed_xml_profile";
            var dataRoot = Path.Combine(root, "data");
            Directory.CreateDirectory(dataRoot);
            var xmlPath = Path.Combine(dataRoot, "Objects.xml");
            await File.WriteAllTextAsync(
                xmlPath,
                """
                <Root>
                  <LandUnit Name="EMPIRE_STORMTROOPER_SQUAD">
                    <Text_ID>TEXT_STORMTROOPER</Text_ID>
                    <Encyclopedia_Text>TEXT_STORMTROOPER_DESC</Encyclopedia_Text>
                    <Affiliation>EMPIRE</Affiliation>
                    <Population_Value>2</Population_Value>
                    <Build_Cost_Credits>200</Build_Cost_Credits>
                    <Icon_Name>i_stormtrooper.tga</Icon_Name>
                    <Required_Structures>EMPIRE_BARRACKS</Required_Structures>
                    <Company_Unit>STORMTROOPER_COMPANY</Company_Unit>
                  </LandUnit>
                  <Structure ID="EMPIRE_BARRACKS">
                    <Text_ID>TEXT_BARRACKS</Text_ID>
                    <Affiliation>EMPIRE</Affiliation>
                    <Icon_Name>i_barracks.tga</Icon_Name>
                    <Required_Prerequisites>TECH_1</Required_Prerequisites>
                  </Structure>
                  <Hero Object_Name="HERO_VADER">
                    <Text_ID>TEXT_VADER</Text_ID>
                    <Affiliation>EMPIRE</Affiliation>
                    <Icon_Name>i_vader.tga</Icon_Name>
                  </Hero>
                  <Planet Type="PLANET_CORUSCANT">
                    <Text_ID>TEXT_CORUSCANT</Text_ID>
                  </Planet>
                </Root>
                """);

            var profile = CreateProfile(
                profileId,
                catalogSources:
                [
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

            var snapshot = await service.LoadTypedCatalogAsync(profileId, CancellationToken.None);

            var stormtrooper = snapshot.Entities.Should().ContainSingle(record => record.EntityId == "EMPIRE_STORMTROOPER_SQUAD").Subject;
            stormtrooper.Kind.Should().Be(CatalogEntityKind.Unit);
            stormtrooper.DisplayNameKey.Should().Be("TEXT_STORMTROOPER");
            stormtrooper.DisplayName.Should().Be("TEXT_STORMTROOPER");
            stormtrooper.EncyclopediaTextKey.Should().Be("TEXT_STORMTROOPER_DESC");
            stormtrooper.Affiliations.Should().ContainSingle("EMPIRE");
            stormtrooper.PopulationValue.Should().Be(2);
            stormtrooper.BuildCostCredits.Should().Be(200);
            stormtrooper.VisualRef.Should().Be("i_stormtrooper.tga");
            stormtrooper.VisualState.Should().Be(CatalogEntityVisualState.Missing);
            stormtrooper.CompatibilityState.Should().Be(CatalogEntityCompatibilityState.Blocked);
            stormtrooper.DependencyRefs.Should().Contain(["EMPIRE_BARRACKS", "STORMTROOPER_COMPANY"]);

            var building = snapshot.Entities.Should().ContainSingle(record => record.EntityId == "EMPIRE_BARRACKS").Subject;
            building.Kind.Should().Be(CatalogEntityKind.Building);
            building.DependencyRefs.Should().Contain("TECH_1");

            var hero = snapshot.Entities.Should().ContainSingle(record => record.EntityId == "HERO_VADER").Subject;
            hero.Kind.Should().Be(CatalogEntityKind.Hero);
            hero.VisualRef.Should().Be("i_vader.tga");

            var planet = snapshot.Entities.Should().ContainSingle(record => record.EntityId == "PLANET_CORUSCANT").Subject;
            planet.Kind.Should().Be(CatalogEntityKind.Planet);
            planet.DisplayNameKey.Should().Be("TEXT_CORUSCANT");
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
    public async Task LoadTypedCatalogAsync_ShouldMergeDuplicateXmlEntries_AndPreserveSpecificMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileId = "merge_profile";
            var dataRoot = Path.Combine(root, "data");
            Directory.CreateDirectory(dataRoot);

            var unitsPath = Path.Combine(dataRoot, "Units.xml");
            await File.WriteAllTextAsync(
                unitsPath,
                """
                <Root>
                  <LandUnit Name="EMPIRE_STORMTROOPER_SQUAD">
                    <Text_ID>TEXT_STORMTROOPER</Text_ID>
                    <Affiliation>EMPIRE</Affiliation>
                    <Required_Structures>EMPIRE_BARRACKS</Required_Structures>
                  </LandUnit>
                </Root>
                """);

            var overridesPath = Path.Combine(dataRoot, "Overrides.xml");
            await File.WriteAllTextAsync(
                overridesPath,
                """
                <Root>
                  <LandUnit Name="EMPIRE_STORMTROOPER_SQUAD">
                    <Build_Cost_Credits>200</Build_Cost_Credits>
                    <Population_Value>2</Population_Value>
                    <Icon_Name>i_stormtrooper.tga</Icon_Name>
                    <Required_Prerequisites>TECH_1</Required_Prerequisites>
                  </LandUnit>
                </Root>
                """);

            var profile = CreateProfile(
                profileId,
                catalogSources:
                [
                    new CatalogSource("xml", unitsPath, Required: true),
                    new CatalogSource("xml", overridesPath, Required: true)
                ]);

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root, MaxParsedXmlFiles = 10 },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var snapshot = await service.LoadTypedCatalogAsync(profileId, CancellationToken.None);

            var unit = snapshot.Entities.Should().ContainSingle(record => record.EntityId == "EMPIRE_STORMTROOPER_SQUAD").Subject;
            unit.DisplayNameKey.Should().Be("TEXT_STORMTROOPER");
            unit.Affiliations.Should().ContainSingle("EMPIRE");
            unit.DependencyRefs.Should().Contain(["EMPIRE_BARRACKS", "TECH_1"]);
            unit.BuildCostCredits.Should().Be(200);
            unit.PopulationValue.Should().Be(2);
            unit.VisualRef.Should().Be("i_stormtrooper.tga");
            unit.VisualState.Should().Be(CatalogEntityVisualState.Missing);
            unit.CompatibilityState.Should().Be(CatalogEntityCompatibilityState.Blocked);
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
    public async Task LoadTypedCatalogAsync_ShouldResolveVisualRefs_WhenAssetExists()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-visual-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileId = "visual_profile";
            var dataRoot = Path.Combine(root, "Data");
            var textureRoot = Path.Combine(root, "Art", "Textures", "UI");
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(textureRoot);

            var xmlPath = Path.Combine(dataRoot, "Objects.xml");
            var iconPath = Path.Combine(textureRoot, "i_stormtrooper.tga");
            await File.WriteAllTextAsync(iconPath, "fake-icon");
            await File.WriteAllTextAsync(
                xmlPath,
                """
                <Root>
                  <LandUnit Name="EMPIRE_STORMTROOPER_SQUAD">
                    <Text_ID>TEXT_STORMTROOPER</Text_ID>
                    <Affiliation>EMPIRE</Affiliation>
                    <Icon_Name>i_stormtrooper.tga</Icon_Name>
                  </LandUnit>
                </Root>
                """);

            var profile = CreateProfile(
                profileId,
                catalogSources:
                [
                    new CatalogSource("xml", xmlPath, Required: true)
                ]);

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root, MaxParsedXmlFiles = 10 },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var snapshot = await service.LoadTypedCatalogAsync(profileId, CancellationToken.None);

            var unit = snapshot.Entities.Should().ContainSingle(record => record.EntityId == "EMPIRE_STORMTROOPER_SQUAD").Subject;
            unit.VisualRef.Should().Be(iconPath);
            unit.VisualState.Should().Be(CatalogEntityVisualState.Resolved);
            unit.CompatibilityState.Should().Be(CatalogEntityCompatibilityState.Unknown);
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
    public async Task LoadCatalogAsync_ShouldExposeTypedEntityProjection_ForRosterConsumers()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-typed-projection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileId = "typed_projection_profile";
            var dataRoot = Path.Combine(root, "Data");
            Directory.CreateDirectory(dataRoot);
            var xmlPath = Path.Combine(dataRoot, "Objects.xml");
            await File.WriteAllTextAsync(
                xmlPath,
                """
                <Root>
                  <LandUnit Name="EMPIRE_STORMTROOPER_SQUAD">
                    <Text_ID>TEXT_STORMTROOPER</Text_ID>
                    <Affiliation>EMPIRE</Affiliation>
                    <Population_Value>2</Population_Value>
                    <Build_Cost_Credits>200</Build_Cost_Credits>
                    <Icon_Name>i_stormtrooper.tga</Icon_Name>
                  </LandUnit>
                </Root>
                """);

            var profile = CreateProfile(
                profileId,
                catalogSources:
                [
                    new CatalogSource("xml", xmlPath, Required: true)
                ]);

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root, MaxParsedXmlFiles = 10 },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync(profileId, CancellationToken.None);

            catalog.Should().ContainKey("entity_catalog_typed");
            var typedRow = JsonNode.Parse(catalog["entity_catalog_typed"].Single())!.AsObject();
            typedRow["entityId"]!.GetValue<string>().Should().Be("EMPIRE_STORMTROOPER_SQUAD");
            typedRow["displayNameKey"]!.GetValue<string>().Should().Be("TEXT_STORMTROOPER");
            typedRow["populationValue"]!.GetValue<int>().Should().Be(2);
            typedRow["buildCostCredits"]!.GetValue<int>().Should().Be(200);
            typedRow["visualState"]!.GetValue<string>().Should().Be(nameof(CatalogEntityVisualState.Missing));
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
    public async Task LoadCatalogAsync_ShouldIgnoreUnsupportedAndMissingSources_WithoutThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileId = "missing_profile";
            var missingXml = Path.Combine(root, "missing.xml");
            var profile = CreateProfile(
                profileId,
                catalogSources:
                [
                    new CatalogSource("file", missingXml, Required: false),
                    new CatalogSource("xml", missingXml, Required: false)
                ]);

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root, MaxParsedXmlFiles = 10 },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync(profileId, CancellationToken.None);

            catalog["unit_catalog"].Should().BeEmpty();
            catalog["building_catalog"].Should().BeEmpty();
            catalog["entity_catalog"].Should().BeEmpty();
            catalog["action_constraints"].Should().ContainSingle("spawn_tactical_entity");
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

    private static T InvokePrivateStatic<T>(string methodName, params object?[] args)
    {
        var method = typeof(CatalogService).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull($"Expected helper '{methodName}'");
        return (T)method!.Invoke(null, args)!;
    }
}
