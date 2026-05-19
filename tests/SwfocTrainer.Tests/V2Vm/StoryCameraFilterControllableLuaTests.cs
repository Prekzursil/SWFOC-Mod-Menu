using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.V2Vm;

internal sealed class RecordingStoryDispatcher : IStoryEventsDispatcher
{
    public List<string> Calls { get; } = new();
    public bool ReturnValue { get; set; } = true;
    public Task<bool> FireStoryEventAsync(string id, CancellationToken ct)
    { Calls.Add($"Fire({id})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetStoryFlagAsync(string f, string v, CancellationToken ct)
    { Calls.Add($"Flag({f}={v})"); return Task.FromResult(ReturnValue); }
}

public sealed class StoryEventsTabStateTests
{
    [Fact]
    public async Task FireEvent_NoSelection_Rejected()
    {
        var d = new RecordingStoryDispatcher();
        var s = new StoryEventsTabState(d, new RecordingFeedbackSink());
        (await s.FireEventAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task FireEvent_ValidSelection_Dispatches()
    {
        var d = new RecordingStoryDispatcher();
        var s = new StoryEventsTabState(d, new RecordingFeedbackSink());
        s.SelectedEventId = "INTRO_REBEL";
        (await s.FireEventAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("Fire(INTRO_REBEL)");
    }

    [Fact]
    public async Task SetFlag_RequiresFlagName()
    {
        var d = new RecordingStoryDispatcher();
        var s = new StoryEventsTabState(d, new RecordingFeedbackSink());
        (await s.SetFlagAsync()).Severity.Should().Be(UxSeverity.Error);
        s.FlagName = "level_1_complete"; s.FlagValue = "true";
        (await s.SetFlagAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("Flag(level_1_complete=true)");
    }

    [Fact]
    public void FilteredEvents_FiltersBySubstring()
    {
        var s = new StoryEventsTabState(new RecordingStoryDispatcher(), new RecordingFeedbackSink());
        s.SetAvailableEvents(new[] { "INTRO_REBEL", "INTRO_EMPIRE", "FINALE", "" });
        s.SearchQuery = "INTRO";
        s.FilteredEvents().Should().HaveCount(2);
        s.SearchQuery = "FINALE";
        s.FilteredEvents().Should().HaveCount(1);
    }
}

internal sealed class RecordingCameraDispatcher : ICameraDebugDispatcher
{
    public List<string> Calls { get; } = new();
    public bool ReturnValue { get; set; } = true;
    public string? RawResponse { get; set; } = "OK";
    public Task<bool> SetFreeCamAsync(bool e, CancellationToken ct)
    { Calls.Add($"FreeCam({e})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetCameraPosAsync(float x, float y, float z, CancellationToken ct)
    { Calls.Add($"Pos({x:0.0},{y:0.0},{z:0.0})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetCameraZoomAsync(float z, CancellationToken ct)
    { Calls.Add($"Zoom({z:0.00})"); return Task.FromResult(ReturnValue); }
    public Task<string?> ExecuteRawLuaAsync(string lua, CancellationToken ct)
    { Calls.Add($"Raw({lua})"); return Task.FromResult(RawResponse); }
}

public sealed class CameraDebugTabStateTests
{
    private (CameraDebugTabState s, RecordingCameraDispatcher d, RecordingFeedbackSink fb,
             FeatureToggleCoordinator coord) Build()
    {
        var d = new RecordingCameraDispatcher();
        var fb = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(fb);
        return (new CameraDebugTabState(d, fb, coord), d, fb, coord);
    }

    [Fact]
    public async Task ToggleFreeCam_EnableAndCleanup()
    {
        var (s, d, _, coord) = Build();
        await s.ToggleFreeCamAsync(true);
        coord.IsEnabled("free_cam").Should().BeTrue();
        await coord.CleanupAllAsync();
        d.Calls.Should().ContainInOrder("FreeCam(True)", "FreeCam(False)");
    }

    [Fact]
    public async Task SetCameraPos_Dispatches()
    {
        var (s, d, _, _) = Build();
        s.CamX = 100; s.CamY = 200; s.CamZ = 300;
        (await s.SetCameraPosAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("Pos(100.0,200.0,300.0)");
    }

    [Fact]
    public async Task SetCameraZoom_NonPositiveRejected()
    {
        var (s, d, _, _) = Build();
        s.CamZoom = 0;
        (await s.SetCameraZoomAsync()).Severity.Should().Be(UxSeverity.Error);
        s.CamZoom = -1;
        (await s.SetCameraZoomAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
        s.CamZoom = 2.5f;
        (await s.SetCameraZoomAsync()).Severity.Should().Be(UxSeverity.Success);
    }

    [Fact]
    public async Task SubmitRaw_OkResponse_EmitsWarningSeverity()
    {
        var (s, d, _, _) = Build();
        d.RawResponse = "OK: 42";
        s.RawLuaCommand = "return 42";
        var fb = await s.SubmitRawCommandAsync();
        fb.Severity.Should().Be(UxSeverity.Warning,
            "raw escape hatch always emits Warning even on OK");
        fb.Message.Should().Contain("OK: 42");
    }

    [Fact]
    public async Task SubmitRaw_ErrorResponse_EmitsErrorSeverity()
    {
        var (s, d, _, _) = Build();
        d.RawResponse = "ERR: bad syntax";
        s.RawLuaCommand = "?? bogus";
        (await s.SubmitRawCommandAsync()).Severity.Should().Be(UxSeverity.Error);
    }

    [Fact]
    public async Task SubmitRaw_EmptyCommand_Rejected()
    {
        var (s, d, _, _) = Build();
        s.RawLuaCommand = "";
        (await s.SubmitRawCommandAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }
}

internal sealed class RecordingTacticalListDispatcher : ITacticalUnitsListDispatcher
{
    public IReadOnlyList<TacticalUnitRow> Seed { get; set; } = Array.Empty<TacticalUnitRow>();
    public Task<IReadOnlyList<TacticalUnitRow>> ListTacticalUnitsAsync(CancellationToken ct)
        => Task.FromResult(Seed);
}

public sealed class TacticalUnitsFilterTabStateTests
{
    private TacticalUnitRow Row(long addr, int slot, bool local, bool sel) =>
        new(addr, slot, 100, 0, 0, local, sel);

    [Fact]
    public async Task Refresh_LoadsRows()
    {
        var d = new RecordingTacticalListDispatcher
        {
            Seed = new[] { Row(0x1, 1, true, false), Row(0x2, 0, false, true) }
        };
        var s = new TacticalUnitsFilterTabState(d, new RecordingFeedbackSink());
        await s.RefreshAsync();
        s.Selection.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task Filter_BySlot()
    {
        var d = new RecordingTacticalListDispatcher
        {
            Seed = new[] { Row(0x1, 1, true, false), Row(0x2, 0, false, false), Row(0x3, 1, true, false) }
        };
        var s = new TacticalUnitsFilterTabState(d, new RecordingFeedbackSink());
        await s.RefreshAsync();
        s.FactionSlotFilter = 1;
        s.FilteredRows().Should().HaveCount(2);
        s.FilteredRows().All(r => r.OwnerSlot == 1).Should().BeTrue();
    }

    [Fact]
    public async Task Filter_BySelectedOnly()
    {
        var d = new RecordingTacticalListDispatcher
        {
            Seed = new[] { Row(0x1, 1, true, false), Row(0x2, 0, false, true) }
        };
        var s = new TacticalUnitsFilterTabState(d, new RecordingFeedbackSink());
        await s.RefreshAsync();
        s.SelectedOnlyFilter = true;
        s.FilteredRows().Should().HaveCount(1);
        s.FilteredRows()[0].ObjAddr.Should().Be(0x2L);
    }

    [Fact]
    public async Task Filter_ByText_AddrSubstring()
    {
        var d = new RecordingTacticalListDispatcher
        {
            Seed = new[] { Row(0x1234, 1, true, false), Row(0xABCD, 0, false, false) }
        };
        var s = new TacticalUnitsFilterTabState(d, new RecordingFeedbackSink());
        await s.RefreshAsync();
        s.TextFilter = "AB";
        s.FilteredRows().Should().HaveCount(1);
        s.FilteredRows()[0].ObjAddr.Should().Be(0xABCDL);
    }

    [Fact]
    public async Task Filter_Combined_All3FiltersAndedTogether()
    {
        var d = new RecordingTacticalListDispatcher
        {
            Seed = new[] {
                Row(0xAA, 1, true, true),
                Row(0xAB, 1, true, false),
                Row(0xCD, 0, false, true),
            }
        };
        var s = new TacticalUnitsFilterTabState(d, new RecordingFeedbackSink());
        await s.RefreshAsync();
        s.FactionSlotFilter = 1;
        s.SelectedOnlyFilter = true;
        s.FilteredRows().Should().HaveCount(1);
        s.FilteredRows()[0].ObjAddr.Should().Be(0xAAL);
    }
}

public sealed class ControllableOwnerIndicatorTests
{
    [Fact]
    public void NoSelection_ReturnsNoSelectionState()
    {
        var r = ControllableOwnerIndicator.Resolve(null);
        r.State.Should().Be(ControllableOwnerIndicator.ControllabilityState.NoSelection);
        r.Tooltip.Should().Contain("Pick");
    }

    [Fact]
    public void LocalUnit_ReportsControllable()
    {
        var row = new TacticalUnitRow(0x1, 1, 100, 0, 0, IsLocal: true, IsSelected: true);
        var r = ControllableOwnerIndicator.Resolve(row);
        r.State.Should().Be(ControllableOwnerIndicator.ControllabilityState.Controllable);
        r.Tooltip.Should().Contain("writes are accepted");
    }

    [Fact]
    public void EnemyUnit_ReportsReadOnly()
    {
        var row = new TacticalUnitRow(0x2, 0, 100, 0, 0, IsLocal: false, IsSelected: true);
        var r = ControllableOwnerIndicator.Resolve(row);
        r.State.Should().Be(ControllableOwnerIndicator.ControllabilityState.ReadOnly);
        // Tooltip is the user-facing prose; Label is the marker. Assert
        // on Label for the READ-ONLY signal, Tooltip for the explanation.
        r.Label.Should().Contain("READ-ONLY");
        r.Tooltip.Should().Contain("reject");
    }

    [Fact]
    public void EnemyUnit_BecomesControllable_WithExplicitLocalSlotMatch()
    {
        // Edge case: the IsLocal flag was wrong but the operator
        // knows their slot. ExplicitSlotMatch overrides.
        var row = new TacticalUnitRow(0x2, 7, 100, 0, 0, IsLocal: false, IsSelected: true);
        var r = ControllableOwnerIndicator.Resolve(row, localSlot: 7);
        r.State.Should().Be(ControllableOwnerIndicator.ControllabilityState.Controllable);
    }
}

internal sealed class RecordingLuaPlaygroundDispatcher : ILuaPlaygroundDispatcher
{
    public List<string> Calls { get; } = new();
    public string? Response { get; set; } = "OK: nil";
    public Task<string?> ExecuteLuaAsync(string s, CancellationToken ct)
    { Calls.Add(s); return Task.FromResult(Response); }
}

public sealed class LuaPlaygroundTabStateTests
{
    [Fact]
    public void SaveRecipe_RequiresName()
    {
        var s = new LuaPlaygroundTabState(new RecordingLuaPlaygroundDispatcher(), new RecordingFeedbackSink());
        s.ScriptText = "return 1";
        s.SaveRecipe("").Severity.Should().Be(UxSeverity.Error);
    }

    [Fact]
    public void SaveRecipe_RequiresScript()
    {
        var s = new LuaPlaygroundTabState(new RecordingLuaPlaygroundDispatcher(), new RecordingFeedbackSink());
        s.ScriptText = "";
        s.SaveRecipe("foo").Severity.Should().Be(UxSeverity.Error);
    }

    [Fact]
    public void SaveLoadDelete_RoundTrips()
    {
        var s = new LuaPlaygroundTabState(new RecordingLuaPlaygroundDispatcher(), new RecordingFeedbackSink());
        s.ScriptText = "return SWFOC_GetCredits()";
        s.SaveRecipe("creds").Severity.Should().Be(UxSeverity.Success);
        s.Recipes.Should().ContainKey("creds");

        s.ScriptText = "wiped";
        s.LoadRecipe("creds").Severity.Should().Be(UxSeverity.Info);
        s.ScriptText.Should().Be("return SWFOC_GetCredits()");

        s.DeleteRecipe("creds").Severity.Should().Be(UxSeverity.Success);
        s.Recipes.Should().NotContainKey("creds");
    }

    [Fact]
    public void DeleteRecipe_Missing_ReportsWarning()
    {
        var s = new LuaPlaygroundTabState(new RecordingLuaPlaygroundDispatcher(), new RecordingFeedbackSink());
        s.DeleteRecipe("nope").Severity.Should().Be(UxSeverity.Warning);
    }

    [Fact]
    public async Task RunAsync_OkResponse_EmitsWarning_NotSuccess()
    {
        var d = new RecordingLuaPlaygroundDispatcher { Response = "OK: 12345" };
        var s = new LuaPlaygroundTabState(d, new RecordingFeedbackSink());
        s.ScriptText = "return SWFOC_GetCredits()";
        var fb = await s.RunAsync();
        fb.Severity.Should().Be(UxSeverity.Warning,
            "the playground bypasses validation, so even OK is a 'verify yourself' Warning");
        s.LastResponse.Should().Be("OK: 12345");
    }

    [Fact]
    public async Task RunAsync_ErrResponse_EmitsError()
    {
        var d = new RecordingLuaPlaygroundDispatcher { Response = "ERR: bad" };
        var s = new LuaPlaygroundTabState(d, new RecordingFeedbackSink());
        s.ScriptText = "junk";
        (await s.RunAsync()).Severity.Should().Be(UxSeverity.Error);
    }

    [Fact]
    public async Task RunAsync_EmptyScript_Rejected()
    {
        var d = new RecordingLuaPlaygroundDispatcher();
        var s = new LuaPlaygroundTabState(d, new RecordingFeedbackSink());
        (await s.RunAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }
}
