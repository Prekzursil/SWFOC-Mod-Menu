using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SwfocTrainer.Core.Diagnostics;

/// <summary>
/// Static catalog of unit-ability names recovered from SWFOC's
/// EnumConversionClass&lt;UnitAbilityType&gt; static initializer at RVA 0x5DEA20
/// (image-base-relative). 69 entries extracted via callgraph mining at iter-402.
///
/// Used by UnitControl tab's Activate_Ability button (iter-156 SWFOC_ActivateAbilityLua
/// + iter-173 SWFOC_IsAbilityActiveLua) — operator picks from this dropdown
/// instead of memorizing engine ability strings.
///
/// Names follow SWFOC's underscore_separated_TitleCase convention. Some entries
/// were truncated by IDA's ~14-character symbol-label cap; full forms documented
/// in <c>knowledge-base/iter402_unit_ability_re_kickoff.md</c>.
/// </summary>
public static class KnownUnitAbilityNames
{
    /// <summary>
    /// 69 ability names recovered from EnumConversionClass&lt;UnitAbilityType&gt;
    /// static initializer at RVA 0x5DEA20 (image-base-relative).
    /// Sorted alphabetically for operator scan-ability.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new ReadOnlyCollection<string>(new[]
    {
        "Afterburner",
        "Area_Effect_Conversion",
        "Area_Effect_Heal",
        "Area_Effect_Stun",
        "Avoid_Danger",
        "Berserker",
        "Blast",
        "Buzz_Droids",
        "Cable_Attack",
        "Capture_Vehicle",
        "Cluster_Bomb",
        "Concentrate_Fire",
        "Corrupt_Systems",
        "Deploy",
        "Deploy_Squad",
        "Deploy_Troopers",
        "Detonate_Remote",
        "Distract",
        "Drain_Life",
        "Eject_Vehicle_Thieves",
        "Energy_Weapon",
        "Fire_Lobbing_Support",
        "Flame_Thrower",
        "Force_Cloak",
        "Force_Confuse",
        "Force_Lightning",
        "Force_Sight",
        "Force_Telekinesis",
        "Force_Whirlwind",
        "FOW_Reveal_Ping",
        "Full_Salvo",
        "Harmonic_Bomb",
        "Invulnerability",
        "Ion_Cannon_Shot",
        "Jet_Pack",
        "Laser_Defense",
        "Leech_Shields",
        "Lucky_Shot",
        "Lure",
        "Maximum_Firepower",
        "Missile_Shield",
        "Place_Remote_Bomb",
        "Power_To_Weapons",
        "Proximity_Mines",
        "Radioactive_Contamination",
        "Replenish_Wingmen",
        "Rocket_Attack",
        "Saber_Throw",
        "Self_Destruct",
        "Sensor_Jamming",
        "Shield_Flare",
        "Spoiler_Lock",
        "Spread_Out",
        "Sprint",
        "Stealth",
        "Sticky_Bomb",
        "Stim_Pack",
        "Stun",
        "Summon",
        "Super_Laser",
        "Swap_Weapons",
        "Tactical_Bribe",
        "Targeted_Hack",
        "Targeted_Invulnerability",
        "Targeted_Repair",
        "Tractor_Beam",
        "Turbo",
        "Untargeted_Sticky_Bomb",
        "Weaken_Enemy",
    });
}
