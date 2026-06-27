using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Catalog;

/// <summary>
/// 2026-05-07 (iter 300; 300th-iter milestone): pin tests for SWFOC_ListMods
/// bridge wire. Sibling to iter-299 GetCurrentMod (which picks the active mod);
/// ListMods enumerates ALL mods discoverable under ./Mods/*. Closes 4th of
/// 6 missing enumeration wires from iter-294 Audit B.
/// </summary>
public sealed class Iter300ListModsTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession(FakeGameState state)
    {
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        return (sim, new V2BridgeAdapter(pipe));
    }

    [Fact]
    public void SwfocListMods_IsCataloguedAsLive()
    {
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_ListMods");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 300 ships SWFOC_ListMods as LIVE via filesystem ./Mods/*/Modinfo.xml enumeration");
        (entry.Note ?? string.Empty).ToLowerInvariant().Should().Contain("iter 300");
        (entry.Note ?? string.Empty).Should().Contain("Modinfo.xml",
            because: "rationale should pin the filesystem mechanism");
    }

    [Fact]
    public async Task Simulator_ListMods_NoModsSentinel_WhenEmpty()
    {
        var state = new FakeGameState(); // AvailableMods defaults empty
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var rt = await adapter.SendRawAsync("return SWFOC_ListMods", CancellationToken.None);
        rt.Succeeded.Should().BeTrue();
        rt.Response.Should().Be("(no_mods)",
            because: "empty AvailableMods list -> bridge sentinel '(no_mods)'");
    }

    [Fact]
    public async Task Simulator_ListMods_ReturnsAllAvailableMods()
    {
        var state = new FakeGameState();
        state.AvailableMods.Add(("AOTR", @"C:\Games\SWFOC\Mods\AOTR"));
        state.AvailableMods.Add(("ROTM", @"C:\Games\SWFOC\Mods\ROTM"));
        state.AvailableMods.Add(("Awakening_of_the_Rebellion", @"C:\Games\SWFOC\Mods\Awakening_of_the_Rebellion"));

        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var rt = await adapter.SendRawAsync("return SWFOC_ListMods", CancellationToken.None);
        rt.Succeeded.Should().BeTrue();
        var result = rt.Response ?? string.Empty;

        result.Should().Contain("AOTR;C:\\Games\\SWFOC\\Mods\\AOTR");
        result.Should().Contain("ROTM;C:\\Games\\SWFOC\\Mods\\ROTM");
        result.Should().Contain("Awakening_of_the_Rebellion;C:\\Games\\SWFOC\\Mods\\Awakening_of_the_Rebellion");
        result.Split('\n').Length.Should().Be(3,
            because: "3 mods seeded -> 3 newline-separated rows");
    }

    [Fact]
    public async Task Simulator_ListMods_AndGetCurrentMod_CrossReferenceWorks()
    {
        // Cross-iteration check: iter-300 ListMods + iter-299 GetCurrentMod
        // should agree about which mods exist when both are populated.
        var state = new FakeGameState
        {
            ActiveModName = "AOTR",
            ActiveModVersion = "2.7",
            ActiveModPath = @"C:\Games\SWFOC\Mods\AOTR",
        };
        state.AvailableMods.Add(("AOTR", @"C:\Games\SWFOC\Mods\AOTR"));
        state.AvailableMods.Add(("Vanilla_Plus", @"C:\Games\SWFOC\Mods\Vanilla_Plus"));

        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var listRt = await adapter.SendRawAsync("return SWFOC_ListMods", CancellationToken.None);
        var currentRt = await adapter.SendRawAsync("return SWFOC_GetCurrentMod", CancellationToken.None);

        listRt.Succeeded.Should().BeTrue();
        currentRt.Succeeded.Should().BeTrue();
        var listResult = listRt.Response ?? string.Empty;
        var currentResult = currentRt.Response ?? string.Empty;

        listResult.Should().Contain("AOTR");
        currentResult.Should().StartWith("AOTR;",
            because: "iter-299 GetCurrentMod returns name;version on first line");
        // Cross-ref: the active mod from GetCurrentMod must appear in the ListMods enumeration.
        var activeName = currentResult.Split(';', '\n')[0];
        listResult.Should().Contain(activeName + ";",
            because: $"the active mod '{activeName}' must be one of the listed mods");
    }
}
