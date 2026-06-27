using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class HardpointServiceTests
{
    private static readonly ILogger<HardpointService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<HardpointService>();

    // --- BuildGetHardpointsLuaCommand ---

    [Fact]
    public void BuildGetHardpointsLuaCommand_DecimalAddress_ReturnsExpectedLua()
    {
        HardpointService.BuildGetHardpointsLuaCommand(0x100L)
            .Should().Be("return SWFOC_GetHardpoints(256)");
    }

    [Fact]
    public void BuildGetHardpointsLuaCommand_ZeroAddress_StillEmitsCall()
    {
        HardpointService.BuildGetHardpointsLuaCommand(0L)
            .Should().Be("return SWFOC_GetHardpoints(0)");
    }

    // --- ParseHardpointResult ---

    [Fact]
    public void ParseHardpointResult_ThreeHardpoints_ParsesAllEntries()
    {
        var raw = "count=3 child0=0x1234 hp0=600 child1=0x5678 hp1=450 child2=0x9abc hp2=0";

        var parsed = HardpointService.ParseHardpointResult(raw);

        parsed.Count.Should().Be(3);
        parsed.Entries.Should().HaveCount(3);
        parsed.Entries[0].Address.Should().Be(0x1234L);
        parsed.Entries[0].Hp.Should().Be(600f);
        parsed.Entries[1].Address.Should().Be(0x5678L);
        parsed.Entries[1].Hp.Should().Be(450f);
        parsed.Entries[2].Address.Should().Be(0x9abcL);
        parsed.Entries[2].Hp.Should().Be(0f);
    }

    [Fact]
    public void ParseHardpointResult_NullOrEmpty_ReturnsZeroCount()
    {
        var parsed = HardpointService.ParseHardpointResult(null);

        parsed.Count.Should().Be(0);
        parsed.Entries.Should().BeEmpty();
    }

    [Fact]
    public void ParseHardpointResult_CountZero_ReturnsEmptyEntries()
    {
        var parsed = HardpointService.ParseHardpointResult("count=0");

        parsed.Count.Should().Be(0);
        parsed.Entries.Should().BeEmpty();
    }

    [Fact]
    public void ParseHardpointResult_FractionalHp_ParsesAsFloat()
    {
        var parsed = HardpointService.ParseHardpointResult("count=1 child0=0x1 hp0=42.5");

        parsed.Count.Should().Be(1);
        parsed.Entries.Should().HaveCount(1);
        parsed.Entries[0].Hp.Should().Be(42.5f);
    }

    [Fact]
    public void ParseHardpointResult_DecimalChildAddress_AlsoSupported()
    {
        var parsed = HardpointService.ParseHardpointResult("count=1 child0=4096 hp0=100");

        parsed.Entries.Should().ContainSingle()
            .Which.Address.Should().Be(4096L);
    }

    [Fact]
    public void ParseHardpointResult_MissingHp_StopsAtBoundary()
    {
        // count=2 but only one full pair is provided. Parser must stop at the
        // missing field rather than throw.
        var parsed = HardpointService.ParseHardpointResult("count=2 child0=0x1 hp0=10 child1=0x2");

        parsed.Count.Should().Be(2);
        parsed.Entries.Should().HaveCount(1);
    }

    // --- Constructor / offline mode ---

    [Fact]
    public void Constructor_NullBridgeOverload_Accepted()
    {
        var act = () => new HardpointService(NullLogger);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task GetHardpointsAsync_Offline_ReturnsSuccess()
    {
        var service = new HardpointService(NullLogger);

        var result = await service.GetHardpointsAsync("p1", 0x10L, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.AddressSource.Should().Be(AddressSource.None);
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("return SWFOC_GetHardpoints(16)");
    }

    [Fact]
    public async Task GetHardpointsAsync_NullProfileId_Throws()
    {
        var service = new HardpointService(NullLogger);

        var act = () => service.GetHardpointsAsync(null!, 0L, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new HardpointService(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
