using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using SwfocTrainer.Tests.V2;
using Xunit;

namespace SwfocTrainer.Tests.Simulator.E2E;

/// <summary>
/// 2026-04-27 (iter 26 — Phase D continued) — VM-driven scenarios for the
/// HeroLabTabViewModel. Validates that refreshing heroes / killing /
/// reviving / mass-reviving / setting respawn timer / editing a hero stat
/// from the operator UI mutates the simulated state correctly.
/// </summary>
public sealed class HeroLabViewModelScenarioTests
{
    private static (SwfocSimulator sim, HeroLabTabViewModel vm, FakeGameState state, FakeUnit hero1, FakeUnit hero2) NewSession()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var hero1 = new FakeUnit
        {
            TypeName = "Han_Solo",
            OwnerSlot = 0,
            IsHero = true,
            MaxHull = 200,
            CurrentHull = 200,
        };
        var hero2 = new FakeUnit
        {
            TypeName = "Luke_Skywalker",
            OwnerSlot = 0,
            IsHero = true,
            MaxHull = 250,
            CurrentHull = 0,
            Alive = false,
        };
        state.Units.Add(hero1);
        state.Units.Add(hero2);
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, new HeroLabTabViewModel(adapter), state, hero1, hero2);
    }

    [Fact]
    public async Task RefreshHeroes_PopulatesHeroesCollection()
    {
        var (sim, vm, state, hero1, hero2) = NewSession();
        using var _ = sim;

        await AsyncCommandPump.PumpAsync(vm.RefreshHeroesCommand);

        vm.Heroes.Should().HaveCount(2);
        vm.Heroes.Should().Contain(h => h.TypeName == "Han_Solo");
        vm.Heroes.Should().Contain(h => h.TypeName == "Luke_Skywalker");
    }

    [Fact]
    public async Task ReviveHero_FlipsAliveTrue()
    {
        var (sim, vm, state, hero1, hero2) = NewSession();
        using var _ = sim;

        // Refresh first so the VM has heroes loaded.
        await AsyncCommandPump.PumpAsync(vm.RefreshHeroesCommand);
        var dead = vm.Heroes.First(h => !h.Alive);
        vm.SelectedHeroAddr = dead.ObjAddr;

        await AsyncCommandPump.PumpAsync(vm.ReviveHeroCommand);

        // The simulated hero should be alive again.
        var liveHero = state.Units.First(u => u.Id == (int)dead.ObjAddr);
        liveHero.Alive.Should().BeTrue();
        liveHero.CurrentHull.Should().Be(liveHero.MaxHull);
    }

    [Fact]
    public async Task KillHero_FlipsAliveFalse()
    {
        var (sim, vm, state, hero1, hero2) = NewSession();
        using var _ = sim;

        await AsyncCommandPump.PumpAsync(vm.RefreshHeroesCommand);
        var liveHeroRow = vm.Heroes.First(h => h.Alive);
        vm.SelectedHeroAddr = liveHeroRow.ObjAddr;

        await AsyncCommandPump.PumpAsync(vm.KillHeroCommand);

        state.Units.First(u => u.Id == (int)liveHeroRow.ObjAddr).Alive.Should().BeFalse();
    }

    [Fact]
    public async Task SetRespawn_UpdatesGlobalTimer()
    {
        var (sim, vm, state, hero1, hero2) = NewSession();
        using var _ = sim;

        await AsyncCommandPump.PumpAsync(vm.RefreshHeroesCommand);
        var hero = vm.Heroes.First();
        vm.SelectedHeroAddr = hero.ObjAddr;
        vm.CustomRespawnMs = 30_000; // 30 seconds

        await AsyncCommandPump.PumpAsync(vm.SetRespawnCommand);

        state.HeroRespawnSeconds.Should().Be(30);
    }

    [Fact]
    public async Task EditStat_UpdatesSelectedHeroField()
    {
        var (sim, vm, state, hero1, hero2) = NewSession();
        using var _ = sim;

        await AsyncCommandPump.PumpAsync(vm.RefreshHeroesCommand);
        vm.SelectedHeroAddr = hero1.Id;
        vm.EditField = "MaxHull";
        vm.EditValue = 9999f;

        await AsyncCommandPump.PumpAsync(vm.EditStatCommand);

        hero1.MaxHull.Should().Be(9999f);
    }

    [Fact]
    public async Task ReviveAllHeroes_RevivesEveryDeadHero()
    {
        var (sim, vm, state, hero1, hero2) = NewSession();
        using var _ = sim;

        // Add a third hero, also dead, to make the mass-respawn meaningful.
        var hero3 = new FakeUnit
        {
            TypeName = "Princess_Leia",
            OwnerSlot = 0,
            IsHero = true,
            MaxHull = 180,
            CurrentHull = 0,
            Alive = false,
        };
        state.Units.Add(hero3);

        await AsyncCommandPump.PumpAsync(vm.RefreshHeroesCommand);
        await AsyncCommandPump.PumpAsync(vm.ReviveAllHeroesCommand);

        state.Units.Where(u => u.IsHero).All(u => u.Alive).Should().BeTrue();
    }
}
