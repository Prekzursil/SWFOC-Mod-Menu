using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// V2 Tab 4 (Inspector). Task #148 — live read of selected unit
/// fields, NO manual pointer entry, refresh every 500ms while attached.
/// Consumes the <see cref="TacticalUnitSelection"/> model from Task
/// #103: the operator picks rows in the Tactical Units DataGrid; the
/// Inspector binds to the live currently-selected row and refreshes
/// its detail fields on a timer.
///
/// The actual timer / dispatcher is App-side; this Core type exposes
/// a single <see cref="RefreshAsync"/> method the timer calls. Tests
/// exercise the refresh logic directly without needing a real timer.
/// </summary>
public sealed class InspectorTabState
{
    private readonly IInspectorDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;

    public InspectorTabState(IInspectorDispatcher dispatcher, IUxFeedbackSink feedback)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        _dispatcher = dispatcher;
        _feedback = feedback;
    }

    /// <summary>The current selected unit (single-selection projection).</summary>
    public TacticalUnitRow? SelectedUnit { get; set; }

    /// <summary>Most recent detail snapshot. Refreshed on a 500ms tick.</summary>
    public InspectorDetailSnapshot? CurrentSnapshot { get; private set; }

    /// <summary>
    /// Clear inspector state. Called when the user empties their
    /// selection in the DataGrid OR when the editor detaches from
    /// the game.
    /// </summary>
    public void Clear()
    {
        SelectedUnit = null;
        CurrentSnapshot = null;
    }

    /// <summary>
    /// Pull a fresh detail snapshot from the bridge. No-op when
    /// SelectedUnit is null. Emits a Warning if the obj_addr is no
    /// longer valid (despawned mid-session) so the operator knows
    /// to re-select.
    /// </summary>
    public async Task<UxFeedback> RefreshAsync(CancellationToken ct = default)
    {
        if (SelectedUnit is null)
        {
            return Emit(UxFeedback.Info("inspector",
                "no unit selected — Inspector idle", "inspector"));
        }

        var addr = SelectedUnit.ObjAddr;
        var snapshot = await _dispatcher.InspectUnitAsync(addr, ct);
        if (snapshot is null)
        {
            CurrentSnapshot = null;
            return Emit(UxFeedback.Warning("inspector",
                $"unit 0x{addr:X} no longer valid — re-select", "inspector"));
        }
        CurrentSnapshot = snapshot;
        return Emit(UxFeedback.Info("inspector",
            $"refreshed unit 0x{addr:X} hull={snapshot.Hull:0.0}", "inspector"));
    }

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

/// <summary>
/// One row of inspector detail. Mirrors what SWFOC_InspectUnit returns
/// from the bridge.
/// </summary>
public sealed record InspectorDetailSnapshot(
    long ObjAddr,
    string TypeName,
    int OwnerSlot,
    float Hull,
    float MaxHull,
    float Shield,
    float MaxShield,
    float Speed,
    float MaxSpeed,
    bool IsHero,
    bool InvulnFlag,
    bool PreventDeath);

public interface IInspectorDispatcher
{
    /// <summary>Returns null when the obj_addr is no longer in the live unit map.</summary>
    Task<InspectorDetailSnapshot?> InspectUnitAsync(long objAddr, CancellationToken ct);
}
