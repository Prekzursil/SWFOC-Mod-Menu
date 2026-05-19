using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using SwfocTrainer.Tests.V2;
using Xunit;

namespace SwfocTrainer.Tests.Catalog;

/// <summary>
/// 2026-05-07 (iter 299): pin tests for SWFOC_GetFactionRoster +
/// SWFOC_GetCurrentMod bridge wires. Both wires ship LIVE on first
/// introduction (no Phase2HookPending intermediate) — see catalog rationale.
/// Closes iter-294 Audit B's "missing enumeration wires" gaps #1-2 of 6.
/// </summary>
public sealed class Iter299FactionRosterAndCurrentModTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession(FakeGameState state)
    {
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        return (sim, new V2BridgeAdapter(pipe));
    }

    [Fact]
    public void SwfocGetFactionRoster_IsCataloguedAsLive()
    {
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_GetFactionRoster");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 299 ships SWFOC_GetFactionRoster as LIVE via DoString-driven engine Lua API");
        (entry.Note ?? string.Empty).ToLowerInvariant().Should().Contain("iter 299");
        (entry.Note ?? string.Empty).Should().Contain("Find_All_Objects_Of_Type",
            because: "rationale should pin the engine Lua API used");
    }

    [Fact]
    public void SwfocGetCurrentMod_IsCataloguedAsLive()
    {
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_GetCurrentMod");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 299 ships SWFOC_GetCurrentMod as LIVE via filesystem ./Mods/* probe");
        (entry.Note ?? string.Empty).ToLowerInvariant().Should().Contain("iter 299");
        (entry.Note ?? string.Empty).Should().Contain("Modinfo.xml",
            because: "rationale should pin the filesystem mechanism");
    }

    [Fact]
    public async Task Simulator_GetFactionRoster_FiltersByFaction()
    {
        var state = new FakeGameState();
        state.Players.Add(new FakePlayer { Slot = 1, Faction = "Rebel" });
        state.Players.Add(new FakePlayer { Slot = 2, Faction = "Empire" });
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper", OwnerSlot = 1, IsGround = true });
        state.Units.Add(new FakeUnit { TypeName = "Han_Solo", OwnerSlot = 1, IsHero = true });
        state.Units.Add(new FakeUnit { TypeName = "Stormtrooper", OwnerSlot = 2, IsGround = true });
        state.Units.Add(new FakeUnit { TypeName = "Star_Destroyer", OwnerSlot = 2 });

        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var rt = await adapter.SendRawAsync("return SWFOC_GetFactionRoster('Rebel')", CancellationToken.None);
        rt.Succeeded.Should().BeTrue();
        var result = rt.Response ?? string.Empty;

        result.Should().Contain("Rebel_Trooper;GroundCompany",
            because: "ground unit owned by slot 1 (Rebel) should appear");
        result.Should().Contain("Han_Solo;Hero",
            because: "hero unit owned by slot 1 (Rebel) should appear");
        result.Should().NotContain("Stormtrooper",
            because: "Empire-owned unit should NOT appear in Rebel roster");
        result.Should().NotContain("Star_Destroyer");
    }

    [Fact]
    public async Task Simulator_GetFactionRoster_EmptyFactionReturnsSentinel()
    {
        var state = new FakeGameState();
        state.Players.Add(new FakePlayer { Slot = 1, Faction = "Rebel" });
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper", OwnerSlot = 1, IsGround = true });

        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var rt = await adapter.SendRawAsync("return SWFOC_GetFactionRoster('Empire')", CancellationToken.None);
        rt.Succeeded.Should().BeTrue();
        rt.Response.Should().Be("(empty)",
            because: "no units owned by Empire-faction slot should produce sentinel matching the bridge wire format");
    }

    [Fact]
    public async Task Simulator_GetCurrentMod_VanillaWhenNoMod()
    {
        var state = new FakeGameState(); // ActiveModName defaults to ""
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var rt = await adapter.SendRawAsync("return SWFOC_GetCurrentMod", CancellationToken.None);
        rt.Succeeded.Should().BeTrue();
        rt.Response.Should().Be("vanilla",
            because: "no active mod set in fake state -> bridge wire returns 'vanilla' sentinel");
    }

    [Fact]
    public async Task Simulator_GetCurrentMod_ReturnsModNameVersionAndPath()
    {
        var state = new FakeGameState
        {
            ActiveModName = "AOTR",
            ActiveModVersion = "2.7",
            ActiveModPath = @"C:\Games\SWFOC\Mods\AOTR",
        };
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var rt = await adapter.SendRawAsync("return SWFOC_GetCurrentMod", CancellationToken.None);
        rt.Succeeded.Should().BeTrue();
        var result = rt.Response ?? string.Empty;

        result.Should().Contain("AOTR;2.7", because: "first line is name;version per bridge wire format");
        result.Should().Contain(@"C:\Games\SWFOC\Mods\AOTR", because: "second line is absolute path");
        result.Should().Contain("\n", because: "name;version and path are separated by newline");
    }
}
