using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// V2 Diagnostics — controllable-owner indicator. Task #109 surfaces
/// "this unit IS / IS NOT writable by you" in the V2 status pane,
/// based on the same READ-ONLY rule the bridge uses.
///
/// A unit is controllable when either:
///   * IsLocal is true (the unit belongs to the local slot), OR
///   * the unit's owner_slot equals the explicit local-slot value
///     supplied to <see cref="Resolve"/>.
///
/// This is a pure-function module with no I/O — perfect for unit
/// tests and direct binding from the V2 Inspector pane.
/// </summary>
public static class ControllableOwnerIndicator
{
    public enum ControllabilityState
    {
        /// <summary>No unit selected.</summary>
        NoSelection,
        /// <summary>Unit is writable (local-owned).</summary>
        Controllable,
        /// <summary>Unit is enemy/neutral — writes will be rejected.</summary>
        ReadOnly,
    }

    public sealed record Result(ControllabilityState State, string Label, string Tooltip);

    public static Result Resolve(TacticalUnitRow? row, int? localSlot = null)
    {
        if (row is null)
        {
            return new Result(
                ControllabilityState.NoSelection,
                "(no selection)",
                "Pick a unit in the Tactical Units tab to see whether you can write to it.");
        }
        var ownedByLocalFlag = row.IsLocal;
        var ownedByExplicitSlot = localSlot is int slot && row.OwnerSlot == slot;
        var controllable = ownedByLocalFlag || ownedByExplicitSlot;
        return controllable
            ? new Result(
                ControllabilityState.Controllable,
                $"Controllable (slot {row.OwnerSlot})",
                $"Unit 0x{row.ObjAddr:X} is owned by your slot — writes are accepted.")
            : new Result(
                ControllabilityState.ReadOnly,
                $"READ-ONLY (slot {row.OwnerSlot})",
                $"Unit 0x{row.ObjAddr:X} belongs to slot {row.OwnerSlot}, not your local slot. " +
                "The bridge will reject any write helper. Inspect-only.");
    }
}
