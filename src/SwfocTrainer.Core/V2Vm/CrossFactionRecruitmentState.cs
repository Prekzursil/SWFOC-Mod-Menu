using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// P7 / Task #170 — Cross-Faction Recruitment. Transfer a unit's
/// ownership to a different slot. The SetUnitField generic dispatcher
/// (#157) explicitly excludes owner_slot from the writable surface
/// (it's READ-ONLY there to protect against accidental engine-state
/// corruption); this dedicated helper goes through a separate bridge
/// path that knows how to call the engine's Change_Owner side-effects
/// safely.
///
/// Hard rule reaffirmed: cross-faction recruitment is the ONE write
/// helper allowed to flip a unit's ownership, but it still respects
/// the local-only rule for the SOURCE — you can only recruit units
/// already on your side, transferring to another local-controlled slot
/// (multi-faction control). Recruiting an enemy unit from across the
/// map is REJECTED.
/// </summary>
public sealed class CrossFactionRecruitmentState
{
    private readonly ICrossFactionDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;

    public CrossFactionRecruitmentState(
        ICrossFactionDispatcher dispatcher, IUxFeedbackSink feedback)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        _dispatcher = dispatcher;
        _feedback = feedback;
    }

    public TacticalUnitRow? SourceUnit { get; set; }
    public int TargetSlot { get; set; } = -1;

    public async Task<UxFeedback> RecruitAsync(CancellationToken ct = default)
    {
        if (SourceUnit is null)
        {
            return Emit(UxFeedback.Error("recruit",
                "no source unit selected", "recruit"));
        }
        if (!SourceUnit.IsLocal)
        {
            return Emit(UxFeedback.Error("recruit",
                $"source unit (slot {SourceUnit.OwnerSlot}) is not local — " +
                "cross-faction recruitment requires the source unit to be locally owned. " +
                "READ-ONLY discipline prevents recruiting enemy units.",
                "recruit"));
        }
        if (TargetSlot < 0)
        {
            return Emit(UxFeedback.Error("recruit",
                $"target slot must be >= 0, got {TargetSlot}", "recruit"));
        }
        if (TargetSlot == SourceUnit.OwnerSlot)
        {
            return Emit(UxFeedback.Warning("recruit",
                $"target slot {TargetSlot} == source slot — no-op", "recruit"));
        }
        var ok = await _dispatcher.TransferOwnershipAsync(
            SourceUnit.ObjAddr, TargetSlot, ct);
        return Emit(ok
            ? UxFeedback.Success("recruit",
                $"unit 0x{SourceUnit.ObjAddr:X} transferred from slot {SourceUnit.OwnerSlot} → slot {TargetSlot}",
                "recruit")
            : UxFeedback.Error("recruit", "bridge rejected", "recruit"));
    }

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

public interface ICrossFactionDispatcher
{
    Task<bool> TransferOwnershipAsync(long objAddr, int targetSlot, CancellationToken ct);
}
