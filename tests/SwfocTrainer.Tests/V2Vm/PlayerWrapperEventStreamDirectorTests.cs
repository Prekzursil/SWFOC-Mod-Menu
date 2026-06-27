using FluentAssertions;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.V2Vm;

public sealed class PlayerWrapperBuilderTests
{
    [Fact]
    public void Build_LocalSlot_FlagsIsLocalTrue()
    {
        var snapshot = PlayerWrapperBuilder.Build(
            slot: 1, faction: "REBEL", credits: 12345.0, techLevel: 4,
            isHuman: true, localSlot: 1, unitCount: 8,
            capturedPlanets: new[] { "Naboo", "Tatooine" });
        snapshot.IsLocal.Should().BeTrue();
        snapshot.Faction.Should().Be("REBEL");
        snapshot.UnitCount.Should().Be(8);
        snapshot.CapturedPlanets.Should().HaveCount(2);
    }

    [Fact]
    public void Build_DifferentSlot_FlagsIsLocalFalse()
    {
        var snapshot = PlayerWrapperBuilder.Build(
            slot: 2, faction: "EMPIRE", credits: 0, techLevel: 5,
            isHuman: false, localSlot: 1, unitCount: 0,
            capturedPlanets: Array.Empty<string>());
        snapshot.IsLocal.Should().BeFalse();
    }

    [Fact]
    public void Build_NegativeSlot_NeverLocal()
    {
        var snapshot = PlayerWrapperBuilder.Build(
            slot: -1, faction: "?", credits: 0, techLevel: 1,
            isHuman: false, localSlot: -1, unitCount: 0,
            capturedPlanets: Array.Empty<string>());
        snapshot.IsLocal.Should().BeFalse();
    }
}

internal sealed class RecordingEventStreamDispatcher : IEventStreamDispatcher
{
    public List<DamageEventRow> Seed { get; set; } = new();
    public Task<IReadOnlyList<DamageEventRow>> DrainEventStreamAsync(CancellationToken ct)
    {
        var snapshot = Seed.ToList();
        Seed.Clear();
        return Task.FromResult<IReadOnlyList<DamageEventRow>>(snapshot);
    }
}

public sealed class EventStreamViewStateTests
{
    [Fact]
    public async Task Drain_NoEvents_EmitsInfoNoNew()
    {
        var d = new RecordingEventStreamDispatcher();
        var s = new EventStreamViewState(d, new RecordingFeedbackSink());
        var fb = await s.DrainAsync();
        fb.Severity.Should().Be(UxSeverity.Info);
        fb.Message.Should().Contain("no new events");
    }

    [Fact]
    public async Task Drain_AccumulatesEvents()
    {
        var d = new RecordingEventStreamDispatcher();
        d.Seed.Add(new DamageEventRow(1000, 0xAA, 1, 100, 90));
        d.Seed.Add(new DamageEventRow(1100, 0xBB, 0, 50, 40));
        var s = new EventStreamViewState(d, new RecordingFeedbackSink());
        await s.DrainAsync();
        s.Events.Should().HaveCount(2);
        d.Seed.Add(new DamageEventRow(1200, 0xCC, 1, 200, 200));
        await s.DrainAsync();
        s.Events.Should().HaveCount(3);
    }

    [Fact]
    public async Task FilteredEvents_ByOwnerSlot()
    {
        var d = new RecordingEventStreamDispatcher();
        d.Seed.Add(new DamageEventRow(1, 0xA, 1, 100, 90));
        d.Seed.Add(new DamageEventRow(2, 0xB, 0, 50, 40));
        var s = new EventStreamViewState(d, new RecordingFeedbackSink());
        await s.DrainAsync();
        s.OwnerSlotFilter = 1;
        s.FilteredEvents().Should().HaveCount(1);
        s.FilteredEvents()[0].ObjAddr.Should().Be(0xAL);
    }

    [Fact]
    public async Task FilteredEvents_GodModeClampsOnly()
    {
        var d = new RecordingEventStreamDispatcher();
        // current_hp > requested_hp == god mode clamped (didn't actually take damage)
        d.Seed.Add(new DamageEventRow(1, 0xA, 1, 50, 100));
        d.Seed.Add(new DamageEventRow(2, 0xB, 1, 100, 90));
        var s = new EventStreamViewState(d, new RecordingFeedbackSink());
        await s.DrainAsync();
        s.ShowGodModeClampsOnly = true;
        s.FilteredEvents().Should().HaveCount(1);
        s.FilteredEvents()[0].ObjAddr.Should().Be(0xAL);
    }

    [Fact]
    public async Task ClampsAtMaxBufferRows_DropsOldestFirst()
    {
        var d = new RecordingEventStreamDispatcher();
        // Push 5500 events to verify the 5000-cap.
        for (var i = 0; i < 5500; i++)
        {
            d.Seed.Add(new DamageEventRow((long)i, 0xA, 1, 100, 90));
        }
        var s = new EventStreamViewState(d, new RecordingFeedbackSink());
        await s.DrainAsync();
        s.Events.Should().HaveCount(5000);
        s.Events[0].TimestampMs.Should().Be(500L,
            "the first 500 events were dropped to keep at most 5000");
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var d = new RecordingEventStreamDispatcher();
        var s = new EventStreamViewState(d, new RecordingFeedbackSink());
        s.Clear();
        s.Events.Should().BeEmpty();
    }
}

