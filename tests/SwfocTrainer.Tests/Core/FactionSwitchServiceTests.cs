using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class FactionSwitchServiceTests
{
    private static readonly ILogger<FactionSwitchService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<FactionSwitchService>();

    [Fact]
    public async Task SwitchFactionAsync_ValidFaction_ReturnsSucceededResult()
    {
        var service = new FactionSwitchService(NullLogger);
        var request = new FactionSwitchRequest("REBEL");

        var result = await service.SwitchFactionAsync("test_profile", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("REBEL");
        result.AddressSource.Should().Be(AddressSource.None);
    }

    [Fact]
    public async Task SwitchFactionAsync_EmptyFaction_ReturnsFailedResult()
    {
        var service = new FactionSwitchService(NullLogger);
        var request = new FactionSwitchRequest("");

        var result = await service.SwitchFactionAsync("test_profile", request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("empty");
    }

    [Fact]
    public async Task SwitchFactionAsync_WhitespaceFaction_ReturnsFailedResult()
    {
        var service = new FactionSwitchService(NullLogger);
        var request = new FactionSwitchRequest("   ");

        var result = await service.SwitchFactionAsync("test_profile", request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task SwitchFactionAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = new FactionSwitchService(NullLogger);
        var request = new FactionSwitchRequest("EMPIRE");

        var act = () => service.SwitchFactionAsync(null!, request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public async Task SwitchFactionAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = new FactionSwitchService(NullLogger);

        var act = () => service.SwitchFactionAsync("test_profile", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("request");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new FactionSwitchService(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void FeatureId_IsSetContextAllegiance()
    {
        FactionSwitchService.FeatureId.Should().Be("set_context_allegiance");
    }

    [Fact]
    public async Task SwitchFactionAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        IFactionSwitchService service = new FactionSwitchService(NullLogger);
        var request = new FactionSwitchRequest("UNDERWORLD");

        var result = await service.SwitchFactionAsync("test_profile", request);

        result.Succeeded.Should().BeTrue();
    }
}
