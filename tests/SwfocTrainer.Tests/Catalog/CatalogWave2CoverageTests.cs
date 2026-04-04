using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Catalog.Config;
using SwfocTrainer.Catalog.Services;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Catalog;

/// <summary>
/// Wave 2 coverage: fills remaining branches in CatalogService —
/// building name markers, faction detection, hero detection, entity catalog
/// projection, action_constraints, and edge cases in GetCatalogSet.
/// </summary>
public sealed class CatalogWave2CoverageTests
{
    [Fact]
    public async Task LoadCatalogAsync_ShouldDetectAllBuildingMarkers_FromXmlSource()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-w2-{Guid.NewGuid():N}");
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
  <Unit Name="REGULAR_UNIT" />
</Units>
""");

            var profile = CreateProfileWithSources("test", new[]
            {
                new CatalogSource("xml", xmlPath)
            });

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test", CancellationToken.None);

            // All building markers should be recognized
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
            // Regular unit should NOT be in building catalog
            catalog["building_catalog"].Should().NotContain("REGULAR_UNIT");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadCatalogAsync_ShouldPopulateEntityCatalog_WithAllCategoryPrefixes()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-w2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var xmlDir = Path.Join(root, "xml");
        Directory.CreateDirectory(xmlDir);

        try
        {
            var xmlPath = Path.Join(xmlDir, "entities.xml");
            await File.WriteAllTextAsync(xmlPath, """
<Units>
  <Unit Name="INFANTRY_SQUAD" />
  <Unit Name="PLANET_TATOOINE" />
  <Unit Name="HERO_LUKE" />
  <Unit Name="EMPIRE_BARRACKS" />
</Units>
""");

            var profile = CreateProfileWithSources("test", new[]
            {
                new CatalogSource("xml", xmlPath)
            });

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test");

            catalog["entity_catalog"].Should().Contain("Unit|INFANTRY_SQUAD");
            catalog["entity_catalog"].Should().Contain("Planet|PLANET_TATOOINE");
            catalog["entity_catalog"].Should().Contain("Hero|HERO_LUKE");
            catalog["entity_catalog"].Should().Contain("Building|EMPIRE_BARRACKS");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadCatalogAsync_ShouldEmitActionConstraints_FromProfileActions()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-w2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profile = new TrainerProfile(
                Id: "test",
                DisplayName: "test",
                Inherits: null,
                ExeTarget: ExeTarget.Swfoc,
                SteamWorkshopId: null,
                SignatureSets: Array.Empty<SignatureSet>(),
                FallbackOffsets: new Dictionary<string, long>(),
                Actions: new Dictionary<string, ActionSpec>
                {
                    ["set_credits"] = new("set_credits", ActionCategory.Campaign, RuntimeMode.Galactic,
                        ExecutionKind.Memory, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
                    ["freeze_timer"] = new("freeze_timer", ActionCategory.Tactical, RuntimeMode.AnyTactical,
                        ExecutionKind.Memory, new JsonObject(), VerifyReadback: false, CooldownMs: 0)
                },
                FeatureFlags: new Dictionary<string, bool>(),
                CatalogSources: Array.Empty<CatalogSource>(),
                SaveSchemaId: "schema",
                HelperModHooks: Array.Empty<HelperHookSpec>(),
                Metadata: new Dictionary<string, string>());

            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test");

            catalog["action_constraints"].Should().Contain("freeze_timer");
            catalog["action_constraints"].Should().Contain("set_credits");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadCatalogAsync_ShouldHandlePrebuiltCatalogWithEmptyValues()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-w2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileCatalogDir = Path.Join(root, "test");
            Directory.CreateDirectory(profileCatalogDir);

            await File.WriteAllTextAsync(Path.Join(profileCatalogDir, "catalog.json"), """
{
  "unit_catalog": ["UNIT_A", "  ", "UNIT_B"],
  "planet_catalog": [],
  "hero_catalog": [],
  "faction_catalog": []
}
""");

            var profile = CreateProfile("test");
            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test");

            catalog["unit_catalog"].Should().Contain("UNIT_A");
            catalog["unit_catalog"].Should().Contain("UNIT_B");
            catalog.Should().ContainKey("building_catalog");
            catalog.Should().ContainKey("entity_catalog");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadCatalogAsync_ShouldRecognizeCIS_AsFaction()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-w2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var xmlDir = Path.Join(root, "xml");
        Directory.CreateDirectory(xmlDir);

        try
        {
            var xmlPath = Path.Join(xmlDir, "factions.xml");
            await File.WriteAllTextAsync(xmlPath, """
<Factions>
  <Faction Name="CIS_DROID_ARMY" />
  <Faction Name="UNDERWORLD_CRIME_LORD" />
</Factions>
""");

            var profile = CreateProfileWithSources("test", new[] { new CatalogSource("xml", xmlPath) });
            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test");

            catalog["faction_catalog"].Should().Contain("CIS_DROID_ARMY");
            catalog["faction_catalog"].Should().Contain("UNDERWORLD_CRIME_LORD");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadCatalogAsync_ShouldRecognizePalpatine_AsHero()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-w2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var xmlDir = Path.Join(root, "xml");
        Directory.CreateDirectory(xmlDir);

        try
        {
            var xmlPath = Path.Join(xmlDir, "heroes.xml");
            await File.WriteAllTextAsync(xmlPath, """
<Units>
  <Unit Name="EMPEROR_PALPATINE_THRONE" />
  <Unit Name="DARTH_VADER_EXECUTOR" />
</Units>
""");

            var profile = CreateProfileWithSources("test", new[] { new CatalogSource("xml", xmlPath) });
            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test");

            catalog["hero_catalog"].Should().Contain("EMPEROR_PALPATINE_THRONE");
            catalog["hero_catalog"].Should().Contain("DARTH_VADER_EXECUTOR");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadCatalogAsync_ShouldHandleEmptyCatalogJson()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-catalog-w2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var profileCatalogDir = Path.Join(root, "test");
            Directory.CreateDirectory(profileCatalogDir);
            await File.WriteAllTextAsync(Path.Join(profileCatalogDir, "catalog.json"), "{}");

            var profile = CreateProfile("test");
            var service = new CatalogService(
                new CatalogOptions { CatalogRootPath = root },
                new StubProfileRepository(profile),
                NullLogger<CatalogService>.Instance);

            var catalog = await service.LoadCatalogAsync("test");

            catalog.Should().ContainKey("building_catalog");
            catalog.Should().ContainKey("entity_catalog");
            catalog.Should().ContainKey("action_constraints");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static TrainerProfile CreateProfile(string profileId)
    {
        return new TrainerProfile(
            Id: profileId,
            DisplayName: "test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>());
    }

    private static TrainerProfile CreateProfileWithSources(string profileId, CatalogSource[] sources)
    {
        return new TrainerProfile(
            Id: profileId,
            DisplayName: "test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: sources,
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>());
    }

    private sealed class StubProfileRepository : IProfileRepository
    {
        private readonly TrainerProfile _profile;
        public StubProfileRepository(TrainerProfile profile) { _profile = profile; }
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct) => Task.FromResult(new ProfileManifest("1.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>()));
        public Task<TrainerProfile> LoadProfileAsync(string id, CancellationToken ct) => Task.FromResult(_profile);
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string id, CancellationToken ct) => Task.FromResult(_profile);
        public Task ValidateProfileAsync(TrainerProfile p, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(new[] { _profile.Id });
    }
}
