using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class OwnershipTransferServiceTests
{
    private static readonly ILogger<OwnershipTransferService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<OwnershipTransferService>();

    [Fact]
    public async Task TransferOwnershipAsync_SelectedUnit_ReturnsSucceededResult()
    {
        var service = new OwnershipTransferService(NullLogger);
        var request = new OwnershipTransferRequest("unit_42", "REBEL", OwnershipTransferScope.SelectedUnit);

        var result = await service.TransferOwnershipAsync("test_profile", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("selected_unit");
        result.Message.Should().Contain("unit_42");
        result.Message.Should().Contain("REBEL");
        result.AddressSource.Should().Be(AddressSource.None);
    }

    [Fact]
    public async Task TransferOwnershipAsync_AllOfType_ReturnsSucceededResult()
    {
        var service = new OwnershipTransferService(NullLogger);
        var request = new OwnershipTransferRequest("AT_AT", "EMPIRE", OwnershipTransferScope.AllOfType);

        var result = await service.TransferOwnershipAsync("test_profile", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("all_of_type");
    }

    [Fact]
    public async Task TransferOwnershipAsync_AllVisible_ReturnsSucceededResult()
    {
        var service = new OwnershipTransferService(NullLogger);
        var request = new OwnershipTransferRequest("visible_group", "UNDERWORLD", OwnershipTransferScope.AllVisible);

        var result = await service.TransferOwnershipAsync("test_profile", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("all_visible");
    }

    [Fact]
    public async Task TransferOwnershipAsync_Planet_ReturnsSucceededResult()
    {
        var service = new OwnershipTransferService(NullLogger);
        var request = new OwnershipTransferRequest("CORUSCANT", "EMPIRE", OwnershipTransferScope.Planet);

        var result = await service.TransferOwnershipAsync("test_profile", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("planet");
        result.Message.Should().Contain("CORUSCANT");
    }

    [Fact]
    public async Task TransferOwnershipAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = new OwnershipTransferService(NullLogger);
        var request = new OwnershipTransferRequest("unit_1", "REBEL", OwnershipTransferScope.SelectedUnit);

        var act = () => service.TransferOwnershipAsync(null!, request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public async Task TransferOwnershipAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = new OwnershipTransferService(NullLogger);

        var act = () => service.TransferOwnershipAsync("test_profile", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("request");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new OwnershipTransferService(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Theory]
    [InlineData(OwnershipTransferScope.SelectedUnit, "selected_unit")]
    [InlineData(OwnershipTransferScope.AllOfType, "all_of_type")]
    [InlineData(OwnershipTransferScope.AllVisible, "all_visible")]
    [InlineData(OwnershipTransferScope.Planet, "planet")]
    public void ResolveScopeLabel_MapsCorrectly(OwnershipTransferScope scope, string expected)
    {
        OwnershipTransferService.ResolveScopeLabel(scope).Should().Be(expected);
    }

    [Fact]
    public void ResolveScopeLabel_UnknownScope_ThrowsArgumentOutOfRangeException()
    {
        var act = () => OwnershipTransferService.ResolveScopeLabel((OwnershipTransferScope)999);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("scope");
    }

    [Fact]
    public async Task TransferOwnershipAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        IOwnershipTransferService service = new OwnershipTransferService(NullLogger);
        var request = new OwnershipTransferRequest("unit_1", "REBEL", OwnershipTransferScope.SelectedUnit);

        var result = await service.TransferOwnershipAsync("test_profile", request);

        result.Succeeded.Should().BeTrue();
    }
}
