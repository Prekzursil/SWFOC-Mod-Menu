using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 150) — pins SWFOC_LetterBoxOn / SWFOC_LetterBoxOff
/// LIVE wires. Per docs/lua-api.md, the canonical cinematic recipe is
/// Point_Camera_At(unit) + Letter_Box_On(). Iter 150 ships the second
/// half of the recipe; iter 145's cinematic camera quad covered the
/// camera side. Together they enable filming workflows.
///
/// LIVE flips #30-31. Master loop now at 31 LIVE wires.
/// </summary>
public sealed class Iter150LetterBoxTests
{
    [Fact]
    public void LetterBoxOn_StatusIsLive()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_LetterBoxOn"];
        entry.Status.Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void LetterBoxOff_StatusIsLive()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_LetterBoxOff"];
        entry.Status.Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void LetterBoxOn_NoteCitesEngineLuaApi()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_LetterBoxOn"];
        entry.Note.Should().Contain("Letter_Box_On");
        entry.Note.Should().Contain("DoString");
        entry.Note.Should().Contain("iter 145");
    }

    [Fact]
    public async System.Threading.Tasks.Task LetterBoxOnOff_StateMachineRoundTrips()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        sim.GameState.LetterBoxActive.Should().BeFalse("initial state");

        var onResp = await adapter.SendRawAsync(
            "return SWFOC_LetterBoxOn()", System.Threading.CancellationToken.None);
        onResp.Succeeded.Should().BeTrue();
        onResp.Response.Should().Contain("OK").And.Contain("LIVE");
        sim.GameState.LetterBoxActive.Should().BeTrue();

        var offResp = await adapter.SendRawAsync(
            "return SWFOC_LetterBoxOff()", System.Threading.CancellationToken.None);
        offResp.Succeeded.Should().BeTrue();
        offResp.Response.Should().Contain("OK").And.Contain("LIVE");
        sim.GameState.LetterBoxActive.Should().BeFalse();
    }

    [Fact]
    public void LetterBoxPair_BothLive_InvariantPin()
    {
        // Iter 150 — both letterbox primitives must remain LIVE as a unit.
        // Cinematic recipe in docs/lua-api.md treats them as a pair.
        CapabilityStatusCatalog.Entries["SWFOC_LetterBoxOn"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_LetterBoxOff"].Status
            .Should().Be(CapabilityStatus.Live);
    }
}
