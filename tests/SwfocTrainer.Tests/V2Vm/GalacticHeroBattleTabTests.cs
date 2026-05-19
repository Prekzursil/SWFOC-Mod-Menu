using FluentAssertions;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.V2Vm;

internal sealed class RecordingGalacticDispatcher : IGalacticDispatcher
{
    public List<string> Calls { get; } = new();
    public IReadOnlyList<PlanetRow> PlanetSeed { get; set; } = Array.Empty<PlanetRow>();
    public bool ReturnValue { get; set; } = true;

    public Task<IReadOnlyList<PlanetRow>> GetPlanetsAsync(CancellationToken ct)
    { Calls.Add("GetPlanets"); return Task.FromResult(PlanetSeed); }
    public Task<bool> ChangePlanetOwnerAsync(string p, string n, CancellationToken ct)
    { Calls.Add($"ChangeOwner({p}→{n})"); return Task.FromResult(ReturnValue); }
    public Task<bool> ChangePlanetOwnerWithModeAsync(string p, string n, PlanetFlipMode m, CancellationToken ct)
    { Calls.Add($"ChangeOwner({p}→{n};{m})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SpawnAsStoryArrivalAsync(string t, string p, string f, CancellationToken ct)
    { Calls.Add($"StoryArrival({t}@{p}#{f})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetRevealAllAsync(bool e, CancellationToken ct)
    { Calls.Add($"Reveal({e})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetDiplomacyAsync(string a, string b, DiplomacyRelation r, CancellationToken ct)
    { Calls.Add($"Dipl({a},{b},{r})"); return Task.FromResult(ReturnValue); }
}

public sealed class GalacticTabStateTests
{
    private (GalacticTabState s, RecordingGalacticDispatcher d, RecordingFeedbackSink fb,
             FeatureToggleCoordinator coord) Build()
    {
        var d = new RecordingGalacticDispatcher();
        var fb = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(fb);
        return (new GalacticTabState(d, fb, coord), d, fb, coord);
    }

    [Fact]
    public async Task RefreshPlanets_PopulatesList()
    {
        var (s, d, _, _) = Build();
        d.PlanetSeed = new[] {
            new PlanetRow("Coruscant", "EMPIRE", 5),
            new PlanetRow("Naboo", "REBEL", 3),
        };
        var fb = await s.RefreshPlanetsAsync();
        fb.Severity.Should().Be(UxSeverity.Info);
        s.Planets.Should().HaveCount(2);
    }

    [Fact]
    public async Task ChangeOwner_RequiresPlanet_AndOwner()
    {
        var (s, d, _, _) = Build();
        (await s.ChangePlanetOwnerAsync()).Severity.Should().Be(UxSeverity.Error);
        s.SelectedPlanetId = "Naboo";
        (await s.ChangePlanetOwnerAsync()).Severity.Should().Be(UxSeverity.Error);
        s.NewOwnerFaction = "REBEL";
        (await s.ChangePlanetOwnerAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("ChangeOwner(Naboo→REBEL)");
    }

    [Fact]
    public async Task ToggleRevealAll_EnableThenCleanup_FlipsBackOff()
    {
        var (s, d, _, coord) = Build();
        await s.ToggleRevealAllAsync(true);
        coord.IsEnabled("reveal_all").Should().BeTrue();
        await coord.CleanupAllAsync();
        coord.IsEnabled("reveal_all").Should().BeFalse();
        d.Calls.Should().ContainInOrder("Reveal(True)", "Reveal(False)");
    }

    [Fact]
    public async Task SetDiplomacy_NeutralIsRejectedWithWarning()
    {
        var (s, d, _, _) = Build();
        s.DiplomacySlotA = "REBEL"; s.DiplomacySlotB = "EMPIRE";
        s.DiplomacyRelation = DiplomacyRelation.Neutral;
        var fb = await s.SetDiplomacyAsync();
        fb.Severity.Should().Be(UxSeverity.Warning);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetDiplomacy_AlliedDispatches()
    {
        var (s, d, _, _) = Build();
        s.DiplomacySlotA = "REBEL"; s.DiplomacySlotB = "EMPIRE";
        s.DiplomacyRelation = DiplomacyRelation.Allied;
        (await s.SetDiplomacyAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("Dipl(REBEL,EMPIRE,Allied)");
    }
}

internal sealed class RecordingHeroDispatcher : IHeroLabDispatcher
{
    public List<string> Calls { get; } = new();
    public IReadOnlyList<HeroRow> Seed { get; set; } = Array.Empty<HeroRow>();
    public bool ReturnValue { get; set; } = true;
    public Task<IReadOnlyList<HeroRow>> ListHeroesAsync(CancellationToken ct)
    { Calls.Add("List"); return Task.FromResult(Seed); }
    public Task<bool> SetHeroRespawnTimerAsync(long a, int ms, CancellationToken ct)
    { Calls.Add($"Respawn(0x{a:X},{ms})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetPermadeathAsync(long a, bool p, CancellationToken ct)
    { Calls.Add($"Permadeath(0x{a:X},{p})"); return Task.FromResult(ReturnValue); }
    public Task<bool> KillHeroAsync(long a, CancellationToken ct)
    { Calls.Add($"Kill(0x{a:X})"); return Task.FromResult(ReturnValue); }
    public Task<bool> ReviveHeroAsync(long a, CancellationToken ct)
    { Calls.Add($"Revive(0x{a:X})"); return Task.FromResult(ReturnValue); }
    public Task<bool> EditHeroStatAsync(long a, string f, float v, CancellationToken ct)
    { Calls.Add($"Edit(0x{a:X},{f},{v})"); return Task.FromResult(ReturnValue); }
}

public sealed class HeroLabTabStateTests
{
    private (HeroLabTabState s, RecordingHeroDispatcher d, RecordingFeedbackSink fb,
             FeatureToggleCoordinator coord) Build()
    {
        var d = new RecordingHeroDispatcher();
        var fb = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(fb);
        return (new HeroLabTabState(d, fb, coord), d, fb, coord);
    }

    [Fact]
    public async Task RefreshHeroes_PopulatesList()
    {
        var (s, d, _, _) = Build();
        d.Seed = new[] { new HeroRow(0x1, "Luke", 1, true, 0, true) };
        await s.RefreshHeroesAsync();
        s.Heroes.Should().HaveCount(1);
    }

    [Fact]
    public async Task SetCustomRespawn_Operations()
    {
        var (s, d, _, _) = Build();
        (await s.SetCustomRespawnAsync()).Severity.Should().Be(UxSeverity.Error);   // no selection
        s.SelectedHeroAddr = 0x1234; s.CustomRespawnMs = -100;
        (await s.SetCustomRespawnAsync()).Severity.Should().Be(UxSeverity.Error);   // negative
        s.CustomRespawnMs = 5000;
        (await s.SetCustomRespawnAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("Respawn(0x1234,5000)");
    }

    [Fact]
    public async Task TogglePermadeath_EnableAndCleanup()
    {
        var (s, d, _, coord) = Build();
        s.SelectedHeroAddr = 0xABC;
        await s.TogglePermadeathAsync(true);
        coord.IsEnabled("permadeath_0xABC").Should().BeTrue();
        await coord.CleanupAllAsync();
        d.Calls.Should().ContainInOrder("Permadeath(0xABC,True)", "Permadeath(0xABC,False)");
    }

    [Fact]
    public async Task TogglePermadeath_NoSelection_Rejected()
    {
        var (s, d, fb, _) = Build();
        s.SelectedHeroAddr = 0;
        var result = await s.TogglePermadeathAsync(true);
        result.Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task KillReviveEdit_OperateOnSelection()
    {
        var (s, d, _, _) = Build();
        s.SelectedHeroAddr = 0xDEAD;
        s.EditField = "hull"; s.EditValue = 9999;
        (await s.KillHeroAsync()).Severity.Should().Be(UxSeverity.Success);
        (await s.ReviveHeroAsync()).Severity.Should().Be(UxSeverity.Success);
        (await s.EditStatAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain(new[] { "Kill(0xDEAD)", "Revive(0xDEAD)", "Edit(0xDEAD,hull,9999)" });
    }

    [Fact]
    public async Task EditStat_EmptyFieldRejected()
    {
        var (s, d, _, _) = Build();
        s.SelectedHeroAddr = 0xDEAD;
        s.EditField = "";
        (await s.EditStatAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }
}

internal sealed class RecordingBattleControlDispatcher : IBattleControlDispatcher
{
    public List<string> Calls { get; } = new();
    public bool ReturnValue { get; set; } = true;
    public Task<bool> SetFreezeAiAsync(int s, bool e, CancellationToken ct)
    { Calls.Add($"FreezeAi({s},{e})"); return Task.FromResult(ReturnValue); }
    public Task<bool> KillAllEnemiesAsync(CancellationToken ct)
    { Calls.Add("KillAll"); return Task.FromResult(ReturnValue); }
    public Task<bool> HealAllLocalAsync(CancellationToken ct)
    { Calls.Add("HealAll"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetUnitCapOverrideAsync(int s, int cap, CancellationToken ct)
    { Calls.Add($"SetCap({s},{cap})"); return Task.FromResult(ReturnValue); }
    public Task<bool> ClearUnitCapOverrideAsync(int s, CancellationToken ct)
    { Calls.Add($"ClearCap({s})"); return Task.FromResult(ReturnValue); }
}

public sealed class BattleControlTabStateTests
{
    private (BattleControlTabState s, RecordingBattleControlDispatcher d, RecordingFeedbackSink fb,
             FeatureToggleCoordinator coord) Build()
    {
        var d = new RecordingBattleControlDispatcher();
        var fb = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(fb);
        return (new BattleControlTabState(d, fb, coord), d, fb, coord);
    }

    [Fact]
    public async Task ToggleFreezeAi_EnableAndCleanup()
    {
        var (s, d, _, coord) = Build();
        s.TargetSlot = 0;
        await s.ToggleFreezeAiAsync(true);
        coord.IsEnabled("freeze_ai_slot0").Should().BeTrue();
        await coord.CleanupAllAsync();
        d.Calls.Should().ContainInOrder("FreezeAi(0,True)", "FreezeAi(0,False)");
    }

    [Fact]
    public async Task KillAllAndHealAll_Dispatch()
    {
        var (s, d, _, _) = Build();
        (await s.KillAllEnemiesAsync()).Severity.Should().Be(UxSeverity.Success);
        (await s.HealAllLocalAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain(new[] { "KillAll", "HealAll" });
    }

    [Fact]
    public async Task SetUnitCap_OperatesAndValidates()
    {
        var (s, d, _, _) = Build();
        s.TargetSlot = -1;
        (await s.SetUnitCapAsync()).Severity.Should().Be(UxSeverity.Error);
        s.TargetSlot = 0; s.UnitCap = -2;
        (await s.SetUnitCapAsync()).Severity.Should().Be(UxSeverity.Error);
        s.UnitCap = -1;  // unlimited sentinel
        (await s.SetUnitCapAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("SetCap(0,-1)");
    }

    [Fact]
    public async Task ClearUnitCap_RejectsNegativeSlot()
    {
        var (s, d, _, _) = Build();
        s.TargetSlot = -1;
        (await s.ClearUnitCapAsync()).Severity.Should().Be(UxSeverity.Error);
        s.TargetSlot = 1;
        (await s.ClearUnitCapAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("ClearCap(1)");
    }
}