internal sealed class RecordingDirectorDispatcher : IDirectorDispatcher
{
    public List<string> Calls { get; } = new();
    public bool ReturnValue { get; set; } = true;
    public Task<bool> SetUiVisibleAsync(bool v, CancellationToken ct)
    { Calls.Add($"Ui({v})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetGameSpeedAsync(float s, CancellationToken ct)
    { Calls.Add($"Speed({s:0.00})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetCameraPosAsync(float x, float y, float z, CancellationToken ct)
    { Calls.Add($"Pos({x:0.0},{y:0.0},{z:0.0})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetCameraZoomAsync(float z, CancellationToken ct)
    { Calls.Add($"Zoom({z:0.00})"); return Task.FromResult(ReturnValue); }
}

public sealed class DirectorModeStateTests
{
    private (DirectorModeState s, RecordingDirectorDispatcher d, RecordingFeedbackSink fb,
             FeatureToggleCoordinator coord) Build()
    {
        var d = new RecordingDirectorDispatcher();
        var fb = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(fb);
        return (new DirectorModeState(d, fb, coord), d, fb, coord);
    }

    [Fact]
    public void AddWaypoint_RequiresName()
    {
        var (s, _, _, _) = Build();
        s.AddWaypoint(new CameraWaypoint("", 0, 0, 0, 0, 1, 1000)).Severity.Should().Be(UxSeverity.Error);
        s.Path.Should().BeEmpty();
    }

    [Fact]
    public void AddWaypoint_NegativeDurationRejected()
    {
        var (s, _, _, _) = Build();
        s.AddWaypoint(new CameraWaypoint("ok", 0, 0, 0, 0, 1, -1)).Severity.Should().Be(UxSeverity.Error);
    }

    [Fact]
    public void AddRemoveWaypoint_ManipulatesPath()
    {
        var (s, _, _, _) = Build();
        s.AddWaypoint(new CameraWaypoint("a", 1, 2, 3, 0, 1, 1000)).Severity.Should().Be(UxSeverity.Success);
        s.AddWaypoint(new CameraWaypoint("b", 4, 5, 6, 0, 1, 500)).Severity.Should().Be(UxSeverity.Success);
        s.Path.Should().HaveCount(2);
        s.RemoveWaypoint(0).Severity.Should().Be(UxSeverity.Info);
        s.Path.Should().HaveCount(1);
        s.Path[0].Name.Should().Be("b");
    }

    [Fact]
    public void RemoveWaypoint_OutOfRangeRejected()
    {
        var (s, _, _, _) = Build();
        s.RemoveWaypoint(0).Severity.Should().Be(UxSeverity.Error);
        s.RemoveWaypoint(-1).Severity.Should().Be(UxSeverity.Error);
    }

    [Fact]
    public void ClearPath_RemovesAll()
    {
        var (s, _, _, _) = Build();
        s.AddWaypoint(new CameraWaypoint("a", 0, 0, 0, 0, 1, 1));
        s.ClearPath().Severity.Should().Be(UxSeverity.Info);
        s.Path.Should().BeEmpty();
    }

    [Fact]
    public async Task ToggleHideUi_EnableAndCleanup()
    {
        var (s, d, _, coord) = Build();
        await s.ToggleHideUiAsync(true);
        coord.IsEnabled("director.hide_ui").Should().BeTrue();
        await coord.CleanupAllAsync();
        d.Calls.Should().ContainInOrder("Ui(False)", "Ui(True)");
    }

    [Theory]
    [InlineData(0.0f, "freeze-frame")]
    [InlineData(0.25f, "slow-mo")]
    [InlineData(1.0f, "real-time")]
    [InlineData(2.0f, "fast-forward")]
    public async Task SetTimeScale_LabelsByRange(float scale, string expectedLabel)
    {
        var (s, _, _, _) = Build();
        var fb = await s.SetTimeScaleAsync(scale);
        fb.Severity.Should().Be(UxSeverity.Success);
        fb.Message.Should().Contain(expectedLabel);
    }

    [Fact]
    public async Task SetTimeScale_NegativeRejected()
    {
        var (s, _, _, _) = Build();
        (await s.SetTimeScaleAsync(-1)).Severity.Should().Be(UxSeverity.Error);
    }

    [Fact]
    public async Task StartPlayback_EmptyPath_Warning()
    {
        var (s, _, _, _) = Build();
        var fb = await s.StartPlaybackAsync();
        fb.Severity.Should().Be(UxSeverity.Warning);
        s.IsPlaybackRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Playback_StepsThroughPath_ToCompletion()
    {
        var (s, d, _, _) = Build();
        s.AddWaypoint(new CameraWaypoint("a", 1, 2, 3, 0, 1.5f, 100));
        s.AddWaypoint(new CameraWaypoint("b", 10, 20, 30, 0, 2, 200));

        await s.StartPlaybackAsync();   // hits waypoint 0 (a)
        s.IsPlaybackRunning.Should().BeTrue();
        s.CurrentWaypointIndex.Should().Be(1);

        await s.StepPlaybackAsync();    // hits waypoint 1 (b)
        s.CurrentWaypointIndex.Should().Be(2);

        var doneFb = await s.StepPlaybackAsync();   // index >= count → complete
        doneFb.Severity.Should().Be(UxSeverity.Success);
        doneFb.Message.Should().Contain("complete");
        s.IsPlaybackRunning.Should().BeFalse();
        d.Calls.Should().Contain(new[] {
            "Pos(1.0,2.0,3.0)", "Zoom(1.50)",
            "Pos(10.0,20.0,30.0)", "Zoom(2.00)" });
    }

    [Fact]
    public async Task StopPlayback_ResetsState()
    {
        var (s, _, _, _) = Build();
        s.AddWaypoint(new CameraWaypoint("a", 0, 0, 0, 0, 1, 100));
        await s.StartPlaybackAsync();
        s.StopPlayback();
        s.IsPlaybackRunning.Should().BeFalse();
        s.CurrentWaypointIndex.Should().Be(0);
    }
}
