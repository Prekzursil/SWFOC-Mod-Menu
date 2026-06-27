using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class UnitInspectorServiceTests
{
    private static readonly ILogger<UnitInspectorService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<UnitInspectorService>();

    // --- BuildInspectUnitLuaCommand ---

    [Fact]
    public void BuildInspectUnitLuaCommand_DecimalAddress_ReturnsExpectedLua()
    {
        UnitInspectorService.BuildInspectUnitLuaCommand(0x12345678L)
            .Should().Be("return SWFOC_InspectUnit(305419896)");
    }

    [Fact]
    public void BuildInspectUnitLuaCommand_ZeroAddress_StillEmitsCall()
    {
        UnitInspectorService.BuildInspectUnitLuaCommand(0L)
            .Should().Be("return SWFOC_InspectUnit(0)");
    }

    // --- ParseInspectResult ---

    [Fact]
    public void ParseInspectResult_FullPayload_ParsesAllFields()
    {
        var raw = "hull=600 owner=0x140abcdef obj_id=42 parent_idx=0 status_flags=0x0 prevent_death=1 invuln_flag=0 hardpoint_flag=1 components_ptr=0xdeadbeef";

        var parsed = UnitInspectorService.ParseInspectResult(raw);

        parsed.Hull.Should().Be(600f);
        parsed.Owner.Should().Be(0x140abcdefL);
        parsed.ObjectId.Should().Be(42);
        parsed.ParentIndex.Should().Be(0);
        parsed.StatusFlags.Should().Be(0);
        parsed.PreventDeath.Should().Be(1);
        parsed.InvulnFlag.Should().Be(0);
        parsed.HardpointFlag.Should().Be(1);
        parsed.ComponentsPtr.Should().Be(0xdeadbeefL);
        parsed.RawFields.Should().ContainKey("hull");
    }

    [Fact]
    public void ParseInspectResult_NullOrEmpty_ReturnsAllNullFields()
    {
        var parsed = UnitInspectorService.ParseInspectResult(null);

        parsed.Hull.Should().BeNull();
        parsed.Owner.Should().BeNull();
        parsed.ObjectId.Should().BeNull();
        parsed.ParentIndex.Should().BeNull();
        parsed.StatusFlags.Should().BeNull();
        parsed.PreventDeath.Should().BeNull();
        parsed.InvulnFlag.Should().BeNull();
        parsed.HardpointFlag.Should().BeNull();
        parsed.ComponentsPtr.Should().BeNull();
        parsed.RawFields.Should().BeEmpty();
    }

    [Fact]
    public void ParseInspectResult_PartialPayload_ParsesAvailableFields()
    {
        var parsed = UnitInspectorService.ParseInspectResult("hull=42.5 obj_id=7");

        parsed.Hull.Should().Be(42.5f);
        parsed.ObjectId.Should().Be(7);
        parsed.Owner.Should().BeNull();
    }

    [Fact]
    public void ParseInspectResult_MalformedToken_IsIgnored()
    {
        var parsed = UnitInspectorService.ParseInspectResult("hull=10 garbage badtoken= =onlyvalue obj_id=3");

        parsed.Hull.Should().Be(10f);
        parsed.ObjectId.Should().Be(3);
    }

    // --- Constructor / offline mode ---

    [Fact]
    public void Constructor_NullBridgeOverload_Accepted()
    {
        var act = () => new UnitInspectorService(NullLogger);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task InspectUnitAsync_Offline_ReturnsSuccess()
    {
        var service = new UnitInspectorService(NullLogger);

        var result = await service.InspectUnitAsync("p1", 0xFFL, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.AddressSource.Should().Be(AddressSource.None);
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("return SWFOC_InspectUnit(255)");
        result.Diagnostics.Should().ContainKey("obj_addr");
    }

    [Fact]
    public async Task InspectUnitAsync_NullProfileId_Throws()
    {
        var service = new UnitInspectorService(NullLogger);

        var act = () => service.InspectUnitAsync(null!, 0L, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new UnitInspectorService(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
