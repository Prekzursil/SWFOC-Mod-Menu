using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class PlanetManagerServiceTests
{
    [Fact]
    public async Task LoadPlanetsAsync_WithPlanetCatalog_ReturnsPlanetInfoList()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["planet_catalog"] = new[] { "CORUSCANT", "TATOOINE", "HOTH" }
        };

        var service = CreateService(catalog);

        var result = await service.LoadPlanetsAsync("test_profile", CancellationToken.None);

        result.Should().HaveCount(3);

        var coruscant = result.First(p => p.PlanetId == "CORUSCANT");
        coruscant.DisplayName.Should().Be("Coruscant");
        coruscant.OwnerFaction.Should().Be("Unknown");
        coruscant.SpaceStationLevel.Should().Be(0);
        coruscant.Buildings.Should().BeEmpty();
        coruscant.CorruptionLevel.Should().Be(0);
        coruscant.CorruptionKind.Should().Be(CorruptionType.None);
    }

    [Fact]
    public async Task LoadPlanetsAsync_WithPlanetsKey_ReturnsPlanetInfoList()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["planets"] = new[] { "KUAT", "MON_CALAMARI" }
        };

        var service = CreateService(catalog);

        var result = await service.LoadPlanetsAsync("test_profile", CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].PlanetId.Should().Be("KUAT");
        result[1].PlanetId.Should().Be("MON_CALAMARI");
        result[1].DisplayName.Should().Be("Mon Calamari");
    }

    [Fact]
    public async Task LoadPlanetsAsync_WithNoPlanets_ReturnsEmptyList()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = new[] { "AT_AT" }
        };

        var service = CreateService(catalog);

        var result = await service.LoadPlanetsAsync("test_profile", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadPlanetsAsync_SkipsNullAndEmptyPlanetIds()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["planet_catalog"] = new[] { "CORUSCANT", "", "  ", null! }
        };

        var service = CreateService(catalog);

        var result = await service.LoadPlanetsAsync("test_profile", CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].PlanetId.Should().Be("CORUSCANT");
    }

    [Fact]
    public async Task LoadPlanetsAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var act = async () => await service.LoadPlanetsAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public async Task SetPlanetOwnerAsync_ValidParams_ReturnsSucceededResult()
    {
        var service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var result = await service.SetPlanetOwnerAsync(
            "test_profile", "CORUSCANT", "REBEL", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("CORUSCANT");
        result.Message.Should().Contain("REBEL");
        result.AddressSource.Should().Be(AddressSource.None);
    }

    [Fact]
    public async Task SetPlanetOwnerAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var act = () => service.SetPlanetOwnerAsync(null!, "CORUSCANT", "REBEL", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public async Task SetPlanetOwnerAsync_NullPlanetId_ThrowsArgumentNullException()
    {
        var service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var act = () => service.SetPlanetOwnerAsync("test_profile", null!, "REBEL", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("planetId");
    }

    [Fact]
    public async Task SetPlanetOwnerAsync_NullNewOwner_ThrowsArgumentNullException()
    {
        var service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var act = () => service.SetPlanetOwnerAsync("test_profile", "CORUSCANT", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("newOwner");
    }

    [Fact]
    public void Constructor_NullCatalog_ThrowsArgumentNullException()
    {
        var act = () => new PlanetManagerService(null!, NullLogger<PlanetManagerService>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("catalog");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new PlanetManagerService(
            new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>()), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Theory]
    [InlineData("CORUSCANT", "Coruscant")]
    [InlineData("MON_CALAMARI", "Mon Calamari")]
    [InlineData("KUAT_DRIVE_YARDS", "Kuat Drive Yards")]
    [InlineData("A", "A")]
    [InlineData("", "")]
    public void FormatDisplayName_ConvertsCorrectly(string planetId, string expected)
    {
        PlanetManagerService.FormatDisplayName(planetId).Should().Be(expected);
    }

    [Fact]
    public void FormatDisplayName_NullPlanetId_ThrowsArgumentNullException()
    {
        var act = () => PlanetManagerService.FormatDisplayName(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task LoadPlanetsAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["planet_catalog"] = new[] { "TATOOINE" }
        };

        IPlanetManagerService service = CreateService(catalog);

        var result = await service.LoadPlanetsAsync("test_profile");

        result.Should().HaveCount(1);
        result[0].PlanetId.Should().Be("TATOOINE");
    }

    [Fact]
    public async Task SetPlanetOwnerAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        IPlanetManagerService service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var result = await service.SetPlanetOwnerAsync("test_profile", "HOTH", "EMPIRE");

        result.Succeeded.Should().BeTrue();
    }

    private static PlanetManagerService CreateService(
        IDictionary<string, IReadOnlyList<string>> catalog)
    {
        return new PlanetManagerService(
            new StubCatalogService(catalog),
            NullLogger<PlanetManagerService>.Instance);
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
