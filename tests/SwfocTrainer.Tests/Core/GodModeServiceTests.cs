using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class GodModeServiceTests
{
    private static readonly ILogger<GodModeService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<GodModeService>();

    [Fact]
    public void BuildGodModeLuaCommand_Enable_ReturnsOneArg()
    {
        GodModeService.BuildGodModeLuaCommand(true)
            .Should().Be("return SWFOC_GodMode(1)");
    }

    [Fact]
    public void BuildGodModeLuaCommand_Disable_ReturnsZeroArg()
    {
        GodModeService.BuildGodModeLuaCommand(false)
            .Should().Be("return SWFOC_GodMode(0)");
    }

    [Fact]
    public void Constructor_NullBridgeOverload_Accepted()
    {
        var act = () => new GodModeService(NullLogger);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task SetGodModeAsync_Offline_ReturnsSuccessWithLuaCall()
    {
        var service = new GodModeService(NullLogger);

        var result = await service.SetGodModeAsync("p1", true, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.AddressSource.Should().Be(AddressSource.None);
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("return SWFOC_GodMode(1)");
        result.Diagnostics.Should().ContainKey("enable")
            .WhoseValue.Should().Be(true);
    }

    [Fact]
    public async Task SetGodModeAsync_Offline_DisableReturnsExpectedLua()
    {
        var service = new GodModeService(NullLogger);

        var result = await service.SetGodModeAsync("p1", false, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics!["lua_call"].Should().Be("return SWFOC_GodMode(0)");
    }

    [Fact]
    public async Task SetGodModeAsync_NullProfileId_Throws()
    {
        var service = new GodModeService(NullLogger);

        var act = () => service.SetGodModeAsync(null!, true, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new GodModeService(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
