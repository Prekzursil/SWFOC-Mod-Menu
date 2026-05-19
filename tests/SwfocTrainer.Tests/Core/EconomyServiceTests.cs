using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class EconomyServiceTests
{
    private static readonly ILogger<EconomyService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<EconomyService>();

    // --- BuildSetCreditsLuaCommand ---

    [Fact]
    public void BuildSetCreditsLuaCommand_Slot0_PositiveAmount_UsesSlotHelper()
    {
        EconomyService.BuildSetCreditsLuaCommand(0, 1500)
            .Should().Be("return SWFOC_SetCreditsForSlot(0, 1500)");
    }

    [Fact]
    public void BuildSetCreditsLuaCommand_NegativeSlot_RoutesToLocalPlayerHelper()
    {
        EconomyService.BuildSetCreditsLuaCommand(-1, 9999)
            .Should().Be("return SWFOC_SetCredits(9999)");
    }

    [Fact]
    public void BuildSetCreditsLuaCommand_FractionalAmount_RoundtripsAsInvariant()
    {
        var lua = EconomyService.BuildSetCreditsLuaCommand(2, 1234.5);
        lua.Should().Be("return SWFOC_SetCreditsForSlot(2, 1234.5)");
    }

    // --- BuildGetCreditsLuaCommand ---

    [Fact]
    public void BuildGetCreditsLuaCommand_Slot1_UsesSlotHelper()
    {
        EconomyService.BuildGetCreditsLuaCommand(1)
            .Should().Be("return SWFOC_GetCreditsForSlot(1)");
    }

    [Fact]
    public void BuildGetCreditsLuaCommand_NegativeSlot_UsesLocalPlayer()
    {
        EconomyService.BuildGetCreditsLuaCommand(-1)
            .Should().Be("return SWFOC_GetCredits()");
    }

    // --- BuildDrainEnemyCreditsLuaCommand ---

    [Fact]
    public void BuildDrainEnemyCreditsLuaCommand_NoArgs_ReturnsExpectedLua()
    {
        EconomyService.BuildDrainEnemyCreditsLuaCommand()
            .Should().Be("return SWFOC_DrainEnemyCredits()");
    }

    // --- BuildUncapCreditsLuaCommand ---

    [Fact]
    public void BuildUncapCreditsLuaCommand_NoArgs_ReturnsExpectedLua()
    {
        EconomyService.BuildUncapCreditsLuaCommand()
            .Should().Be("return SWFOC_UncapCredits()");
    }

    // --- BuildGetMaxCreditsLuaCommand ---

    [Fact]
    public void BuildGetMaxCreditsLuaCommand_NoArgs_ReturnsExpectedLua()
    {
        EconomyService.BuildGetMaxCreditsLuaCommand()
            .Should().Be("return SWFOC_GetMaxCredits()");
    }

    // --- BuildSetTechLuaCommand ---

    [Fact]
    public void BuildSetTechLuaCommand_PositiveSlot_UsesSlotHelper()
    {
        EconomyService.BuildSetTechLuaCommand(2, 5)
            .Should().Be("return SWFOC_SetTechForSlot(2, 5)");
    }

    [Fact]
    public void BuildSetTechLuaCommand_NegativeSlot_UsesLocalPlayerHelper()
    {
        EconomyService.BuildSetTechLuaCommand(-1, 3)
            .Should().Be("return SWFOC_SetTechLevel(3)");
    }

    // --- BuildGetTechLuaCommand ---

    [Fact]
    public void BuildGetTechLuaCommand_PositiveSlot_UsesSlotHelper()
    {
        EconomyService.BuildGetTechLuaCommand(2)
            .Should().Be("return SWFOC_GetTechForSlot(2)");
    }

    [Fact]
    public void BuildGetTechLuaCommand_NegativeSlot_UsesNegativeOne()
    {
        EconomyService.BuildGetTechLuaCommand(-1)
            .Should().Be("return SWFOC_GetTechForSlot(-1)");
    }

    // --- Constructor / offline mode ---

    [Fact]
    public void Constructor_NullBridgeOverload_Accepted()
    {
        var act = () => new EconomyService(NullLogger);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task SetCreditsAsync_Offline_ReturnsSuccess()
    {
        var service = new EconomyService(NullLogger);

        var result = await service.SetCreditsAsync("p1", 0, 5000, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.AddressSource.Should().Be(AddressSource.None);
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("return SWFOC_SetCreditsForSlot(0, 5000)");
    }

    [Fact]
    public async Task GetCreditsAsync_Offline_ReturnsSuccess()
    {
        var service = new EconomyService(NullLogger);

        var result = await service.GetCreditsAsync("p1", 1, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics!["lua_call"].Should().Be("return SWFOC_GetCreditsForSlot(1)");
    }

    [Fact]
    public async Task DrainEnemyCreditsAsync_Offline_ReturnsSuccess()
    {
        var service = new EconomyService(NullLogger);

        var result = await service.DrainEnemyCreditsAsync("p1", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics!["lua_call"].Should().Be("return SWFOC_DrainEnemyCredits()");
    }

    [Fact]
    public async Task UncapCreditsAsync_Offline_ReturnsSuccess()
    {
        var service = new EconomyService(NullLogger);

        var result = await service.UncapCreditsAsync("p1", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics!["lua_call"].Should().Be("return SWFOC_UncapCredits()");
    }

    [Fact]
    public async Task GetMaxCreditsAsync_Offline_ReturnsSuccess()
    {
        var service = new EconomyService(NullLogger);

        var result = await service.GetMaxCreditsAsync("p1", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics!["lua_call"].Should().Be("return SWFOC_GetMaxCredits()");
    }

    [Fact]
    public async Task SetTechAsync_Offline_ReturnsSuccess()
    {
        var service = new EconomyService(NullLogger);

        var result = await service.SetTechAsync("p1", 0, 5, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics!["lua_call"].Should().Be("return SWFOC_SetTechForSlot(0, 5)");
    }

    [Fact]
    public async Task GetTechAsync_Offline_ReturnsSuccess()
    {
        var service = new EconomyService(NullLogger);

        var result = await service.GetTechAsync("p1", 0, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics!["lua_call"].Should().Be("return SWFOC_GetTechForSlot(0)");
    }

    [Fact]
    public async Task SetCreditsAsync_NullProfileId_Throws()
    {
        var service = new EconomyService(NullLogger);

        var act = () => service.SetCreditsAsync(null!, 0, 100, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new EconomyService(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
