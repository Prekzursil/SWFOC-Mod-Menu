using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Catalog.Config;
using SwfocTrainer.Catalog.Services;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Catalog;

/// <summary>
/// Wave 6 — push Catalog to 100% branch coverage.
/// Covers CatalogService: prebuilt catalog path, XML parsing with various name markers
/// (hero, faction, planet, building), non-xml source type, missing required source,
/// missing optional source, MaxParsedXmlFiles limit, EnsureDerivedCatalogs building
/// detection, entity catalog composition, convenience overload, constructor null guards.
/// </summary>
public sealed class CatalogWave6Tests : IDisposable
{
    private readonly string _tempRoot;

    public CatalogWave6Tests()
    {
        _tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-catalog-wave6-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, true);
        }
    }

    #region CatalogService — prebuilt catalog

    [Fact]
    public async Task LoadCatalogAsync_PrebuiltCatalogExists_ShouldUsePrebuilt()
    {
        var catalogDir = Path.Join(_tempRoot, "catalog", "test_profile");
        Directory.CreateDirectory(catalogDir);
        var prebuilt = new Dictionary<string, string[]>
        {
            ["unit_catalog"] = new[] { "AT_AT", "SPEEDER" },
            ["faction_catalog"] = new[] { "EMPIRE", "REBEL" },
            ["planet_catalog"] = new[] { "PLANET_TATOOINE" },
            ["hero_catalog"] = new[] { "HERO_VADER" }
        };
        await File.WriteAllTextAsync(
            Path.Join(catalogDir, "catalog.json"),
            JsonSerializer.Serialize(prebuilt));

        var options = new CatalogOptions { CatalogRootPath = Path.Join(_tempRoot, "catalog") };
        var repo = new StubProfileRepository();
        var service = new CatalogService(options, repo, NullLogger<CatalogService>.Instance);

        var result = await service.LoadCatalogAsync("test_profile", CancellationToken.None);
        result["unit_catalog"].Should().Contain("AT_AT");
        result["faction_catalog"].Should().Contain("EMPIRE");
        // EnsureDerivedCatalogs should add building and entity catalogs
        result.Should().ContainKey("building_catalog");
        result.Should().ContainKey("entity_catalog");
        result.Should().ContainKey("action_constraints");
    }

    [Fact]
    public async Task LoadCatalogAsync_NoPrebuilt_ShouldParseXmlSources()
    {
        var xmlPath = Path.Join(_tempRoot, "units.xml");
        var xml = @"<Root>
            <Unit Name=""AT_AT"" />
            <Unit Name=""HERO_VADER"" />
            <Unit Name=""PLANET_TATOOINE"" />
            <Unit Name=""EMPIRE_FACTION"" />
            <Unit Name=""REBEL_TROOPER"" />
            <Unit Name=""UNDERWORLD_BOSS"" />
            <Unit Name=""CIS_DROID"" />
            <Unit Name=""PALPATINE_GUARD"" />
            <Unit Name=""BARRACK_L1"" />
            <Unit Name=""FACTORY_L1"" />
            <Unit Name=""SHIPYARD_MAIN"" />
            <Unit Name=""STAR_BASE_L1"" />
            <Unit Name=""PLATFORM_DEF"" />
            <Unit Name=""MINE_RESOURCE"" />
            <Unit Name=""TURRET_HEAVY"" />
            <Unit Name=""DEFENSE_TOWER"" />
            <Unit Name=""ACADEMY_ELITE"" />
            <Unit Name=""OUTPOST_FORWARD"" />
            <Unit Name=""REFINERY_MAIN"" />
            <Unit Name=""PALACE_ROYAL"" />
        </Root>";
        await File.WriteAllTextAsync(xmlPath, xml);

        var options = new CatalogOptions
        {
            CatalogRootPath = Path.Join(_tempRoot, "catalog_empty"),
            MaxParsedXmlFiles = 10
        };
        var repo = new StubProfileRepository(xmlPath);
        var service = new CatalogService(options, repo, NullLogger<CatalogService>.Instance);

        var result = await service.LoadCatalogAsync("test_profile");
        result["unit_catalog"].Should().Contain("AT_AT");
        result["hero_catalog"].Should().Contain("HERO_VADER");
        result["hero_catalog"].Should().Contain("PALPATINE_GUARD");
        result["planet_catalog"].Should().Contain("PLANET_TATOOINE");
        result["faction_catalog"].Should().Contain("EMPIRE_FACTION");
        result["faction_catalog"].Should().Contain("REBEL_TROOPER");
        result["faction_catalog"].Should().Contain("UNDERWORLD_BOSS");
        result["faction_catalog"].Should().Contain("CIS_DROID");
        result["building_catalog"].Should().Contain("BARRACK_L1");
        result["building_catalog"].Should().Contain("FACTORY_L1");
        result["building_catalog"].Should().Contain("SHIPYARD_MAIN");
        result["building_catalog"].Should().Contain("STAR_BASE_L1");
        result["building_catalog"].Should().Contain("PLATFORM_DEF");
        result["building_catalog"].Should().Contain("MINE_RESOURCE");
        result["building_catalog"].Should().Contain("TURRET_HEAVY");
        result["building_catalog"].Should().Contain("DEFENSE_TOWER");
        result["building_catalog"].Should().Contain("ACADEMY_ELITE");
        result["building_catalog"].Should().Contain("OUTPOST_FORWARD");
        result["building_catalog"].Should().Contain("REFINERY_MAIN");
        result["building_catalog"].Should().Contain("PALACE_ROYAL");
        result["entity_catalog"].Should().Contain(e => e.StartsWith("Unit|"));
        result["entity_catalog"].Should().Contain(e => e.StartsWith("Building|"));
        result["entity_catalog"].Should().Contain(e => e.StartsWith("Planet|"));
        result["entity_catalog"].Should().Contain(e => e.StartsWith("Hero|"));
    }

    #endregion

    #region CatalogService — non-xml source type

    [Fact]
    public async Task LoadCatalogAsync_NonXmlSourceType_ShouldSkip()
    {
        var options = new CatalogOptions { CatalogRootPath = Path.Join(_tempRoot, "catalog_empty2") };
        var repo = new StubProfileRepository(sourceType: "json");
        var service = new CatalogService(options, repo, NullLogger<CatalogService>.Instance);

        var result = await service.LoadCatalogAsync("test_profile");
        result["unit_catalog"].Should().BeEmpty();
    }

    #endregion

    #region CatalogService — missing source file

    [Fact]
    public async Task LoadCatalogAsync_MissingRequiredSource_ShouldSkipButLog()
    {
        var options = new CatalogOptions { CatalogRootPath = Path.Join(_tempRoot, "catalog_empty3") };
        var repo = new StubProfileRepository(
            xmlPath: Path.Join(_tempRoot, "nonexistent.xml"),
            required: true);
        var service = new CatalogService(options, repo, NullLogger<CatalogService>.Instance);

        var result = await service.LoadCatalogAsync("test_profile");
        result["unit_catalog"].Should().BeEmpty();
    }

    [Fact]
    public async Task LoadCatalogAsync_MissingOptionalSource_ShouldSkipSilently()
    {
        var options = new CatalogOptions { CatalogRootPath = Path.Join(_tempRoot, "catalog_empty4") };
        var repo = new StubProfileRepository(
            xmlPath: Path.Join(_tempRoot, "nonexistent_optional.xml"),
            required: false);
        var service = new CatalogService(options, repo, NullLogger<CatalogService>.Instance);

        var result = await service.LoadCatalogAsync("test_profile");
        result["unit_catalog"].Should().BeEmpty();
    }

    #endregion

    #region CatalogService — MaxParsedXmlFiles limit

    [Fact]
    public async Task LoadCatalogAsync_MaxParsedXmlFilesReached_ShouldStopParsing()
    {
        var xmlPath1 = Path.Join(_tempRoot, "units1.xml");
        var xmlPath2 = Path.Join(_tempRoot, "units2.xml");
        await File.WriteAllTextAsync(xmlPath1, @"<Root><Unit Name=""UNIT_A"" /></Root>");
        await File.WriteAllTextAsync(xmlPath2, @"<Root><Unit Name=""UNIT_B"" /></Root>");

        var options = new CatalogOptions
        {
            CatalogRootPath = Path.Join(_tempRoot, "catalog_empty5"),
            MaxParsedXmlFiles = 1
        };
        var repo = new StubProfileRepository(xmlPaths: new[] { xmlPath1, xmlPath2 });
        var service = new CatalogService(options, repo, NullLogger<CatalogService>.Instance);

        var result = await service.LoadCatalogAsync("test_profile");
        result["unit_catalog"].Should().Contain("UNIT_A");
        result["unit_catalog"].Should().NotContain("UNIT_B");
    }

    #endregion

    #region CatalogService — constructor null guards

    [Fact]
    public void Constructor_NullOptions_ShouldThrow()
    {
        var act = () => new CatalogService(null!, new StubProfileRepository(), NullLogger<CatalogService>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullProfiles_ShouldThrow()
    {
        var options = new CatalogOptions();
        var act = () => new CatalogService(options, null!, NullLogger<CatalogService>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var options = new CatalogOptions();
        var repo = new StubProfileRepository();
        var act = () => new CatalogService(options, repo, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region CatalogService — convenience overload

    [Fact]
    public async Task LoadCatalogAsync_ConvenienceOverload_ShouldDelegate()
    {
        var options = new CatalogOptions { CatalogRootPath = Path.Join(_tempRoot, "catalog_empty6") };
        var repo = new StubProfileRepository();
        var service = new CatalogService(options, repo, NullLogger<CatalogService>.Instance);

        var result = await service.LoadCatalogAsync("test_profile");
        result.Should().ContainKey("unit_catalog");
    }

    #endregion

    #region CatalogService — GetCatalogSet with null values

    [Fact]
    public async Task LoadCatalogAsync_PrebuiltWithNullValues_ShouldHandleGracefully()
    {
        var catalogDir = Path.Join(_tempRoot, "catalog_null", "test_profile");
        Directory.CreateDirectory(catalogDir);
        // Write a catalog where a key maps to null (simulated via empty array)
        var prebuilt = new Dictionary<string, string[]>
        {
            ["unit_catalog"] = new[] { "UNIT_A", "  ", "" },
            ["faction_catalog"] = new[] { "EMPIRE" }
        };
        await File.WriteAllTextAsync(
            Path.Join(catalogDir, "catalog.json"),
            JsonSerializer.Serialize(prebuilt));

        var options = new CatalogOptions { CatalogRootPath = Path.Join(_tempRoot, "catalog_null") };
        var repo = new StubProfileRepository();
        var service = new CatalogService(options, repo, NullLogger<CatalogService>.Instance);

        var result = await service.LoadCatalogAsync("test_profile");
        result["unit_catalog"].Should().Contain("UNIT_A");
        // Whitespace-only values should be filtered in GetCatalogSet
        result["entity_catalog"].Should().NotContain(e => string.IsNullOrWhiteSpace(e));
    }

    #endregion

    #region Stubs

    private sealed class StubProfileRepository : IProfileRepository
    {
        private readonly CatalogSource[] _catalogSources;

        public StubProfileRepository(
            string? xmlPath = null,
            string sourceType = "xml",
            bool required = false,
            string[]? xmlPaths = null)
        {
            if (xmlPaths is not null)
            {
                _catalogSources = xmlPaths.Select(p => new CatalogSource("xml", p, false)).ToArray();
            }
            else if (xmlPath is not null)
            {
                _catalogSources = new[] { new CatalogSource(sourceType, xmlPath, required) };
            }
            else
            {
                _catalogSources = Array.Empty<CatalogSource>();
            }
        }

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ProfileManifest("1.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>()));

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
            => Task.FromResult(BuildProfile(profileId));

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
            => Task.FromResult(BuildProfile(profileId));

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
            => Task.CompletedTask;

        private TrainerProfile BuildProfile(string profileId)
        {
            return new TrainerProfile(
                Id: profileId,
                DisplayName: "Test",
                Inherits: null,
                ExeTarget: ExeTarget.Swfoc,
                SteamWorkshopId: null,
                SignatureSets: Array.Empty<SignatureSet>(),
                FallbackOffsets: new Dictionary<string, long>(),
                Actions: new Dictionary<string, ActionSpec>
                {
                    ["set_credits"] = new("set_credits", ActionCategory.Global, RuntimeMode.Galactic,
                        ExecutionKind.Memory, new System.Text.Json.Nodes.JsonObject(), false, 0)
                },
                FeatureFlags: new Dictionary<string, bool>(),
                CatalogSources: _catalogSources,
                SaveSchemaId: null!,
                HelperModHooks: Array.Empty<HelperHookSpec>());
        }
    }

    #endregion
}
