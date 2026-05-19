namespace SwfocTrainer.Tests.Simulator;

/// <summary>
/// Single unit instance in the simulated world. Maps loosely to the
/// engine's <c>GameObject</c> with the trainer-relevant fields surfaced.
/// </summary>
public sealed class FakeUnit
{
    private static int s_nextId;

    /// <summary>
    /// Stable per-process unit id. The engine uses 0x140-byte
    /// GameObject pointers as identity; tests just need a small int.
    /// </summary>
    public int Id { get; } = System.Threading.Interlocked.Increment(ref s_nextId);

    /// <summary>
    /// XML object-type name (e.g. <c>Rebel_Trooper_Squad</c>).
    /// </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>
    /// Owning slot (matches <see cref="FakePlayer.Slot"/>).
    /// </summary>
    public int OwnerSlot { get; set; }

    public float MaxHull { get; set; } = 100f;
    public float CurrentHull { get; set; } = 100f;
    public float MaxShield { get; set; } = 0f;
    public float CurrentShield { get; set; } = 0f;
    public float Speed { get; set; } = 100f;
    public float MaxSpeed { get; set; } = 100f;
    public float DamageScalar { get; set; } = 1f;
    public float ShieldScalar { get; set; } = 1f;
    public float FireRateScalar { get; set; } = 1f;

    /// <summary>
    /// True after <c>SWFOC_SetUnitInvuln(id, 1)</c>. While set, attempts to
    /// reduce <see cref="CurrentHull"/> via <see cref="ApplyDamage"/> are no-ops.
    /// </summary>
    public bool Invulnerable { get; set; }

    /// <summary>
    /// True after <c>SWFOC_PreventUnitDeath(id, 1)</c>. While set, hull
    /// can drop to 1 but never below — the unit will not die from damage.
    /// </summary>
    public bool DeathPrevented { get; set; }

    /// <summary>
    /// True for hero objects (Hero Lab tab). Mirrors the engine's
    /// <c>IsHero</c> object-type flag.
    /// </summary>
    public bool IsHero { get; set; }

    /// <summary>
    /// True for ground units, false for space units. Used by Spawn tab
    /// faction/category filters in the live game.
    /// </summary>
    public bool IsGround { get; set; }

    /// <summary>
    /// 2026-04-27 (iter 32) — galactic-mode planet anchor. Empty string for
    /// units that aren't currently stationed on a planet (e.g. fleets in
    /// transit, tactical-mode units). The planet-flip handler reads this
    /// to decide which units it affects when ownership changes.
    /// </summary>
    public string OnPlanet { get; set; } = string.Empty;

    /// <summary>
    /// Engine alive flag. Tests assert this transitions on Kill / Revive.
    /// </summary>
    public bool Alive { get; set; } = true;

    /// <summary>
    /// Apply damage with respect to the simulated invulnerability /
    /// prevent-death flags. Returns the actual damage applied (0 when
    /// fully blocked, less than requested when prevent-death capped it).
    /// </summary>
    public float ApplyDamage(float amount)
    {
        if (!Alive) return 0f;
        if (Invulnerable) return 0f;
        var newHull = CurrentHull - amount;
        if (DeathPrevented && newHull < 1f)
        {
            var actual = CurrentHull - 1f;
            CurrentHull = 1f;
            return actual;
        }
        if (newHull <= 0f)
        {
            var actual = CurrentHull;
            CurrentHull = 0f;
            Alive = false;
            return actual;
        }
        CurrentHull = newHull;
        return amount;
    }

    public void Revive()
    {
        Alive = true;
        CurrentHull = MaxHull;
        CurrentShield = MaxShield;
    }
}
