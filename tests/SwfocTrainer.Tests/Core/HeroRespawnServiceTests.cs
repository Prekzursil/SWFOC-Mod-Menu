using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class HeroRespawnServiceTests
{
    private static readonly ILogger<HeroRespawnService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<HeroRespawnService>();

    // --- BuildSetCustomRespawnLuaCommand ---

    [Fact]
    public void BuildSetCustomRespawnLuaCommand_WholeNumber_ReturnsExpectedLua()
    {
        HeroRespawnService.BuildSetCustomRespawnLuaCommand(120)
            .Should().Be("return SWFOC_SetHeroRespawn(120)");
    }

    [Fact]
    public void BuildSetCustomRespawnLuaCommand_Fractional_ReturnsExpectedLua()
    {
        HeroRespawnService.BuildSetCustomRespawnLuaCommand(15.5)
            .Should().Be("return SWFOC_SetHeroRespawn(15.5)");
    }

    [Fact]
    public void BuildSetCustomRespawnLuaCommand_Zero_ReturnsExpectedLua()
    {
        HeroRespawnService.BuildSetCustomRespawnLuaCommand(0)
            .Should().Be("return SWFOC_SetHeroRespawn(0)");
    }

    // --- BuildSetInstantRespawnLuaCommand ---

    [Fact]
    public void BuildSetInstantRespawnLuaCommand_Enable_ReturnsOneArg()
    {
        HeroRespawnService.BuildSetInstantRespawnLuaCommand(true)
            .Should().Be("return SWFOC_HeroInstantRespawn(1)");
    }

    [Fact]
    public void BuildSetInstantRespawnLuaCommand_Disable_ReturnsZeroArg()
    {
        HeroRespawnService.BuildSetInstantRespawnLuaCommand(false)
            .Should().Be("return SWFOC_HeroInstantRespawn(0)");
    }

    // --- Constructor / offline mode ---

    [Fact]
    public void Constructor_NullBridgeOverload_Accepted()
    {
        var act = () => new HeroRespawnService(NullLogger);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task SetCustomRespawnAsync_Offline_ReturnsSuccess()
    {
        var service = new HeroRespawnService(NullLogger);

        var result = await service.SetCustomRespawnAsync("p1", 90, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.AddressSource.Should().Be(AddressSource.None);
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("return SWFOC_SetHeroRespawn(90)");
    }

    [Fact]
    public async Task SetCustomRespawnAsync_Negative_ReturnsValidationFailure()
    {
        var service = new HeroRespawnService(NullLogger);

        var result = await service.SetCustomRespawnAsync("p1", -1, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("non-negative");
    }

    [Fact]
    public async Task SetCustomRespawnAsync_NaN_ReturnsValidationFailure()
    {
        var service = new HeroRespawnService(NullLogger);

        var result = await service.SetCustomRespawnAsync("p1", double.NaN, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task SetInstantRespawnAsync_Offline_ReturnsSuccess()
    {
        var service = new HeroRespawnService(NullLogger);

        var result = await service.SetInstantRespawnAsync("p1", true, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics!["lua_call"].Should().Be("return SWFOC_HeroInstantRespawn(1)");
    }

    [Fact]
    public async Task SetCustomRespawnAsync_NullProfileId_Throws()
    {
        var service = new HeroRespawnService(NullLogger);

        var act = () => service.SetCustomRespawnAsync(null!, 30, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new HeroRespawnService(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
