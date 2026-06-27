using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using SwfocTrainer.Tests.V2;
using Xunit;

namespace SwfocTrainer.Tests.Simulator.E2E;

/// <summary>
/// 2026-04-27 (iter 30 — Phase E continued) — VM-driven scenarios for the
/// WorldStateTabViewModel. Wires the 7-dependency constructor with REAL
/// services (CorruptionService, DiplomacyService, StoryEventService,
/// MaphackService, CrashAnalyzerService) plus V2Settings + V2FactionRegistry,
/// all pointed at the simulator's bridge.
/// </summary>
/// <summary>
/// Empty catalog — StoryEventService takes one in its constructor for
/// validating event ids against the static catalog. The simulator doesn't
/// need that validation; an empty dict allows any event id through.
/// </summary>
internal sealed class NullCatalogService : ICatalogService
{
    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(
        string profileId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
            new Dictionary<string, IReadOnlyList<string>>());
}

public sealed class WorldStateViewModelScenarioTests
{
    private static (SwfocSimulator sim, WorldStateTabViewModel vm, FakeGameState state) NewSession()
    {
        var state = FakeGameState.NewGalacticCampaign();
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, connectTimeoutMs: 1500, readTimeoutMs: 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var settings = new V2Settings();
        var corruption = new CorruptionService(adapter, NullLogger<CorruptionService>.Instance);
        var diplomacy = new DiplomacyService(adapter, NullLogger<DiplomacyService>.Instance);
        var storyEvents = new StoryEventService(
            new NullCatalogService(), adapter, NullLogger<StoryEventService>.Instance);
        var maphack = new MaphackService(adapter, NullLogger<MaphackService>.Instance);
        var crashAnalyzer = new CrashAnalyzerService(adapter, NullLogger<CrashAnalyzerService>.Instance);
        var factionRegistry = new V2FactionRegistry();

        var unitMutator = new V2UnitMutationDispatcher(adapter);
        var vm = new WorldStateTabViewModel(
            settings, corruption, diplomacy, storyEvents, maphack, crashAnalyzer, factionRegistry, unitMutator);
        return (sim, vm, state);
    }

    [Fact]
    public async Task FireStoryEvent_AddsFlagToSimulatorState()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.StoryEventId = "EVENT_DEATH_STAR_BUILT";
        await AsyncCommandPump.PumpAsync(vm.FireStoryEventCommand);

        state.StoryFlags.Should().Contain("EVENT_DEATH_STAR_BUILT");
        state.EventQueue.Should().Contain("STORY_FIRED:EVENT_DEATH_STAR_BUILT",
            "Phase B chained story events into the event queue too");
    }

    [Fact]
    public async Task SetDiplomacy_StoresFactionPairRelation()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.FactionA = "REBEL";
        vm.FactionB = "EMPIRE";
        vm.SelectedDiplomacyRelation = SwfocTrainer.Core.Models.DiplomacyRelation.Allied;

        await AsyncCommandPump.PumpAsync(vm.SetDiplomacyCommand);

        state.Diplomacy.Should().ContainKey("REBEL:EMPIRE");
        state.Diplomacy["REBEL:EMPIRE"].Should().Be("Allied");
    }
}
