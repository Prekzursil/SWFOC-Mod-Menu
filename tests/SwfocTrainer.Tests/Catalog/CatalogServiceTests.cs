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
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileId = "test_profile";
            var profileCatalogDir = Path.Join(root, profileId);
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

    private static async Task WriteCatalogFixtureAsync(string profileCatalogDir)
    {
        var catalogPath = Path.Join(profileCatalogDir, "catalog.json");
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

    private static TrainerProfile CreateProfile(string profileId)
    {
        return new TrainerProfile(
            Id: profileId,
            DisplayName: "test profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "test_schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>());
    }

    [Fact]
    public async Task LoadCatalogAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(CreateProfile("test")),
                NullLogger<CatalogService>.Instance);

            var act = async () => await service.LoadCatalogAsync(null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
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
    public async Task LoadCatalogAsync_ShouldParseXmlSources_WhenNoPrebuiltCatalog()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var xmlDir = Path.Join(root, "xml");
        Directory.CreateDirectory(xmlDir);

        try
        {
            var xmlPath = Path.Join(xmlDir, "units.xml");
            await File.WriteAllTextAsync(xmlPath, """
<Units>
  <Unit Name="EMPIRE_STORMTROOPER_SQUAD" Type="EMPIRE" />
  <Unit Name="PLANET_CORUSCANT" />
  <Unit Name="HERO_VADER" />
  <Unit Name="REBEL_ALLIANCE" />
  <Unit Name="EMPIRE_BARRACKS" />
</Units>
""");

            var profile = CreateProfileWithCatalogSources("test_profile", new[]
            {
                new CatalogSource("xml", xmlPath)
            });

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test_profile", CancellationToken.None);

            catalog["unit_catalog"].Should().Contain("EMPIRE_STORMTROOPER_SQUAD");
            catalog["planet_catalog"].Should().Contain("PLANET_CORUSCANT");
            catalog["hero_catalog"].Should().Contain("HERO_VADER");
            catalog["faction_catalog"].Should().Contain("REBEL_ALLIANCE");
            catalog["building_catalog"].Should().Contain("EMPIRE_BARRACKS");
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
    public async Task LoadCatalogAsync_ShouldSkipNonXmlSources()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profile = CreateProfileWithCatalogSources("test_profile", new[]
            {
                new CatalogSource("json", "some/path.json")
            });

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test_profile");

            catalog["unit_catalog"].Should().BeEmpty();
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
    public async Task LoadCatalogAsync_ShouldSkipMissingXmlSource_WhenNotRequired()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profile = CreateProfileWithCatalogSources("test_profile", new[]
            {
                new CatalogSource("xml", Path.Join(root, "nonexistent.xml"), Required: false)
            });

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test_profile");

            catalog["unit_catalog"].Should().BeEmpty();
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
    public async Task LoadCatalogAsync_ShouldLogWarning_WhenRequiredXmlSourceIsMissing()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profile = CreateProfileWithCatalogSources("test_profile", new[]
            {
                new CatalogSource("xml", Path.Join(root, "nonexistent.xml"), Required: true)
            });

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test_profile");

            catalog["unit_catalog"].Should().BeEmpty();
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
    public async Task LoadCatalogAsync_ShouldRecognizeHeroNames()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var xmlDir = Path.Join(root, "xml");
        Directory.CreateDirectory(xmlDir);

        try
        {
            var xmlPath = Path.Join(xmlDir, "heroes.xml");
            await File.WriteAllTextAsync(xmlPath, """
<Units>
  <Unit Name="DARTH_VADER" />
  <Unit Name="EMPEROR_PALPATINE" />
</Units>
""");

            var profile = CreateProfileWithCatalogSources("test_profile", new[]
            {
                new CatalogSource("xml", xmlPath)
            });

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test_profile");

            catalog["hero_catalog"].Should().Contain("DARTH_VADER");
            catalog["hero_catalog"].Should().Contain("EMPEROR_PALPATINE");
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
    public async Task LoadCatalogAsync_ShouldRecognizeFactionNames()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var xmlDir = Path.Join(root, "xml");
        Directory.CreateDirectory(xmlDir);

        try
        {
            var xmlPath = Path.Join(xmlDir, "factions.xml");
            await File.WriteAllTextAsync(xmlPath, """
<Factions>
  <Faction Name="GALACTIC_EMPIRE" />
  <Faction Name="REBEL_ALLIANCE" />
  <Faction Name="UNDERWORLD_CONSORTIUM" />
  <Faction Name="CIS_FEDERATION" />
</Factions>
""");

            var profile = CreateProfileWithCatalogSources("test_profile", new[]
            {
                new CatalogSource("xml", xmlPath)
            });

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test_profile");

            catalog["faction_catalog"].Should().Contain("GALACTIC_EMPIRE");
            catalog["faction_catalog"].Should().Contain("REBEL_ALLIANCE");
            catalog["faction_catalog"].Should().Contain("UNDERWORLD_CONSORTIUM");
            catalog["faction_catalog"].Should().Contain("CIS_FEDERATION");
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
    public async Task LoadCatalogAsync_SingleParamOverload_ShouldWork()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(CreateProfile("test")),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test");
            catalog.Should().NotBeNull();
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
    public async Task LoadCatalogAsync_ShouldRespectMaxParsedXmlFilesLimit()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var xmlDir = Path.Join(root, "xml");
        Directory.CreateDirectory(xmlDir);

        try
        {
            var sources = new List<CatalogSource>();
            for (var i = 0; i < 5; i++)
            {
                var xmlPath = Path.Join(xmlDir, $"units_{i}.xml");
                await File.WriteAllTextAsync(xmlPath, $"""<Units><Unit Name="UNIT_{i}" /></Units>""");
                sources.Add(new CatalogSource("xml", xmlPath));
            }

            var profile = CreateProfileWithCatalogSources("test_profile", sources.ToArray());
            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root, MaxParsedXmlFiles = 2 },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test_profile");

            catalog["unit_catalog"].Should().HaveCount(2);
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
    public void Constructor_ShouldThrow_WhenDependenciesAreNull()
    {
        var act1 = () => new CatalogService(null!, new StubProfileRepository(CreateProfile("test")), NullLogger<CatalogService>.Instance);
        var act2 = () => new CatalogService(new CatalogOptions(), null!, NullLogger<CatalogService>.Instance);
        var act3 = () => new CatalogService(new CatalogOptions(), new StubProfileRepository(CreateProfile("test")), null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task LoadCatalogAsync_ShouldRecognizeBuildingNames()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var xmlDir = Path.Join(root, "xml");
        Directory.CreateDirectory(xmlDir);

        try
        {
            var xmlPath = Path.Join(xmlDir, "buildings.xml");
            await File.WriteAllTextAsync(xmlPath, """
<Units>
  <Unit Name="EMPIRE_BARRACKS" />
  <Unit Name="REBEL_LIGHT_FACTORY" />
  <Unit Name="GROUND_BASE" />
  <Unit Name="IMPERIAL_SHIPYARD" />
  <Unit Name="SPACE_YARD" />
  <Unit Name="SPACE_STATION" />
  <Unit Name="STAR_BASE" />
  <Unit Name="IMPERIAL_STARBASE" />
  <Unit Name="GUN_PLATFORM" />
  <Unit Name="SPICE_MINE" />
  <Unit Name="LASER_TURRET" />
  <Unit Name="ORBITAL_DEFENSE" />
  <Unit Name="OFFICER_ACADEMY" />
  <Unit Name="BORDER_OUTPOST" />
  <Unit Name="TIBANNA_REFINERY" />
  <Unit Name="IMPERIAL_PALACE" />
</Units>
""");

            var profile = CreateProfileWithCatalogSources("test_profile", new[]
            {
                new CatalogSource("xml", xmlPath)
            });

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test_profile");

            catalog["building_catalog"].Should().Contain("EMPIRE_BARRACKS");
            catalog["building_catalog"].Should().Contain("REBEL_LIGHT_FACTORY");
            catalog["building_catalog"].Should().Contain("GROUND_BASE");
            catalog["building_catalog"].Should().Contain("IMPERIAL_SHIPYARD");
            catalog["building_catalog"].Should().Contain("SPACE_YARD");
            catalog["building_catalog"].Should().Contain("SPACE_STATION");
            catalog["building_catalog"].Should().Contain("STAR_BASE");
            catalog["building_catalog"].Should().Contain("IMPERIAL_STARBASE");
            catalog["building_catalog"].Should().Contain("GUN_PLATFORM");
            catalog["building_catalog"].Should().Contain("SPICE_MINE");
            catalog["building_catalog"].Should().Contain("LASER_TURRET");
            catalog["building_catalog"].Should().Contain("ORBITAL_DEFENSE");
            catalog["building_catalog"].Should().Contain("OFFICER_ACADEMY");
            catalog["building_catalog"].Should().Contain("BORDER_OUTPOST");
            catalog["building_catalog"].Should().Contain("TIBANNA_REFINERY");
            catalog["building_catalog"].Should().Contain("IMPERIAL_PALACE");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static TrainerProfile CreateProfileWithCatalogSources(string profileId, CatalogSource[] sources)
    {
        return new TrainerProfile(
            Id: profileId,
            DisplayName: "test profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: sources,
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
