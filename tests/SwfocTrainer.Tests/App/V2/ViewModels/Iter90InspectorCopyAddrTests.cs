using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 90) — pins Inspector tab's "Copy addr" button.
/// Returns hex format with "0x" prefix; falls back to parsing the
/// ObjAddrInput string the same way the property setter does.
/// </summary>
public sealed class Iter90InspectorCopyAddrTests
{
    private static (SwfocSimulator sim, InspectorTabViewModel vm) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, new InspectorTabViewModel(adapter, new V2UnitMutationDispatcher(adapter)));
    }

    [Fact]
    public void CopyObjAddrCommand_IsExposed()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;

        vm.CopyObjAddrCommand.Should().NotBeNull();
    }

    [Fact]
    public void BuildObjAddrHex_NoInput_ReturnsEmpty()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;

        vm.BuildObjAddrHex().Should().BeEmpty();
    }

    [Fact]
    public void BuildObjAddrHex_DecimalInput_ReturnsHexWithPrefix()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;

        vm.ObjAddrInput = "4096";

        vm.BuildObjAddrHex().Should().Be("0x1000",
            "decimal 4096 → hex 0x1000");
    }

    [Fact]
    public void BuildObjAddrHex_HexInputWithoutPrefix_ParsedAsHex()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        // String "DEADBEEF" parses as decimal first (fails), then hex.
        vm.ObjAddrInput = "DEADBEEF";

        vm.BuildObjAddrHex().Should().Be("0xDEADBEEF");
    }

    [Fact]
    public void BuildObjAddrHex_LargeAddress_PreservesAllBits()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        // Realistic SWFOC unit addr — image-base + offset.
        vm.ObjAddrInput = "140547B0C";  // 64-bit-ish addr

        vm.BuildObjAddrHex().Should().Be("0x140547B0C");
    }

    [Fact]
    public void BuildObjAddrHex_ZeroInput_ReturnsEmpty()
    {
        // Zero is a sentinel for "no addr" in the Inspector — should not
        // produce a confusing "0x0" clipboard payload.
        var (sim, vm) = NewSession();
        using var _ = sim;

        vm.ObjAddrInput = "0";

        vm.BuildObjAddrHex().Should().BeEmpty();
    }
}
