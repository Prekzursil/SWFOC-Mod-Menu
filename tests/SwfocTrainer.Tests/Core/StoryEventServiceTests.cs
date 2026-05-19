using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class StoryEventServiceTests
{
    // --- LoadEventsAsync ---

    [Fact]
    public async Task LoadEventsAsync_WithStoryEventCatalog_ReturnsEntries()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["story_event_catalog"] = new[] { "DEATH_STAR_DESTROYED", "ENDOR_SHIELD_DOWN" }
        };

        var service = CreateService(catalog);

        var result = await service.LoadEventsAsync("p1", CancellationToken.None);

        result.Should().HaveCount(2);

        var first = result[0];
        first.EventId.Should().Be("DEATH_STAR_DESTROYED");
        first.DisplayName.Should().Be("Death Star Destroyed");
        first.Source.Should().Be("catalog");
        first.Category.Should().Be("story");
    }

    [Fact]
    public async Task LoadEventsAsync_WithStoryEventsAltKey_ReturnsEntries()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["story_events"] = new[] { "REBEL_FLEET_ARRIVES" }
        };

        var service = CreateService(catalog);

        var result = await service.LoadEventsAsync("p1", CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].EventId.Should().Be("REBEL_FLEET_ARRIVES");
        result[0].DisplayName.Should().Be("Rebel Fleet Arrives");
    }

    [Fact]
    public async Task LoadEventsAsync_NoCatalogKey_ReturnsEmpty()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["planet_catalog"] = new[] { "CORUSCANT" }
        };

        var service = CreateService(catalog);

        var result = await service.LoadEventsAsync("p1", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadEventsAsync_SkipsNullAndEmptyEventIds()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["story_event_catalog"] = new[] { "DEATH_STAR_DESTROYED", "", "  ", null! }
        };

        var service = CreateService(catalog);

        var result = await service.LoadEventsAsync("p1", CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].EventId.Should().Be("DEATH_STAR_DESTROYED");
    }

    [Fact]
    public async Task LoadEventsAsync_EmptyCatalog_ReturnsEmpty()
    {
        var service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var result = await service.LoadEventsAsync("p1", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadEventsAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var act = async () => await service.LoadEventsAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    // --- FireEventAsync ---

    [Fact]
    public async Task FireEventAsync_ValidEventId_ReturnsSuccessWithLuaCall()
    {
        var service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var result = await service.FireEventAsync(
            "p1", "DEATH_STAR_DESTROYED", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("DEATH_STAR_DESTROYED");
        result.AddressSource.Should().Be(AddressSource.None);
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("Story_Event(\"DEATH_STAR_DESTROYED\")");
        result.Diagnostics.Should().ContainKey("event_id")
            .WhoseValue.Should().Be("DEATH_STAR_DESTROYED");
    }

    [Fact]
    public async Task FireEventAsync_EmptyEventId_ReturnsFailure()
    {
        var service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var result = await service.FireEventAsync(
            "p1", "", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("empty");
    }

    [Fact]
    public async Task FireEventAsync_WhitespaceEventId_ReturnsFailure()
    {
        var service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var result = await service.FireEventAsync(
            "p1", "   ", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task FireEventAsync_NullEventId_ThrowsArgumentNullException()
    {
        var service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var act = () => service.FireEventAsync("p1", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("eventId");
    }

    [Fact]
    public async Task FireEventAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = CreateService(new Dictionary<string, IReadOnlyList<string>>());

        var act = () => service.FireEventAsync(null!, "EVT", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    // --- FormatDisplayName ---

    [Theory]
    [InlineData("DEATH_STAR_DESTROYED", "Death Star Destroyed")]
    [InlineData("ENDOR_SHIELD_DOWN", "Endor Shield Down")]
    [InlineData("A", "A")]
    [InlineData("", "")]
    public void FormatDisplayName_ConvertsCorrectly(string eventId, string expected)
    {
        StoryEventService.FormatDisplayName(eventId).Should().Be(expected);
    }

    [Fact]
    public void FormatDisplayName_NullEventId_ThrowsArgumentNullException()
    {
        var act = () => StoryEventService.FormatDisplayName(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // --- Constructor null guards ---

    [Fact]
    public void Constructor_NullCatalog_ThrowsArgumentNullException()
    {
        var act = () => new StoryEventService(
            null!, NullLogger<StoryEventService>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("catalog");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new StoryEventService(
            new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>()), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // --- Default overloads ---

    [Fact]
    public async Task LoadEventsAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["story_event_catalog"] = new[] { "EVT_1" }
        };

        IStoryEventService service = CreateService(catalog);

        var result = await service.LoadEventsAsync("p1");

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task FireEventAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        IStoryEventService service = CreateService(
            new Dictionary<string, IReadOnlyList<string>>());

        var result = await service.FireEventAsync("p1", "EVT_1");

        result.Succeeded.Should().BeTrue();
    }

    // --- Helpers ---

    private static StoryEventService CreateService(
        IDictionary<string, IReadOnlyList<string>> catalog)
    {
        return new StoryEventService(
            new StubCatalogService(catalog),
            NullLogger<StoryEventService>.Instance);
    }

    private sealed class StubCatalogService : ICatalogService
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _catalog;

        public StubCatalogService(IDictionary<string, IReadOnlyList<string>> catalog)
        {
            _catalog = new Dictionary<string, IReadOnlyList<string>>(
                catalog, StringComparer.OrdinalIgnoreCase);
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(
            string profileId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_catalog);
        }
    }
}
