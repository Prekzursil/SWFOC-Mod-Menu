using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.V2Vm;

internal sealed class RecordingSpeedDispatcher : ISpeedDispatcher
{
    public List<string> Calls { get; } = new();
    public bool ReturnValue { get; set; } = true;
    public Task<bool> SetGameSpeedAsync(float s, CancellationToken ct)
    { Calls.Add($"GameSpeed({s:0.00})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetFactionSpeedMultiplierAsync(int s, float m, CancellationToken ct)
    { Calls.Add($"Faction({s},{m:0.00})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetUnitSpeedAsync(long addr, float spd, CancellationToken ct)
    { Calls.Add($"Unit(0x{addr:X},{spd:0.00})"); return Task.FromResult(ReturnValue); }
    // 2026-04-28 (iter 100): revert helper.
    public Task<bool> ClearUnitSpeedOverrideAsync(long addr, CancellationToken ct)
    { Calls.Add($"Clear(0x{addr:X})"); return Task.FromResult(ReturnValue); }
}

public sealed class SpeedTabStateTests
{
    private (SpeedTabState s, RecordingSpeedDispatcher d, RecordingFeedbackSink fb) Build()
    {
        var d = new RecordingSpeedDispatcher();
        var fb = new RecordingFeedbackSink();
        return (new SpeedTabState(d, fb), d, fb);
    }

    [Theory]
    [InlineData(0.0f, "paused (0×)")]
    [InlineData(1.0f, "1.00×")]
    [InlineData(20.0f, "20.00×")]
    public async Task SetGameSpeed_ReportsHumanReadableLabel(float speed, string expected)
    {
        var (s, d, _) = Build();
        s.GlobalGameSpeed = speed;
        var fb = await s.SetGlobalGameSpeedAsync();
        fb.Severity.Should().Be(UxSeverity.Success);
        fb.Message.Should().Contain(expected);
    }

    [Fact]
    public async Task SetGameSpeed_NegativeRejected()
    {
        var (s, d, _) = Build();
        s.GlobalGameSpeed = -1;
        (await s.SetGlobalGameSpeedAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetFactionMoveSpeed_NegativeRejected()
    {
        var (s, d, _) = Build();
        s.FactionMoveSpeedMultiplier = -0.5f;
        (await s.SetFactionMoveSpeedAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetUnitSpeed_NoSelection_Rejected()
    {
        var (s, d, _) = Build();
        s.SelectedObjAddr = 0;
        (await s.SetUnitSpeedAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetUnitSpeed_ValidInput_Dispatches()
    {
        var (s, d, _) = Build();
        s.SelectedObjAddr = 0xABCD;
        s.UnitSpeed = 7.5f;
        (await s.SetUnitSpeedAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("Unit(0xABCD,7.50)");
    }
}

internal sealed class RecordingInspectorDispatcher : IInspectorDispatcher
{
    public Dictionary<long, InspectorDetailSnapshot?> Map { get; } = new();
    public Task<InspectorDetailSnapshot?> InspectUnitAsync(long addr, CancellationToken ct)
        => Task.FromResult(Map.TryGetValue(addr, out var s) ? s : null);
}

public sealed class InspectorTabStateTests
{
    [Fact]
    public async Task RefreshAsync_NoSelection_EmitsInfo()
    {
        var d = new RecordingInspectorDispatcher();
        var fb = new RecordingFeedbackSink();
        var state = new InspectorTabState(d, fb);
        var result = await state.RefreshAsync();
        result.Severity.Should().Be(UxSeverity.Info);
        state.CurrentSnapshot.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_ValidUnit_PopulatesSnapshot()
    {
        var d = new RecordingInspectorDispatcher();
        d.Map[0x1000] = new InspectorDetailSnapshot(
            0x1000, "x-wing", 1, 100, 100, 50, 50, 5, 5, false, false, false);
        var fb = new RecordingFeedbackSink();
        var state = new InspectorTabState(d, fb);
        state.SelectedUnit = new TacticalUnitRow(0x1000, 1, 100, 0, 0, true, true);

        var result = await state.RefreshAsync();
        result.Severity.Should().Be(UxSeverity.Info);
        state.CurrentSnapshot.Should().NotBeNull();
        state.CurrentSnapshot!.TypeName.Should().Be("x-wing");
        state.CurrentSnapshot.Hull.Should().Be(100);
    }

    [Fact]
    public async Task RefreshAsync_DespawnedUnit_EmitsWarningAndClearsSnapshot()
    {
        var d = new RecordingInspectorDispatcher();
        // No entry in Map → dispatcher returns null
        var fb = new RecordingFeedbackSink();
        var state = new InspectorTabState(d, fb);
        state.SelectedUnit = new TacticalUnitRow(0xDEAD, 1, 100, 0, 0, true, true);

        // Set a snapshot first to verify it gets cleared on despawn.
        var result = await state.RefreshAsync();
        result.Severity.Should().Be(UxSeverity.Warning);
        result.Message.Should().Contain("re-select");
        state.CurrentSnapshot.Should().BeNull();
    }

    [Fact]
    public void Clear_ResetsSelectionAndSnapshot()
    {
        var d = new RecordingInspectorDispatcher();
        var fb = new RecordingFeedbackSink();
        var state = new InspectorTabState(d, fb);
        state.SelectedUnit = new TacticalUnitRow(0x1, 1, 100, 0, 0, true, true);
        state.Clear();
        state.SelectedUnit.Should().BeNull();
        state.CurrentSnapshot.Should().BeNull();
    }
}

internal sealed class RecordingSpawningDispatcher : ISpawningDispatcher
{
    public List<string> Calls { get; } = new();
    public bool ReturnValue { get; set; } = true;
    public Task<bool> SpawnUnitAsync(string type, int slot, float x, float y, float z, int count, CancellationToken ct)
    {
        Calls.Add($"Spawn({type},{slot},{x:0.0},{y:0.0},{z:0.0},{count})");
        return Task.FromResult(ReturnValue);
    }
}

public sealed class SpawningTabStateTests
{
    [Fact]
    public async Task Spawn_NoType_Rejected()
    {
        var d = new RecordingSpawningDispatcher();
        var fb = new RecordingFeedbackSink();
        var state = new SpawningTabState(d, fb);
        state.FactionSlot = 1;
        state.Count = 1;
        (await state.SpawnAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Spawn_NegativeSlot_Rejected()
    {
        var d = new RecordingSpawningDispatcher();
        var fb = new RecordingFeedbackSink();
        var state = new SpawningTabState(d, fb);
        state.SelectedTypeId = "Rebel_Stormtrooper";
        state.FactionSlot = -1;
        state.Count = 1;
        (await state.SpawnAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Spawn_ZeroCount_Rejected()
    {
        var d = new RecordingSpawningDispatcher();
        var fb = new RecordingFeedbackSink();
        var state = new SpawningTabState(d, fb);
        state.SelectedTypeId = "x-wing";
        state.FactionSlot = 0;
        state.Count = 0;
        (await state.SpawnAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Spawn_ValidInput_Dispatches()
    {
        var d = new RecordingSpawningDispatcher();
        var fb = new RecordingFeedbackSink();
        var state = new SpawningTabState(d, fb);
        state.SelectedTypeId = "TIE_Fighter";
        state.FactionSlot = 0;
        state.PosX = 100; state.PosY = 200; state.PosZ = 300;
        state.Count = 5;
        (await state.SpawnAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("Spawn(TIE_Fighter,0,100.0,200.0,300.0,5)");
    }

    [Fact]
    public void FilteredTypes_EmptyQuery_ReturnsAll()
    {
        var state = new SpawningTabState(new RecordingSpawningDispatcher(), new RecordingFeedbackSink());
        state.SetAvailableTypes(new[] { "x-wing", "TIE_Fighter", "Star_Destroyer" });
        state.FilteredTypes().Should().HaveCount(3);
    }

    [Fact]
    public void FilteredTypes_CaseInsensitiveSubstring()
    {
        var state = new SpawningTabState(new RecordingSpawningDispatcher(), new RecordingFeedbackSink());
        state.SetAvailableTypes(new[] { "x-wing", "TIE_Fighter", "Star_Destroyer", "Y-Wing" });
        state.SearchQuery = "wing";
        var filtered = state.FilteredTypes();
        filtered.Should().HaveCount(2);
        filtered.Should().Contain(new[] { "x-wing", "Y-Wing" });
    }

    [Fact]
    public void FilteredTypes_NoMatches_ReturnsEmpty()
    {
        var state = new SpawningTabState(new RecordingSpawningDispatcher(), new RecordingFeedbackSink());
        state.SetAvailableTypes(new[] { "x-wing", "TIE_Fighter" });
        state.SearchQuery = "millennium";
        state.FilteredTypes().Should().BeEmpty();
    }

    [Fact]
    public void SetAvailableTypes_FiltersBlankEntries()
    {
        var state = new SpawningTabState(new RecordingSpawningDispatcher(), new RecordingFeedbackSink());
        state.SetAvailableTypes(new[] { "x-wing", "", "  ", "TIE_Fighter" });
        state.FilteredTypes().Should().HaveCount(2);
    }
}
