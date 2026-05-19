namespace SwfocTrainer.Tests.Simulator;

/// <summary>
/// Galactic-mode planet. Owner is tracked two ways for compatibility with
/// the editor's wire formats:
/// <list type="bullet">
///   <item><see cref="OwnerSlot"/> — slot index (or -1 unowned). Used by
///     simulator handlers that take int-keyed arguments (Phase B legacy).</item>
///   <item><see cref="OwnerFaction"/> — engine faction token like
///     <c>REBEL</c> / <c>EMPIRE</c>. Used by <c>BridgeGalacticDispatcher</c>'s
///     <c>SWFOC_GetPlanets</c> / <c>ChangePlanetOwner</c> string-arg API.</item>
/// </list>
/// Both are kept in sync by <see cref="FakeGameState.SetPlanetOwner"/>.
/// </summary>
public sealed class FakePlanet
{
    public string Name { get; init; } = string.Empty;
    public int OwnerSlot { get; set; } = -1;
    public string OwnerFaction { get; set; } = string.Empty;
    public bool IsRevealed { get; set; }
    public int Structures { get; set; }
    public int TechLevel { get; set; } = 1;

    public static FakePlanet New(string name, int ownerSlot, bool revealed = false,
        string ownerFaction = "")
        => new()
        {
            Name = name,
            OwnerSlot = ownerSlot,
            OwnerFaction = ownerFaction,
            IsRevealed = revealed,
            Structures = 0,
            TechLevel = 1,
        };
}
