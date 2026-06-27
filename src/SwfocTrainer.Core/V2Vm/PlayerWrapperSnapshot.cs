namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// P4 / Task #156 — Phase 1 offline mirror of what
/// <c>SWFOC_GetPlayerWrapper(slot)</c> would expose if the bridge had
/// the live PlayerWrapper userdata constructor wired. Provides the
/// data shape that V2 tabs / scripts can bind against today, ahead of
/// the engine call landing.
///
/// Phase 2 wires this through the bridge once the
/// <c>PlayerWrapper_Create @ 0x6019F0</c> RVA is verified.
/// </summary>
public sealed record PlayerWrapperSnapshot(
    int Slot,
    string Faction,
    double Credits,
    int TechLevel,
    bool IsHuman,
    bool IsLocal,
    int UnitCount,
    IReadOnlyList<string> CapturedPlanets);

/// <summary>
/// Builds a <see cref="PlayerWrapperSnapshot"/> from the existing
/// per-slot data sources (GetAllPlayers + GetPlanets) without needing
/// the live PlayerWrapper userdata. Phase 1: synthesises the snapshot
/// from raw bridge readings; Phase 2 will replace this with a single
/// SWFOC_GetPlayerWrapper round-trip.
/// </summary>
public static class PlayerWrapperBuilder
{
    public static PlayerWrapperSnapshot Build(
        int slot,
        string faction,
        double credits,
        int techLevel,
        bool isHuman,
        int localSlot,
        int unitCount,
        IEnumerable<string> capturedPlanets)
    {
        ArgumentNullException.ThrowIfNull(faction);
        ArgumentNullException.ThrowIfNull(capturedPlanets);
        return new PlayerWrapperSnapshot(
            Slot: slot,
            Faction: faction,
            Credits: credits,
            TechLevel: techLevel,
            IsHuman: isHuman,
            IsLocal: slot >= 0 && slot == localSlot,
            UnitCount: unitCount,
            CapturedPlanets: capturedPlanets.ToList());
    }
}
