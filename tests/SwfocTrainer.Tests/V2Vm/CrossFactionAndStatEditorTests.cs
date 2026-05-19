using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.V2Vm;

internal sealed class RecordingCrossFactionDispatcher : ICrossFactionDispatcher
{
    public List<string> Calls { get; } = new();
    public bool ReturnValue { get; set; } = true;
    public Task<bool> TransferOwnershipAsync(long addr, int slot, CancellationToken ct)
    { Calls.Add($"Transfer(0x{addr:X}→{slot})"); return Task.FromResult(ReturnValue); }
}

public sealed class CrossFactionRecruitmentStateTests
{
    private TacticalUnitRow Local(long addr, int slot) =>
        new(addr, slot, 100, 0, 0, IsLocal: true, IsSelected: true);
    private TacticalUnitRow Enemy(long addr, int slot) =>
        new(addr, slot, 100, 0, 0, IsLocal: false, IsSelected: true);

    [Fact]
    public async Task Recruit_NoSource_Rejected()
    {
        var d = new RecordingCrossFactionDispatcher();
        var s = new CrossFactionRecruitmentState(d, new RecordingFeedbackSink());
        s.TargetSlot = 1;
        (await s.RecruitAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Recruit_EnemySource_RejectedByReadOnlyRule()
    {
        var d = new RecordingCrossFactionDispatcher();
        var s = new CrossFactionRecruitmentState(d, new RecordingFeedbackSink());
        s.SourceUnit = Enemy(0xDEAD, 0);
        s.TargetSlot = 1;
        var fb = await s.RecruitAsync();
        fb.Severity.Should().Be(UxSeverity.Error);
        fb.Message.Should().Contain("READ-ONLY");
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Recruit_NegativeTarget_Rejected()
    {
        var d = new RecordingCrossFactionDispatcher();
        var s = new CrossFactionRecruitmentState(d, new RecordingFeedbackSink());
        s.SourceUnit = Local(0xAAAA, 1);
        s.TargetSlot = -1;
        (await s.RecruitAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Recruit_SameSourceAndTarget_NoOpWarning()
    {
        var d = new RecordingCrossFactionDispatcher();
        var s = new CrossFactionRecruitmentState(d, new RecordingFeedbackSink());
        s.SourceUnit = Local(0xAAAA, 1);
        s.TargetSlot = 1;
        var fb = await s.RecruitAsync();
        fb.Severity.Should().Be(UxSeverity.Warning);
        fb.Message.Should().Contain("no-op");
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Recruit_LocalToOtherSlot_Dispatches()
    {
        var d = new RecordingCrossFactionDispatcher();
        var s = new CrossFactionRecruitmentState(d, new RecordingFeedbackSink());
        s.SourceUnit = Local(0xAAAA, 1);
        s.TargetSlot = 2;
        (await s.RecruitAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("Transfer(0xAAAA→2)");
    }
}

internal sealed class RecordingStatEditDispatcher : IUnitStatEditDispatcher
{
    public List<string> Calls { get; } = new();
    public HashSet<string> FailingFields { get; } = new();
    public Task<bool> SetUnitFieldAsync(long addr, string f, float v, CancellationToken ct)
    {
        Calls.Add($"Set(0x{addr:X},{f},{v})");
        return Task.FromResult(!FailingFields.Contains(f));
    }
}

public sealed class UnitStatEditorStateTests
{
    private (UnitStatEditorState st, RecordingStatEditDispatcher d, RecordingFeedbackSink fb,
             TacticalUnitSelection sel) Build()
    {
        var d = new RecordingStatEditDispatcher();
        var fb = new RecordingFeedbackSink();
        var sel = new TacticalUnitSelection();
        return (new UnitStatEditorState(d, fb, sel), d, fb, sel);
    }

    [Fact]
    public void StageEdit_RequiresFieldName()
    {
        var (s, _, _, _) = Build();
        s.StageEdit("", 100).Severity.Should().Be(UxSeverity.Error);
        s.PendingEdits.Should().BeEmpty();
    }

    [Fact]
    public void StageEdit_DeDupsByFieldName()
    {
        var (s, _, _, _) = Build();
        s.StageEdit("hull", 100);
        s.StageEdit("hull", 200);   // overrides
        s.StageEdit("HULL", 300);   // case-insensitive, overrides again
        s.PendingEdits.Should().HaveCount(1);
        s.PendingEdits[0].Value.Should().Be(300);
    }

    [Fact]
    public async Task ApplyAll_NoEdits_Warning()
    {
        var (s, d, _, _) = Build();
        (await s.ApplyAllAsync()).Severity.Should().Be(UxSeverity.Warning);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAll_NoSelection_Error()
    {
        var (s, d, _, _) = Build();
        s.StageEdit("hull", 100);
        (await s.ApplyAllAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAll_OnlyEnemySelection_Rejected()
    {
        var (s, d, _, sel) = Build();
        sel.LoadRows(new[] {
            new TacticalUnitRow(0x1, 0, 100, 0, 0, IsLocal: false, IsSelected: true)
        });
        s.StageEdit("hull", 100);
        var fb = await s.ApplyAllAsync();
        fb.Severity.Should().Be(UxSeverity.Error);
        fb.Message.Should().Contain("READ-ONLY");
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAll_AllSucceed_FullSuccess()
    {
        var (s, d, _, sel) = Build();
        sel.LoadRows(new[] {
            new TacticalUnitRow(0x1, 1, 100, 0, 0, true, true),
            new TacticalUnitRow(0x2, 1, 100, 0, 0, true, true),
        });
        s.StageEdit("hull", 9999);
        s.StageEdit("max_hull", 9999);
        var fb = await s.ApplyAllAsync();
        fb.Severity.Should().Be(UxSeverity.Success);
        fb.Message.Should().Contain("4 edits across 2 units");  // 2 units × 2 edits
        d.Calls.Should().HaveCount(4);
    }

    [Fact]
    public async Task ApplyAll_PartialFailure_EmitsWarningWithDetails()
    {
        var (s, d, _, sel) = Build();
        sel.LoadRows(new[] {
            new TacticalUnitRow(0x1, 1, 100, 0, 0, true, true),
            new TacticalUnitRow(0x2, 1, 100, 0, 0, true, true),
        });
        s.StageEdit("hull", 100);
        s.StageEdit("bogus_field", 0);    // dispatcher will fail on this one
        d.FailingFields.Add("bogus_field");

        var fb = await s.ApplyAllAsync();
        fb.Severity.Should().Be(UxSeverity.Warning);
        fb.Message.Should().Contain("2/4 succeeded");
        fb.Message.Should().Contain("0x1.bogus_field");
        fb.Message.Should().Contain("0x2.bogus_field");
    }

    [Fact]
    public async Task ApplyAll_AllFail_EmitsError()
    {
        var (s, d, _, sel) = Build();
        sel.LoadRows(new[] {
            new TacticalUnitRow(0x1, 1, 100, 0, 0, true, true),
        });
        s.StageEdit("hull", 100);
        d.FailingFields.Add("hull");
        (await s.ApplyAllAsync()).Severity.Should().Be(UxSeverity.Error);
    }

    [Fact]
    public void ClearStaged_EmptiesList()
    {
        var (s, _, _, _) = Build();
        s.StageEdit("hull", 100);
        s.StageEdit("shield", 50);
        s.ClearStaged();
        s.PendingEdits.Should().BeEmpty();
    }
}
