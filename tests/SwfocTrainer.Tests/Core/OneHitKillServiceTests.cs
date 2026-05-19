using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class OneHitKillServiceTests
{
    private static readonly ILogger<OneHitKillService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<OneHitKillService>();

    [Fact]
    public void BuildOneHitKillLuaCommand_Enable_ReturnsOneArg()
    {
        OneHitKillService.BuildOneHitKillLuaCommand(true)
            .Should().Be("return SWFOC_OneHitKill(1)");
    }

    [Fact]
    public void BuildOneHitKillLuaCommand_Disable_ReturnsZeroArg()
    {
        OneHitKillService.BuildOneHitKillLuaCommand(false)
            .Should().Be("return SWFOC_OneHitKill(0)");
    }

    [Fact]
    public void Constructor_NullBridgeOverload_Accepted()
    {
        var act = () => new OneHitKillService(NullLogger);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task SetOneHitKillAsync_Offline_ReturnsSuccessWithLuaCall()
    {
        var service = new OneHitKillService(NullLogger);

        var result = await service.SetOneHitKillAsync("p1", true, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.AddressSource.Should().Be(AddressSource.None);
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("return SWFOC_OneHitKill(1)");
        result.Diagnostics.Should().ContainKey("enable")
            .WhoseValue.Should().Be(true);
    }

    [Fact]
    public async Task SetOneHitKillAsync_Offline_DisableReturnsExpectedLua()
    {
        var service = new OneHitKillService(NullLogger);

        var result = await service.SetOneHitKillAsync("p1", false, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics!["lua_call"].Should().Be("return SWFOC_OneHitKill(0)");
    }

    [Fact]
    public async Task SetOneHitKillAsync_NullProfileId_Throws()
    {
        var service = new OneHitKillService(NullLogger);

        var act = () => service.SetOneHitKillAsync(null!, true, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new OneHitKillService(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
