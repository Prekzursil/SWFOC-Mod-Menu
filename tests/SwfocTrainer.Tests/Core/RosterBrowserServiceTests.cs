using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class RosterBrowserServiceTests
{
    [Fact]
    public async Task LoadRosterAsync_WithPopulatedCatalog_ReturnsEntriesWithCorrectFields()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["hero_catalog"] = new[] { "HERO_VADER" },
            ["unit_catalog"] = new[] { "EMPIRE_STORMTROOPER_SQUAD" },
            ["building_catalog"] = new[] { "REBEL_LIGHT_FACTORY" }
        };

        var service = CreateService(catalog);

        var result = await service.LoadRosterAsync("test-profile", CancellationToken.None);

        result.Should().HaveCount(3);

        var hero = result.First(e => e.EntityId == "HERO_VADER");
        hero.DisplayName.Should().Be("Hero Vader");
        hero.Kind.Should().Be(RosterEntityKind.Hero);
        hero.Category.Should().Be("hero_catalog");

        var unit = result.First(e => e.EntityId == "EMPIRE_STORMTROOPER_SQUAD");
        unit.DisplayName.Should().Be("Empire Stormtrooper Squad");
        unit.Kind.Should().Be(RosterEntityKind.Unit);
        unit.Faction.Should().Be("Empire");

        var building = result.First(e => e.EntityId == "REBEL_LIGHT_FACTORY");
        building.Kind.Should().Be(RosterEntityKind.Building);
        building.Faction.Should().Be("Rebel");
    }

    [Fact]
    public async Task LoadRosterAsync_WithEmptyCatalog_ReturnsEmptyList()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var service = CreateService(catalog);

        var result = await service.LoadRosterAsync("test-profile", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadRosterAsync_SkipsNullAndEmptyEntityIds()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = new[] { "VALID_UNIT", "", "  ", null! }
        };

        var service = CreateService(catalog);

        var result = await service.LoadRosterAsync("test-profile", CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].EntityId.Should().Be("VALID_UNIT");
    }

    [Fact]
    public async Task LoadRosterAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var act = async () => await service.LoadRosterAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public void Constructor_NullCatalog_ThrowsArgumentNullException()
    {
        var act = () => new RosterBrowserService(null!, NullLogger<RosterBrowserService>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("catalog");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new RosterBrowserService(new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>()), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Theory]
    [InlineData("hero_catalog", RosterEntityKind.Hero)]
    [InlineData("HERO_CATALOG", RosterEntityKind.Hero)]
    [InlineData("HEROES", RosterEntityKind.Hero)]
    [InlineData("HERO", RosterEntityKind.Hero)]
    [InlineData("building_catalog", RosterEntityKind.Building)]
    [InlineData("BUILDING_CATALOG", RosterEntityKind.Building)]
    [InlineData("BUILDINGS", RosterEntityKind.Building)]
    [InlineData("STRUCTURES", RosterEntityKind.Building)]
    [InlineData("STRUCTURE", RosterEntityKind.Building)]
    [InlineData("unit_catalog", RosterEntityKind.Unit)]
    [InlineData("UNIT_CATALOG", RosterEntityKind.Unit)]
    [InlineData("UNITS", RosterEntityKind.Unit)]
    [InlineData("planet_catalog", RosterEntityKind.Unknown)]
    [InlineData("faction_catalog", RosterEntityKind.Unknown)]
    [InlineData("entity_catalog", RosterEntityKind.Unknown)]
    [InlineData("something_else", RosterEntityKind.Unknown)]
    public void ClassifyCategory_MapsCorrectly(string category, RosterEntityKind expected)
    {
        RosterBrowserService.ClassifyCategory(category).Should().Be(expected);
    }

    [Fact]
    public void ClassifyCategory_NullCategory_ThrowsArgumentNullException()
    {
        var act = () => RosterBrowserService.ClassifyCategory(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("IMPERIAL_STAR_DESTROYER", "Imperial Star Destroyer")]
    [InlineData("EMPIRE_STORMTROOPER_SQUAD", "Empire Stormtrooper Squad")]
    [InlineData("HERO_VADER", "Hero Vader")]
    [InlineData("X_WING", "X Wing")]
    [InlineData("A", "A")]
    [InlineData("", "")]
    public void FormatDisplayName_ConvertsCorrectly(string entityId, string expected)
    {
        RosterBrowserService.FormatDisplayName(entityId).Should().Be(expected);
    }

    [Fact]
    public void FormatDisplayName_NullEntityId_ThrowsArgumentNullException()
    {
        var act = () => RosterBrowserService.FormatDisplayName(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("EMPIRE_ISD", "Empire")]
    [InlineData("IMPERIAL_STAR_DESTROYER", "Empire")]
    [InlineData("REBEL_TROOPER", "Rebel")]
    [InlineData("UNDERWORLD_TYBER_ZANN", "Underworld")]
    [InlineData("REPUBLIC_CRUISER", "Republic")]
    [InlineData("CIS_DROID_ARMY", "CIS")]
    [InlineData("RANDOM_UNIT", "Unknown")]
    [InlineData("HERO_VADER", "Unknown")]
    public void InferFaction_ExtractsCorrectly(string entityId, string expected)
    {
        RosterBrowserService.InferFaction(entityId).Should().Be(expected);
    }

    [Fact]
    public void InferFaction_NullEntityId_ThrowsArgumentNullException()
    {
        var act = () => RosterBrowserService.InferFaction(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task LoadRosterAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = new[] { "EMPIRE_AT_AT" }
        };

        IRosterBrowserService service = CreateService(catalog);

        var result = await service.LoadRosterAsync("test-profile");

        result.Should().HaveCount(1);
        result[0].EntityId.Should().Be("EMPIRE_AT_AT");
    }

    // --- InferFaction: empty string branch ---

    [Fact]
    public void InferFaction_EmptyString_ReturnsUnknown()
    {
        RosterBrowserService.InferFaction(string.Empty).Should().Be("Unknown");
    }

    // --- ClassifyCategory: additional keyword coverage ---

    [Theory]
    [InlineData("UNIT", RosterEntityKind.Unit)]
    [InlineData("BUILDING", RosterEntityKind.Building)]
    public void ClassifyCategory_AdditionalKeywords_MapCorrectly(
        string category, RosterEntityKind expected)
    {
        RosterBrowserService.ClassifyCategory(category).Should().Be(expected);
    }

    // --- FormatDisplayName: single-character word in middle ---

    [Fact]
    public void FormatDisplayName_SingleCharWords_FormatsCorrectly()
    {
        RosterBrowserService.FormatDisplayName("A_B_C").Should().Be("A B C");
    }

    // --- LoadRosterAsync with CIS and Republic faction entities ---

    [Fact]
    public async Task LoadRosterAsync_CisPrefixEntity_InfersCisFaction()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = new[] { "CIS_DROID_ARMY" }
        };

        var service = CreateService(catalog);

        var result = await service.LoadRosterAsync("test-profile", CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Faction.Should().Be("CIS");
    }

    [Fact]
    public async Task LoadRosterAsync_RepublicPrefixEntity_InfersRepublicFaction()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = new[] { "REPUBLIC_CRUISER" }
        };

        var service = CreateService(catalog);

        var result = await service.LoadRosterAsync("test-profile", CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Faction.Should().Be("Republic");
    }

    [Fact]
    public async Task LoadRosterAsync_UnderworldPrefixEntity_InfersUnderworldFaction()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = new[] { "UNDERWORLD_DEFILER" }
        };

        var service = CreateService(catalog);

        var result = await service.LoadRosterAsync("test-profile", CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Faction.Should().Be("Underworld");
    }

    private static RosterBrowserService CreateService(
        IDictionary<string, IReadOnlyList<string>> catalog)
    {
        return new RosterBrowserService(
            new StubCatalogService(catalog),
            NullLogger<RosterBrowserService>.Instance);
    }

    private sealed class StubCatalogService : ICatalogService
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _catalog;

        public StubCatalogService(IDictionary<string, IReadOnlyList<string>> catalog)
        {
            _catalog = new Dictionary<string, IReadOnlyList<string>>(catalog, StringComparer.OrdinalIgnoreCase);
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(
            string profileId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_catalog);
        }
    }
}
