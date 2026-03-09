using FluentAssertions;
using System.Reflection;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class EntityCatalogModelsTests
{
    [Fact]
    public void EntityCatalogRecord_DefaultConstructor_ShouldInitializeNonNullCollections()
    {
        var record = new EntityCatalogRecord();

        record.EntityId.Should().BeEmpty();
        record.DisplayNameKey.Should().BeEmpty();
        record.DisplayName.Should().BeEmpty();
        record.SourceProfileId.Should().BeEmpty();
        record.Affiliations.Should().BeEmpty();
        record.DependencyRefs.Should().BeEmpty();
        record.Metadata.Should().BeEmpty();
        record.DefaultAffiliation.Should().BeEmpty();
    }

    [Fact]
    public async Task CatalogServiceDefaultMethods_ShouldUseNoneCancellation_AndProjectLegacyTypedSnapshot()
    {
        var service = new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = ["EMPIRE_STORMTROOPER_SQUAD"],
            ["building_catalog"] = ["EMPIRE_BARRACKS"],
            ["hero_catalog"] = ["HERO_VADER"],
            ["planet_catalog"] = ["PLANET_CORUSCANT"],
            ["faction_catalog"] = ["EMPIRE"],
            ["entity_catalog"] = ["Building|EMPIRE_BARRACKS", "Hero|HERO_VADER"]
        });

        var legacy = await ((ICatalogService)service).LoadCatalogAsync("profile-1");
        var snapshot = await ((ICatalogService)service).LoadTypedCatalogAsync("profile-1");

        service.LoadCatalogCallCount.Should().Be(2);
        service.LastProfileId.Should().Be("profile-1");
        service.LastCancellationToken.Should().Be(CancellationToken.None);
        legacy["unit_catalog"].Should().Contain("EMPIRE_STORMTROOPER_SQUAD");
        snapshot.Entities.Should().Contain(record => record.EntityId == "EMPIRE_BARRACKS" && record.Kind == CatalogEntityKind.Building);
        snapshot.Entities.Should().Contain(record => record.EntityId == "HERO_VADER" && record.Kind == CatalogEntityKind.Hero);
    }

    [Fact]
    public void FromLegacy_ShouldIgnoreMalformedEntries_AndMergeSpecificKinds()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = ["EMPIRE_STORMTROOPER_SQUAD", "EMPIRE_BARRACKS"],
            ["building_catalog"] = ["EMPIRE_BARRACKS"],
            ["faction_catalog"] = ["EMPIRE"],
            ["entity_catalog"] = ["invalid", "Unit|EMPIRE_STORMTROOPER_SQUAD", "Building|EMPIRE_BARRACKS"]
        };

        var snapshot = EntityCatalogSnapshot.FromLegacy("legacy-profile", catalog);

        snapshot.Entities.Should().ContainSingle(record => record.EntityId == "EMPIRE_STORMTROOPER_SQUAD")
            .Which.Kind.Should().Be(CatalogEntityKind.Unit);
        snapshot.Entities.Should().ContainSingle(record => record.EntityId == "EMPIRE_BARRACKS")
            .Which.Kind.Should().Be(CatalogEntityKind.Building);
        snapshot.Entities.Should().ContainSingle(record => record.EntityId == "EMPIRE")
            .Which.DefaultAffiliation.Should().Be("EMPIRE");
    }

    [Theory]
    [InlineData("Faction", "EMPIRE", CatalogEntityKind.Faction)]
    [InlineData("LandUnit", "EMPIRE_STORMTROOPER_SQUAD", CatalogEntityKind.Unit)]
    [InlineData("Structure", "EMPIRE_BARRACKS", CatalogEntityKind.Building)]
    [InlineData("SpaceStructure", "EMPIRE_STAR_BASE", CatalogEntityKind.SpaceStructure)]
    [InlineData("Hero", "HERO_VADER", CatalogEntityKind.Hero)]
    [InlineData("Planet", "PLANET_CORUSCANT", CatalogEntityKind.Planet)]
    [InlineData("Ability", "ABILITY_ORBITAL_BOMBARDMENT", CatalogEntityKind.AbilityCarrier)]
    public void CatalogEntityKindClassifier_ShouldResolveExpectedKinds(string elementName, string entityId, CatalogEntityKind expected)
    {
        CatalogEntityKindClassifier.ResolveKind(elementName, entityId).Should().Be(expected);
    }

    [Fact]
    public void CatalogEntityKindClassifier_ShouldInferAffiliations_WithoutTreatingUnitsAsFactions()
    {
        CatalogEntityKindClassifier.InferAffiliations("EMPIRE_STORMTROOPER_SQUAD")
            .Should()
            .ContainSingle("EMPIRE");

        CatalogEntityKindClassifier.ResolveKind("LandUnit", "EMPIRE_STORMTROOPER_SQUAD")
            .Should()
            .Be(CatalogEntityKind.Unit);
    }

    [Theory]
    [InlineData("Hero", CatalogEntityKind.Hero)]
    [InlineData("Building", CatalogEntityKind.Building)]
    [InlineData("SpaceStructure", CatalogEntityKind.SpaceStructure)]
    [InlineData("AbilityCarrier", CatalogEntityKind.AbilityCarrier)]
    [InlineData("Planet", CatalogEntityKind.Planet)]
    [InlineData("Faction", CatalogEntityKind.Faction)]
    [InlineData("unknown", CatalogEntityKind.Unit)]
    public void CatalogEntityKindClassifier_ParseLegacyToken_ShouldRoundTripExpectedValues(string token, CatalogEntityKind expected)
    {
        var parsed = CatalogEntityKindClassifier.ParseLegacyToken(token);

        parsed.Should().Be(expected);
        CatalogEntityKindClassifier.ToLegacyToken(parsed).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CatalogEntityKindClassifier_SelectMoreSpecificKind_ShouldPreferHigherSpecificity()
    {
        CatalogEntityKindClassifier.SelectMoreSpecificKind(CatalogEntityKind.Unit, CatalogEntityKind.Building)
            .Should()
            .Be(CatalogEntityKind.Building);

        CatalogEntityKindClassifier.SelectMoreSpecificKind(CatalogEntityKind.Building, CatalogEntityKind.Unit)
            .Should()
            .Be(CatalogEntityKind.Building);

        CatalogEntityKindClassifier.SelectMoreSpecificKind(CatalogEntityKind.Unknown, CatalogEntityKind.Faction)
            .Should()
            .Be(CatalogEntityKind.Faction);
    }

    [Fact]
    public void CatalogEntityKindClassifier_ShouldHandleWhitespaceAndFactionMarkers()
    {
        CatalogEntityKindClassifier.InferAffiliations("  ").Should().BeEmpty();
        CatalogEntityKindClassifier.ResolveKind("Faction", "FACTION_UNDERWORLD")
            .Should()
            .Be(CatalogEntityKind.Faction);
        CatalogEntityKindClassifier.ResolveKind("Structure", "REBEL_STAR_BASE")
            .Should()
            .Be(CatalogEntityKind.SpaceStructure);
    }

    [Fact]
    public void AddOrMergeRecord_ShouldMergeAffiliations_AndPreferIncomingDisplayValuesWhenExistingUsesFallback()
    {
        var records = new Dictionary<string, EntityCatalogRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["EMPIRE_BARRACKS"] = new()
            {
                EntityId = "EMPIRE_BARRACKS",
                DisplayNameKey = "EMPIRE_BARRACKS",
                DisplayName = "EMPIRE_BARRACKS",
                Kind = CatalogEntityKind.Unit,
                Affiliations = ["EMPIRE", ""]
            }
        };

        var incoming = new EntityCatalogRecord
        {
            EntityId = "EMPIRE_BARRACKS",
            DisplayNameKey = "TEXT_BARRACKS",
            DisplayName = "Imperial Barracks",
            Kind = CatalogEntityKind.Building,
            Affiliations = ["empire", "PIRATE", " "]
        };

        InvokeAddOrMergeRecord(records, incoming);

        records["EMPIRE_BARRACKS"].Kind.Should().Be(CatalogEntityKind.Building);
        records["EMPIRE_BARRACKS"].DisplayNameKey.Should().Be("TEXT_BARRACKS");
        records["EMPIRE_BARRACKS"].DisplayName.Should().Be("Imperial Barracks");
        records["EMPIRE_BARRACKS"].Affiliations.Should().Equal("EMPIRE", "PIRATE");
    }

    [Fact]
    public void AddOrMergeRecord_ShouldKeepExistingAffiliations_WhenMergedValuesAreBlank()
    {
        var records = new Dictionary<string, EntityCatalogRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["HERO_VADER"] = new()
            {
                EntityId = "HERO_VADER",
                DisplayNameKey = "TEXT_VADER",
                DisplayName = "Darth Vader",
                Kind = CatalogEntityKind.Hero,
                Affiliations = ["EMPIRE"]
            }
        };

        var incoming = new EntityCatalogRecord
        {
            EntityId = "HERO_VADER",
            DisplayNameKey = string.Empty,
            DisplayName = string.Empty,
            Kind = CatalogEntityKind.Hero,
            Affiliations = ["", " "]
        };

        InvokeAddOrMergeRecord(records, incoming);

        records["HERO_VADER"].DisplayNameKey.Should().Be("TEXT_VADER");
        records["HERO_VADER"].DisplayName.Should().Be("Darth Vader");
        records["HERO_VADER"].Affiliations.Should().Equal("EMPIRE");
    }

    private static void InvokeAddOrMergeRecord(
        IDictionary<string, EntityCatalogRecord> records,
        EntityCatalogRecord incoming)
    {
        var method = typeof(EntityCatalogSnapshot).GetMethod("AddOrMergeRecord", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        method!.Invoke(null, [records, incoming]);
    }

    private sealed class StubCatalogService : ICatalogService
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _catalog;

        public StubCatalogService(IReadOnlyDictionary<string, IReadOnlyList<string>> catalog)
        {
            _catalog = catalog;
        }

        public int LoadCatalogCallCount { get; private set; }

        public string LastProfileId { get; private set; } = string.Empty;

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken)
        {
            LoadCatalogCallCount++;
            LastProfileId = profileId;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_catalog);
        }
    }
}
