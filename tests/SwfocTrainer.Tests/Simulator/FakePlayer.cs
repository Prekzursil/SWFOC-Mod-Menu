namespace SwfocTrainer.Tests.Simulator;

/// <summary>
/// Mirror of the runtime PlayerObject struct fields the editor cares
/// about. Field names match the live engine's offsets where it makes the
/// test-vs-engine mapping clearer:
/// <list type="bullet">
///   <item><c>Slot</c> — index in the engine's player array (0..7).</item>
///   <item><c>Faction</c> — engine faction token (REBEL / EMPIRE / UNDERWORLD / mod).</item>
///   <item><c>Credits</c> — galactic-mode credit balance (mirrors <c>PlayerObject+0x80</c>).</item>
///   <item><c>IsHuman</c> — set when SetHumanPlayer flips this slot to operator control.</item>
///   <item><c>IsLocal</c> — engine's "this is the local console's player" flag.</item>
///   <item><c>HasAiBrain</c> — true when an AI controller is attached (the dual-control bug).</item>
/// </list>
/// </summary>
public sealed class FakePlayer
{
    public int Slot { get; init; }
    public string Faction { get; set; } = "NONE";
    public int Credits { get; set; }
    public bool IsHuman { get; set; }
    public bool IsLocal { get; set; }
    public bool HasAiBrain { get; set; }

    /// <summary>
    /// Creates a fresh slot with sensible defaults for the test harness.
    /// </summary>
    public static FakePlayer NewAiSlot(int slot, string faction)
        => new()
        {
            Slot = slot,
            Faction = faction,
            Credits = 5000,
            IsHuman = false,
            IsLocal = false,
            HasAiBrain = true,
        };

    /// <summary>
    /// Creates the initial human-controlled slot.
    /// </summary>
    public static FakePlayer NewLocalHumanSlot(int slot, string faction)
        => new()
        {
            Slot = slot,
            Faction = faction,
            Credits = 5000,
            IsHuman = true,
            IsLocal = true,
            HasAiBrain = false,
        };
}
