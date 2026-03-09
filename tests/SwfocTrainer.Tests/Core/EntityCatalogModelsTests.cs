using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class EntityCatalogModelsTests
{
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
