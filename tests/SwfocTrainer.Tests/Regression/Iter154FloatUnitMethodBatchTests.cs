using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 154) — pins float-arg unit-method LIVE batch.
/// New Lua_DispatchUnitFloatMethod helper mirrors iter-111 bool-arg
/// pattern; ~5 LoC per wire after the helper exists.
///
/// LIVE flips #36-39 (Heal no-arg + 3 float-arg). Master loop now at
/// 39 LIVE wires.
/// </summary>
public sealed class Iter154FloatUnitMethodBatchTests
{
    [Fact]
    public void Heal_StatusIsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_HealUnitLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void TakeDamage_StatusIsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_TakeDamageLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void SetDamageModifier_StatusIsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_SetDamageModifierLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void SetRateOfFireModifier_StatusIsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_SetRateOfFireModifierLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void TakeDamage_NoteCitesIter96Chokepoint()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_TakeDamageLua"];
        entry.Note.Should().Contain("Take_Damage");
        entry.Note.Should().Contain("iter 96");
    }

    [Fact]
    public void SetRateOfFireModifier_NoteClosesIter101Gap()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetRateOfFireModifierLua"];
        entry.Note.Should().Contain("Set_Rate_Of_Fire_Modifier");
        entry.Note.Should().Contain("iter 101",
            "operator should know this closes the SetFireRate gap iter 101 left at the global level");
    }

    [Fact]
    public async System.Threading.Tasks.Task FloatBatch_AllFour_DispatchOk()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var heal = await adapter.SendRawAsync(
            "return SWFOC_HealUnitLua('Find_First_Object(\"Empire_AT_AT\")')",
            System.Threading.CancellationToken.None);
        heal.Succeeded.Should().BeTrue();
        heal.Response.Should().Contain("OK");

        var damage = await adapter.SendRawAsync(
            "return SWFOC_TakeDamageLua('Find_First_Object(\"Empire_AT_AT\")', '500.0')",
            System.Threading.CancellationToken.None);
        damage.Succeeded.Should().BeTrue();
        damage.Response.Should().Contain("OK");

        var dmgMod = await adapter.SendRawAsync(
            "return SWFOC_SetDamageModifierLua('Find_First_Object(\"Rebel_T2A_Tank\")', '2.5')",
            System.Threading.CancellationToken.None);
        dmgMod.Succeeded.Should().BeTrue();
        dmgMod.Response.Should().Contain("OK");

        var rofMod = await adapter.SendRawAsync(
            "return SWFOC_SetRateOfFireModifierLua('Find_First_Object(\"Rebel_T2A_Tank\")', '3.0')",
            System.Threading.CancellationToken.None);
        rofMod.Succeeded.Should().BeTrue();
        rofMod.Response.Should().Contain("OK");
    }

    [Fact]
    public void FloatBatch_AllFour_LiveInvariant()
    {
        // All 4 iter-154 float-arg / no-arg wires must remain LIVE as a unit.
        CapabilityStatusCatalog.Entries["SWFOC_HealUnitLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_TakeDamageLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_SetDamageModifierLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_SetRateOfFireModifierLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }
}
