using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 156) — pins 4-wire unit-method LIVE batch using
/// existing iter-111/112/154 helpers. ~3-5 LoC bridge per wire after
/// the dispatchers exist.
///
/// LIVE flips #43-46. Master loop now at 46 LIVE wires.
/// </summary>
public sealed class Iter156UnitMethodBatchTests
{
    [Fact]
    public void ActivateAbility_StatusIsLive() =>
        CapabilityStatusCatalog.Entries["SWFOC_ActivateAbilityLua"].Status
            .Should().Be(CapabilityStatus.Live);

    [Fact]
    public void DisableCapture_StatusIsLive() =>
        CapabilityStatusCatalog.Entries["SWFOC_DisableCaptureLua"].Status
            .Should().Be(CapabilityStatus.Live);

    [Fact]
    public void SetGarrisonSpawn_StatusIsLive() =>
        CapabilityStatusCatalog.Entries["SWFOC_SetGarrisonSpawnLua"].Status
            .Should().Be(CapabilityStatus.Live);

    [Fact]
    public void CancelHyperspace_StatusIsLive() =>
        CapabilityStatusCatalog.Entries["SWFOC_CancelHyperspaceLua"].Status
            .Should().Be(CapabilityStatus.Live);

    [Fact]
    public async System.Threading.Tasks.Task FourWireBatch_AllDispatchOk()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var ability = await adapter.SendRawAsync(
            "return SWFOC_ActivateAbilityLua('Find_First_Object(\"Empire_AT_AT\")', '\"Tractor_Beam\"')",
            System.Threading.CancellationToken.None);
        ability.Succeeded.Should().BeTrue();

        var capture = await adapter.SendRawAsync(
            "return SWFOC_DisableCaptureLua('Find_First_Object(\"Rebel_T2A_Tank\")', 'true')",
            System.Threading.CancellationToken.None);
        capture.Succeeded.Should().BeTrue();

        var garrison = await adapter.SendRawAsync(
            "return SWFOC_SetGarrisonSpawnLua('Find_First_Object(\"Empire_AT_AT\")', 'false')",
            System.Threading.CancellationToken.None);
        garrison.Succeeded.Should().BeTrue();

        var hyper = await adapter.SendRawAsync(
            "return SWFOC_CancelHyperspaceLua('Find_First_Object(\"Rebel_T2A_Tank\")')",
            System.Threading.CancellationToken.None);
        hyper.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Batch_AllFour_LiveInvariant()
    {
        CapabilityStatusCatalog.Entries["SWFOC_ActivateAbilityLua"].Status.Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_DisableCaptureLua"].Status.Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_SetGarrisonSpawnLua"].Status.Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_CancelHyperspaceLua"].Status.Should().Be(CapabilityStatus.Live);
    }
}
