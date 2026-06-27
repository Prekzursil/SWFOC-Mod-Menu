namespace SwfocTrainer.Savegame;

/// <summary>
/// The outcome of a <see cref="ModHashValidator"/> check of a save's embedded
/// mod hash against a hash freshly computed from the current mod.
/// </summary>
public enum ModHashStatus
{
    /// <summary>The embedded hash equals the freshly computed mod hash — the save matches its mod.</summary>
    Match,

    /// <summary>
    /// The embedded hash differs from the freshly computed mod hash — the mod
    /// changed since the save was written. Re-anchor the save to recover it.
    /// </summary>
    Mismatch,

    /// <summary>
    /// The save carries no embedded mod hash — its 0x3E8 mod-context chunk has
    /// no type-0x01 int32 slot — so it can be neither validated nor re-anchored.
    /// </summary>
    NoEmbeddedHash,
}
