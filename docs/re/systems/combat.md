# Combat


### Damage Pipeline Stages

- Weapon Fire
- Invulnerability Gate
- Damage Routing
- HP Subtraction
- SetHP

**Sethp Rva**: 0x3A89D0

**Sethp Callers Count**: 18

**Max Health Formula**: base_hp(type+0xDCC) * difficulty_mult * player_mult * (1.0 + ability_bonus)


### Shield System

| Key | Value |
|-----|-------|
| front | {'qi_id': '0x0F', 'set': '0x3A8630', 'storage': '[obj+0xF0]+0xF8', 'max_from': 'type+0xDD0'} |
| rear | {'qi_id': '0x10', 'set': '0x3A91E0', 'storage': '[obj+0xF0]+0xFC', 'max_from': 'type+0xDD4'} |

**Invulnerability Flag**: obj+0x3A7

**Prevent Death Flag**: obj+0x3A1 bit 7


### Damage Types

| Key | Value |
|-----|-------|
| 0x00 | Normal |
| 0x06 | Special(Berserker check) |
| 0x08 | Fire/Burn |
| 0x68 | Berserker/Eat |
| 0x74 | Construction |

**Special Abilities Count**: 91


### Key Abilities

- CombatBonus(8 stats)
- AbsorbBlaster
- Berserker
- DrainLife
- ConcentrateFire
- MaximumFirepower
- LuckyShot
- TractorBeam
- CableAttack
- EatAttack


## RE Findings Detail

**Analysis**: Alamo Engine Combat System — Complete Damage Pipeline

Full mapping of the SWFOC damage pipeline from weapon fire to unit death. Derived from Ghidra static analysis of StarWarsG.exe (x86_64). All addresses are Ghidra VA = imagebase 0x140000000 + RVA.

- **Date**: 2026-04-04
- **Analyst**: Agent 3C (Combat System Deep Dive)


### Damage Pipeline Overview

- **description**: The complete flow from a weapon firing to a unit dying. Every HP modification in the engine funnels through SetHP (0x3A89D0). The combat damage path specifically goes through a 5-stage pipeline.
- **stages**: {'stage': 1, 'name': 'Weapon Fire / Attack Command', 'description': 'CombatantBehaviorClass dispatches attack orders. The targeting system (BaseCombatantClass vtable[34] SelectTarget) evaluates targets by range, health ratio, and weapon bitmask (target_type+0x1648). FUN_14038D730 is the master fire-control function: checks enemy status, invulnerability, LOS, game mode, and dispatches to hardpoint or direct weapon fire.', 'key_functions': {'SelectTarget': {'rva': '0x3A97E0_vicinity', 'vtable_slot': 34, 'class': 'BaseCombatantClass'}, 'FireControl_Dispatch': {'rva': '0x38D730', 'ghidra_va': '0x14038D730'}, 'WeaponTick': {'rva': '0x387010', 'ghidra_va': '0x140387010'}, 'HardpointFire': {'rva': '0x387F50', 'ghidra_va': '0x140387F50'}}}, {'stage': 2, 'name': 'Invulnerability Gate (Take_Damage_Outer)', 'description': 'Top-level damage entry point. Contains 8 invulnerability checks (one per damage path / hardpoint slot). This is the ONLY function that checks the invulnerability flag at obj+0x3A7. Also checks flags at obj+0x3A0 bits 1 and 6, and container sub-flags at obj+0x381, 0x382, 0x388. If invulnerable, damage is silently discarded.', 'key_functions': {'Take_Damage_Outer': {'rva': '0x38A350', 'ghidra_va': '0x14038A350'}}, 'invulnerability_checks': {'primary_flag': 'obj+0x3A7 (1 = invulnerable, 0 = vulnerable)', 'status_flags': 'obj+0x3A0 bits 1,6 (0x42 mask — blocks damage when set)', 'container_flags': ['obj+0x381', 'obj+0x382', 'obj+0x388'], 'locomotor_check': 'obj+0xA8 -> locomotor+0x2A8 (== 1 blocks damage)', 'note': 'Only blocks damage routed through Take_Damage_Outer. Lua Set_Hull, health regen, script/ability HP sets all bypass this entirely.'}}, {'stage': 3, 'name': 'Damage Routing (Property Dispatch + Shield Absorption)', 'description': 'Damage is routed based on object structure. Objects with hardpoints (obj+0x348 != 0xFF) route through the HardpointManager (QueryInterface 0x16). Objects with shields route through front-shield (QueryInterface 0x0F) or rear-shield (QueryInterface 0x10). Shield absorption reduces damage before it reaches hull HP.', 'key_functions': {'Take_Damage_PropertyDispatch': {'rva': '0x3A97E0', 'ghidra_va': '0x1403A97E0'}, 'SetFrontShield': {'rva': '0x3A8630', 'ghidra_va': '0x1403A8630'}, 'SetRearShield': {'rva': '0x3A91E0', 'ghidra_va': '0x1403A91E0'}, 'FrontShield_Write': {'rva': '0x56C1B0', 'ghidra_va': '0x14056C1B0'}, 'FrontShield_Read': {'rva': '0x56BFB0', 'ghidra_va': '0x14056BFB0'}, 'RearShield_Write': {'rva': '0x549810', 'ghidra_va': '0x140549810'}, 'RearShield_Read': {'rva': '0x549490', 'ghidra_va': '0x140549490'}, 'HardpointCount': {'rva': '0x405300', 'ghidra_va': '0x140405300'}, 'GetHardpoint': {'rva': '0x4052D0', 'ghidra_va': '0x1404052D0'}, 'DamageVisualLevel': {'rva': '0x3AC290', 'ghidra_va': '0x1403AC290'}, 'AnimationDispatch': {'rva': '0x3A92F0', 'ghidra_va': '0x1403A92F0'}}}, {'stage': 4, 'name': 'HP Subtraction (Take_Damage_Impl)', 'description': 'Core damage computation. Reads obj+0x5C (current HP), subtracts damage amount, calls SetHP. If prevent-death flag (obj+0x3A1 bit 7) is set and result would be <= 0, calls SetHP again with max(1.0, old_hp). If HP reaches 0, calls the death handler.', 'key_functions': {'Take_Damage_Impl': {'rva': '0x3AB890', 'ghidra_va': '0x1403AB890'}}, 'pseudocode': 'bool Take_Damage_Impl(GameObjectClass* obj, int damage_type, float damage, attacker_ref, ...) {\n    float old_hp = obj->hp;  // obj+0x5C\n    SetHP(obj, old_hp - damage);\n    \n    if (obj->hp <= 0.0) {\n        if (obj->prevent_death_flags_3A1 & 0x80) {\n            float safe = max(1.0f, old_hp);\n            SetHP(obj, safe);\n        }\n        if (obj->hp <= 0.0) {\n            OnDeath(obj, damage_type, attacker_ref, ...);\n            return false;  // unit is dead\n        }\n    }\n    \n    // Check damage visual thresholds\n    float max_hp = GetMaxHealth(obj->type_ptr);\n    float threshold1 = max_hp * g_damage_threshold_1;  // global at 0x803514\n    float threshold2 = max_hp * g_damage_threshold_2;  // global at 0x8007C0\n    if ((old_hp > threshold1 && obj->hp <= threshold1) ||\n        (old_hp > threshold2 && obj->hp <= threshold2)) {\n        PlayDamageModel(obj);  // switch to damaged art model\n    }\n    \n    UpdateDamageVisualLevel(obj);\n    return true;  // unit survived\n}'}, {'stage': 5, 'name': 'SetHP (Canonical HP Write)', 'description': 'THE single HP write function. Every hitpoint change in the engine flows through this. Clamps to [0.0, max_hp], sets dirty flag, logs death events.', 'key_functions': {'SetHP': {'rva': '0x3A89D0', 'ghidra_va': '0x1403A89D0'}}, 'pseudocode': 'float SetHP(GameObjectClass* obj, float new_hp) {\n    float old_hp = obj->hp;  // +0x5C\n    float clamped = max(0.0f, new_hp);\n    obj->hp = clamped;\n    float max_hp = GetMaxHealth(obj->type_ptr);  // calls 0x3727A0\n    float final_hp = min(max_hp, max(0.0f, obj->hp));\n    obj->hp = final_hp;\n    if (old_hp != final_hp && obj->object_id == g_tracked_object_id)\n        obj->change_notification_flag |= 1;  // +0x3A0\n    if (final_hp < 0.0f)\n        LogDeath(type_name, obj_id, player_name, final_hp, new_hp);\n    return final_hp;\n}'}


### Death Sequence

- **description**: When HP reaches 0.0 in Take_Damage_Impl, the death handler FUN_14039BDB0 is called. This is a complex function that handles: last-hit attribution, death animation selection, debris spawning, kill events, and object destruction.
- **handler_function**:
  - **rva**: 0x39BDB0
  - **ghidra_va**: 0x14039BDB0
- **sequence**: 1. Set death flag at obj+0x130 (OR with 0x40), 2. Record last-hit player (attacker's player ID stored at obj+0x110), 3. Signal death to behavior system (FUN_14038EB10), 4. If debug mode: call FUN_140392600 for death logging, 5. Notify AffectedByShield if present (obj+0x123 check, calls FUN_1405031A0), 6. Choose death animation: query GameObjectType for animation index (0x20 = special, iterate up to 2), 7. If death debris configured (type+0x23E8 or transport with units), spawn wreckage (FUN_1404D07E0), 8. Broadcast death event to the event system (event type 0x25), 9. Recursive: if object has child hardpoints (via obj+0x70 linked list), call death on children, 10. Determine spawn location for debris/explosion via QueryInterface(4), QI(5), or QI(0x16), 11. Push death record to global death queue (DAT_140B15418+0x42), 12. Destroy the object's visual representation (FUN_140265AE0), 13. Iterate sub-weapons: fire each weapon that is flagged as death-fire, 14. If hero unit: trigger hero respawn logic (ScheduleHeroRespawn at 0x48EB10), 15. Select death model/animation based on damage type (normal vs fire vs special), 16. Spawn death SFX and debris object at position, 17. If local player's unit: broadcast UI death notification


### Shield System

- **description**: Shields are separate from hull HP. Each unit can have a front shield (QueryInterface 0x0F -> BaseShieldBehaviorClass) and a rear shield (QueryInterface 0x10 -> ShieldBehaviorClass). Shield values are stored at obj+0xF0 sub-object.
- **front_shield**:
  - **query_interface_id**: 0x0F (15)
  - **behavior_class**: BaseShieldBehaviorClass
  - **vtable_rva**: 0x899458_vicinity
  - **set_function**:
    - **rva**: 0x3A8630
    - **ghidra_va**: 0x1403A8630
  - **read_function**:
    - **rva**: 0x3963C0
    - **ghidra_va**: 0x1403963C0
  - **shield_write_impl**:
    - **rva**: 0x56C1B0
    - **ghidra_va**: 0x14056C1B0
  - **shield_read_impl**:
    - **rva**: 0x56BFB0
    - **ghidra_va**: 0x14056BFB0
  - **storage**:
    - **current_value**: [obj+0xF0]+0xF8 (float32)
    - **description**: Front shield current HP. Clamped to [0, max_front_shield].
  - **max_value_function**:
    - **rva**: 0x372320
    - **ghidra_va**: 0x140372320
  - **max_value_source**: GameObjectType+0xDD0 (base) * difficulty_multiplier * ability_modifiers
- **rear_shield**:
  - **query_interface_id**: 0x10 (16)
  - **behavior_class**: ShieldBehaviorClass
  - **vtable_rva**: 0x899458
  - **set_function**:
    - **rva**: 0x3A91E0
    - **ghidra_va**: 0x1403A91E0
  - **read_function**:
    - **rva**: 0x396420_vicinity
    - **ghidra_va**: 0x140396420
  - **shield_write_impl**:
    - **rva**: 0x549810
    - **ghidra_va**: 0x140549810
  - **shield_read_impl**:
    - **rva**: 0x549490
    - **ghidra_va**: 0x140549490
  - **storage**:
    - **current_value**: [obj+0xF0]+0xFC (float32)
    - **description**: Rear shield current HP.
  - **max_value_function**:
    - **rva**: 0x3725F0
    - **ghidra_va**: 0x1403725F0
  - **max_value_source**: GameObjectType+0xDD4 (base) * difficulty_multiplier * ability_modifiers
- **shield_depletion_triggers_hull_damage**:
  - **description**: When SetRearShield (0x3A91E0) sets shield to 0 and FUN_14039B950 returns true, it calls Take_Damage_Outer (0x38A350) to begin hull damage. Same for SetFrontShield through BaseShieldBehavior. This means shields must be depleted before hull HP can be reduced through normal combat.
  - **rear_shield_death_trigger**: FUN_140549810 at offset +0x88: calls FUN_14038A350 when shield <= 0 and FUN_14039B950 returns true
- **shield_states**:
  - **active**: shield_state = 1 (shields up, absorbing damage)
  - **depleted**: shield_state = 2 (shields down, hull takes damage directly)
  - **transition**: State change triggers visual/SFX events via FUN_140549CD0 and event broadcast
- **difficulty_multipliers**:
  - **description**: Both GetMaxHealth and GetMaxShield apply game-mode-dependent multipliers. Mode 1 (Easy?) uses global at 0xB16DCC, Mode 2 (Hard?) uses 0xB16DC8. Additionally, player-specific multipliers from PlayerObject+0x360 sub-object are applied.
  - **easy_multiplier_global**: 0xB16DCC
  - **hard_multiplier_global**: 0xB16DC8
  - **player_multiplier**:
    - **health**: PlayerObj+0x360 -> sub+0x50
    - **front_shield**: PlayerObj+0x360 -> sub+0x54
    - **rear_shield**: PlayerObj+0x360 -> sub+0x54


### Max Health System

- **description**: GetMaxHealth reads the base max HP from GameObjectType, then applies difficulty multipliers and ability modifiers.
- **function**:
  - **rva**: 0x3727A0
  - **ghidra_va**: 0x1403727A0
- **formula**: max_hp = type->base_max_hp(+0xDCC) * difficulty_multiplier * player_multiplier * (1.0 + ability_bonus)
- **base_hp_offset**: GameObjectType+0xDCC (float32)
- **pseudocode**: float GetMaxHealth(GameObjectType* type, GameObjectClass* obj) {
    float base = type->max_hp_dcc;  // +0xDCC
    
    // Apply difficulty multiplier
    int game_mode = GetGameModeType();
    if (game_mode == 1)  // Easy/Land?
        base *= g_easy_hp_mult;  // DAT_140B16DCC
    else if (game_mode == 2)  // Hard/Space?
        base *= g_hard_hp_mult;  // DAT_140B16DC8
    
    // Apply player-specific multiplier
    if (obj) {
        PlayerObject* player = FindPlayerByID(obj->owner_id);
        if (player && player->faction_sub_360) {
            void* sub = GetFactionSub(player->faction_sub_360);
            base *= *(float*)(sub + 0x50);  // health multiplier
        }
        
        // Apply ability modifiers (Plus<float> + GreaterThan<float> template)
        if (HasAbilityModifiers(obj)) {
            float bonus = ComputeAbilityBonus(obj);  // FUN_14033FB70
            base *= (bonus + 1.0f);
        }
    }
    return base;
}


### Hardpoint System

- **description**: Objects with hardpoints (obj+0x348 != 0xFF) route damage through the HardpointManager. The HardpointManager (QueryInterface 0x16) maintains an array of HardPointClass instances. Each hardpoint has its own HP, shields, and can be independently destroyed.
- **detection_flag**: obj+0x348 (0xFF = no hardpoints / direct HP, other = has hardpoints)
- **manager_query**: QueryInterface(0x16) -> HardpointManager
- **count_function**:
  - **rva**: 0x405300
  - **ghidra_va**: 0x140405300
  - **reads**: [manager+0x28]+0x110 -> +0x18 (int count)
- **get_hardpoint**:
  - **rva**: 0x4052D0
  - **ghidra_va**: 0x1404052D0
- **hull_ratio_via_hardpoints**:
  - **function**:
    - **rva**: 0x405230
    - **ghidra_va**: 0x140405230
  - **description**: Iterates all hardpoints and calls GetHullPercentage (0x396DF0) on each. Used for aggregate health display.
- **hardpoint_damage_routing**: When Take_Damage_PropertyDispatch detects obj+0x348 != 0xFF, it routes damage to individual hardpoints rather than the parent object's HP directly. The HardpointFire function (0x387F50) handles per-hardpoint HP reduction and station level loss notifications.
- **station_level_loss**: When a hardpoint's HP reaches 0, FUN_140387F50 checks for space station level changes and broadcasts TEXT_ENEMY_SPACE_STATION_LEVEL_LOST or TEXT_FRIENDLY_SPACE_STATION_LEVEL_LOST.


### Weapon Fire System

- **description**: The weapon firing system is driven by CombatantBehaviorClass and its children (BaseCombatantClass, CompanyCombatantClass, SquadronCombatantClass). The fire control dispatch at 0x38D730 manages target acquisition, invulnerability pre-checks, weapon selection, and projectile spawning.
- **fire_control_dispatch**:
  - **rva**: 0x38D730
  - **ghidra_va**: 0x14038D730
  - **description**: Master fire control function. Checks: (1) target is enemy via FUN_1402824D0, (2) target is not invulnerable (obj+0x3A7), (3) target is not paused/immune (obj+0x3A0 & 0x42), (4) target's locomotor allows damage, (5) weapon range, (6) LOS check, (7) game mode constraints. Then dispatches to appropriate fire behavior.
  - **invulnerability_precheck**: if (target+0x3A7 == 1) return 0;  // target invulnerable, skip
  - **status_precheck**: if (target+0x3A0 & 0x42) return 0;  // target has immunity flags
- **weapon_tick**:
  - **rva**: 0x387010
  - **ghidra_va**: 0x140387010
  - **description**: Per-frame weapon update. Checks weapon state, manages firing cooldown via delta-time accumulation (param_1+0x60), resolves fire animation model by name, and calls the actual fire function.
  - **cooldown_mechanism**: delta_ticks = current_tick - last_fire_tick (param_1+0x60). When cooldown expires, calls fire via FUN_140381FF0.
  - **fire_animation**: Resolves model name from weapon data (type+0x1C0 SSO string), loads via asset system, and plays fire animation.
- **hardpoint_fire**:
  - **rva**: 0x387F50
  - **ghidra_va**: 0x140387F50
  - **description**: Per-hardpoint damage application. Subtracts damage from hardpoint HP (param_1+0x28), manages hardpoint-level death (station level loss), and fires death-on-destruction effects.
  - **hp_field**: param_1+0x28 (float, current hardpoint HP)
  - **fire_rate**: Managed by weapon tick delta-time accumulation
- **combatant_classes**:
  - **CombatantBehaviorClass**:
    - **rva_ctor**: 0x6383E0
    - **ghidra_va**: 0x1406383E0
    - **vtable_count**: 55 virtual methods (in BaseCombatantClass)
  - **BaseCombatantClass**:
    - **rva_ctor**: 0x6CB700
    - **ghidra_va**: 0x1406CB700
    - **vtable_rva**: 0x8BF6C0
  - **CompanyCombatantClass**:
    - **rva_ctor**: 0x6CBEE0
    - **ghidra_va**: 0x1406CBEE0
  - **SquadronCombatantClass**:
    - **rva_ctor**: 0x6CBA40
    - **ghidra_va**: 0x1406CBA40
- **target_selection**:
  - **description**: BaseCombatantClass vtable[34] (SelectTarget) evaluates targets by range, health ratio, and weapon type bitmask. The weapon bitmask is stored at target_type+0x1648.
  - **weapon_bitmask_offset**: GameObjectType+0x1648
  - **factors**: distance_to_target, target_health_ratio (vtable[31] GetHealthRatio), weapon_type_compatibility


### Damage Calculation Formula

- **description**: The actual damage amount is computed before reaching Take_Damage_Impl. The formula incorporates base weapon damage, damage type modifiers, ability bonuses, and game speed scaling.
- **base_damage**: Defined in XML per weapon/projectile type
- **modifiers**:
  - **game_speed_scaling**: (float)DAT_140B0A320 / (float)DAT_140B0A340 — applied in AnimationDispatch (0x3A92F0)
  - **berserker_check**: FUN_1403A92F0 checks BerserkerAbilityClass RTTI. If berserker active, damage type forced to 0x68 and param_7 set to 1.
  - **combat_bonus_ability**:
    - **class**: CombatBonusAbilityClass
    - **address**: 0x1406F42D0
    - **description**: Modifies 8 separate combat stats via FUN_14038C850: (1) max HP bonus, (2) damage bonus, (3) front shield bonus, (4) rear shield bonus, (5) rate modifier, (6) range modifier, (7) accuracy modifier, (8) additional modifier. After modifying max_hp, scales current HP proportionally: SetHP(obj, (new_max - old_max) + current_hp).
    - **stat_offsets_in_ability_data**:
      - **hp_bonus**: +0x00
      - **damage_bonus**: +0x04
      - **front_shield_bonus**: +0x08
      - **rear_shield_bonus**: +0x0C
      - **rate_modifier**: +0x10
      - **range_modifier**: +0x14
      - **accuracy_modifier**: +0x18
      - **additional**: +0x1C
  - **absorb_blaster_ability**:
    - **class**: AbsorbBlasterAbilityClass
    - **address**: 0x1406EE840
    - **description**: Converts incoming damage to healing. Reads damage type from target type+0x1FF4. If compatible (damage_type & ~0x3 != 0), converts damage to HP: SetHP(obj, current_hp + type->heal_factor(+0x474) * ability_factor + flat_bonus). Only works against specific damage types.
    - **heal_formula**: new_hp = current_hp + (type+0x474 * ability+0x04) + ability+0x08


### Damage Visual Thresholds

- **description**: Take_Damage_Impl checks two global damage thresholds to trigger visual model changes (switching from pristine to damaged to heavily damaged art).
- **threshold_1**:
  - **global_rva**: 0x803514
  - **description**: First damage threshold multiplier (applied to max_hp). When HP crosses below max_hp * threshold, switch to 'damaged' model.
- **threshold_2**:
  - **global_rva**: 0x8007C0
  - **description**: Second damage threshold multiplier. When HP crosses below max_hp * threshold, switch to 'heavily damaged' model.
- **visual_level_function**:
  - **rva**: 0x3AC290
  - **ghidra_va**: 0x1403AC290
- **visual_level_logic**: Iterates an array at type+0xEB0 (float thresholds) paired with type+0xEC8 (animation indices). Compares health_ratio against each threshold and selects the highest matching visual level.


### Prevent Death System

- **description**: The prevent-death flag at obj+0x3A1 bit 7 (mask 0x80) prevents a unit from dying through normal combat damage. When set, Take_Damage_Impl clamps HP to max(1.0, old_hp) instead of letting it reach 0. This is the 'unkillable but damageable' mode used by Set_Cannot_Be_Killed(true) in Lua.
- **flag_offset**: obj+0x3A1
- **flag_mask**: 0x80 (bit 7)
- **implementation**: In Take_Damage_Impl: if (obj->hp <= 0 && (obj->flags_3A1 & 0x80)) { SetHP(obj, max(1.0f, old_hp)); }
- **note**: Distinct from full invulnerability (obj+0x3A7). Prevent-death still allows damage; it just prevents the final kill.


### Invulnerability System

- **description**: Full invulnerability is a multi-layered system. The primary flag at obj+0x3A7 is checked ONLY by Take_Damage_Outer (0x38A350). Additional immunity flags at obj+0x3A0 bits 1 and 6 are checked by FireControl_Dispatch (0x38D730). Setting invulnerability properly requires calling Make_Invulnerable_Setter (0x3ABB80) which propagates to all hardpoints.
- **primary_flag**: obj+0x3A7 (checked by Take_Damage_Outer)
- **status_flags**: obj+0x3A0 & 0x42 (checked by FireControl_Dispatch — prevents targeting entirely)
- **container_flags**: obj+0x381, obj+0x382, obj+0x388
- **setter_function**:
  - **rva**: 0x3ABB80
  - **ghidra_va**: 0x1403ABB80
- **cleanup_function**:
  - **rva**: 0x3A56B0
  - **ghidra_va**: 0x1403A56B0
- **lua_binding**:
  - **rva**: 0x5819E0
  - **ghidra_va**: 0x1405819E0
- **setter_actions**: 1. Get behavior via QueryInterface(1), 2. Notify game engine of invulnerability change, 3. Schedule timer event for invulnerability expiry, 4. Update behavior system invulnerability count, 5. If has hardpoints (obj+0x348 != 0xFF): iterate all hardpoints via QueryInterface(0x16) and recursively call Make_Invulnerable_Setter on each, 6. In space mode (game_mode == 2): adjust shield position offsets


### Health Regeneration

- **description**: Two regeneration systems exist: natural health regen and shield-linked regen. Both call SetHP directly, bypassing invulnerability checks.
- **natural_regen**:
  - **function_rva**: 0x5D70F0
  - **ghidra_va**: 0x1405D70F0
  - **description**: Periodic health regeneration. First call sets HP to 1.0 (revival?), subsequent ticks add regen_rate to current HP, clamped to max_hp.
  - **formula**: new_hp = min(current_hp + regen_rate, max_hp)
- **shield_linked_regen**:
  - **function_rva**: 0x387010
  - **description**: Shield regeneration ticks also affect hull HP via: new_hp = current_hp * ratio (from GetHullPercentage).
- **periodic_heal**:
  - **function_rva**: 0x71B560
  - **ghidra_va**: 0x14071B560
  - **description**: DrainLifeAbilityClass periodic heal. Adds heal_amount to current HP via SetHP.
  - **formula**: new_hp = current_hp + heal_amount
- **periodic_dot**:
  - **function_rva**: 0x6F80D0
  - **ghidra_va**: 0x1406F80D0
  - **description**: Damage-over-time periodic ticks (burning, ion damage, etc.). Adds damage_amount to HP (negative for damage, positive for heal).
  - **formula**: new_hp = current_hp + damage_amount


### Special Attack Abilities

- **description**: 91 SpecialAbilityClass children provide combat modifiers. Key attack abilities:
- **abilities**:
  - **ConcentrateFireAttackAbilityClass**:
    - **rva_ctor**: 0x6F52C0
    - **ghidra_va**: 0x1406F52C0
    - **description**: Focus-fire ability. Increases damage against a single target.
  - **MaximumFirepowerAttackAbilityClass**:
    - **rva_ctor**: 0x706B10
    - **ghidra_va**: 0x140706B10
    - **description**: Maximum firepower mode. Increases rate of fire and damage.
  - **LuckyShotAttackAbilityClass**:
    - **rva_ctor**: 0x706280
    - **ghidra_va**: 0x140706280
    - **description**: Lucky shot chance. Random critical hit bonus.
  - **EnergyWeaponAttackAbilityClass**:
    - **rva_ctor**: 0x6F9980
    - **ghidra_va**: 0x1406F9980
    - **description**: Energy weapon special attack.
  - **IonCannonShotAttackAbilityClass**:
    - **rva_ctor**: 0x7048E0
    - **ghidra_va**: 0x1407048E0
    - **description**: Ion cannon single-shot ability.
  - **EatAttackAbilityClass**:
    - **rva_ctor**: 0x6F7A30
    - **ghidra_va**: 0x1406F7A30
    - **description**: Eat/consume attack (e.g., Sarlacc). Calls damage dispatch with type 0x68, destroys target.
  - **ArcSweepAttackAbilityClass**:
    - **rva_ctor**: 0x6EEAE0
    - **ghidra_va**: 0x1406EEAE0
    - **description**: Area sweep attack.
  - **CableAttackAbilityClass**:
    - **rva_ctor**: 0x6F22F0
    - **ghidra_va**: 0x1406F22F0
    - **description**: Tow cable attack (AT-AT takedown).
  - **EarthquakeAttackAbilityClass**:
    - **rva_ctor**: 0x6F7170
    - **ghidra_va**: 0x1406F7170
    - **description**: Ground-shake AoE attack.
  - **TractorBeamAttackAbilityClass**:
    - **rva_ctor**: 0x710080
    - **ghidra_va**: 0x140710080
    - **description**: Tractor beam immobilize + damage.
  - **GenericAttackAbilityClass**:
    - **rva_ctor**: 0x6FFFF0
    - **ghidra_va**: 0x1406FFFF0
    - **description**: Generic configurable attack ability.
  - **BerserkerAbilityClass**:
    - **rva_ctor**: 0x712D20
    - **ghidra_va**: 0x140712D20
    - **description**: Berserker mode. Forces damage type to 0x68, overrides normal attack behavior.
  - **AbsorbBlasterAbilityClass**:
    - **rva**: 0x6EE840
    - **ghidra_va**: 0x1406EE840
    - **description**: Converts incoming blaster damage to healing.
  - **CombatBonusAbilityClass**:
    - **rva**: 0x6F42D0
    - **ghidra_va**: 0x1406F42D0
    - **description**: Multi-stat combat bonus: HP, damage, shields (front+rear), rate, range, accuracy.
  - **LeechShieldsAbilityClass**:
    - **rva_ctor**: 0x717530
    - **ghidra_va**: 0x140717530
    - **description**: Steals shield points from target.
  - **ShieldFlareAbilityClass**:
    - **rva_ctor**: 0x71C820
    - **ghidra_va**: 0x14071C820
    - **description**: Shield disruption/flare effect.
  - **DrainLifeAbilityClass**:
    - **rva**: 0x71B560
    - **ghidra_va**: 0x14071B560
    - **description**: Drains HP from target and heals caster.
- **vtable_structure**:
  - **vfunc_0**: destructor
  - **vfunc_2**: Activate (pure virtual — each ability overrides)
  - **vfunc_17**: RestoreEffect
  - **vfunc_18**: CanActivate (checks game mode scope)
  - **vfunc_19**: ApplyEffect
  - **vfunc_29**: OnTick (periodic update — used by DoT, heals)
  - **vfunc_31**: OnApply (initial application — used by CombatBonus, AbsorbBlaster)
  - **vfunc_32**: OnHit (reactionary — used by EatAttack)


### Projectile System

- **description**: Projectiles are GameObjectClass instances with ProjectileBehaviorClass attached. They are spawned by the weapon fire system and carry damage data to the target.
- **behavior_class**:
  - **name**: ProjectileBehaviorClass
  - **vtable_rva**: 0x878D58
  - **virtual_methods**: 33
  - **inherits**: BehaviorClass
- **data_pack**:
  - **name**: ProjectileDataPackClass
  - **rva_ctor**: 0x55B4D0
  - **ghidra_va**: 0x14055B4D0
- **projectile_spawn**: Spawned via FUN_14029F810 (general object spawn). Projectile carries damage amount, damage type, owner reference.
- **projectile_hit**: On collision, projectile triggers damage application through the Take_Damage_Outer pipeline on the target.


### Key Struct Offsets For Combat

- **GameObjectClass**:
  - **vtable_ptr**: +0x00 (8 bytes, pointer)
  - **object_id**: +0x50 (4 bytes, int32)
  - **owner_player_id**: +0x58 (4 bytes, int32)
  - **hp**: +0x5C (4 bytes, float32) — THE hitpoints field
  - **locomotor_ptr**: +0xA8 (8 bytes, pointer)
  - **shield_sub_object**: +0xF0 (8 bytes, pointer) — front shield at [ptr+0xF8], rear shield at [ptr+0xFC]
  - **health_sub_object_ptr**: +0x118 (8 bytes, pointer)
  - **component_array_ptr**: +0x278 (8 bytes, pointer)
  - **game_object_type_ptr**: +0x298 (8 bytes, pointer)
  - **animation_controller_ptr**: +0x2A0 (8 bytes, pointer)
  - **container_ref**: +0x2B0 (8 bytes, pointer)
  - **squad_unit_list**: +0x2D0 (8 bytes, pointer)
  - **component_lookup_table**: +0x332 (byte array)
  - **parent_component_index**: +0x335 (1 byte)
  - **front_shield_present**: +0x341 (1 byte, 0xFF = none)
  - **rear_shield_present**: +0x342 (1 byte, 0xFF = none)
  - **direct_hp_path_flag**: +0x348 (1 byte, 0xFF = direct HP, other = hardpoints)
  - **change_notification_flag**: +0x3A0 (1 byte, bit 0 = HP changed, bits 1+6 = immunity)
  - **prevent_death_flags**: +0x3A1 (1 byte, bit 7 = prevent death)
  - **invulnerability_flag**: +0x3A7 (1 byte, 1 = invulnerable)
- **GameObjectType**:
  - **type_name**: +0xF8 (SSO string, 32 bytes)
  - **base_max_hp**: +0xDCC (float32)
  - **base_max_front_shield**: +0xDD0 (float32)
  - **base_max_rear_shield**: +0xDD4 (float32)
  - **damage_threshold_array**: +0xEB0 (pointer to float array)
  - **damage_threshold_count**: +0xEB8 (int32)
  - **damage_anim_index_array**: +0xEC8 (pointer to int array)
  - **damage_anim_threshold_array**: +0xED8 (pointer to 0x20-stride array)
  - **weapon_bitmask**: +0x1648 (for targeting compatibility)
  - **death_debris_count**: +0xD48 (int32)
  - **death_debris_list**: +0xD40 (pointer)


### All Sethp Callers

- **description**: All 18 known code paths that modify HP through SetHP. Understanding these is critical for any HP-related mod.
- **callers**: {'rva': '0x29F5B5', 'function_rva': '0x29F270', 'category': 'death_cleanup', 'description': 'Death and cleanup handlers'}, {'rva': '0x38738B', 'function_rva': '0x387010', 'category': 'shield_health_regen', 'description': 'Shield regen tick: HP * ratio'}, {'rva': '0x3A0A90', 'function_rva': '0x3A06A0', 'category': 'property_init', 'description': 'Initial HP from XML data system'}, {'rva': '0x3AB0B0', 'function_rva': '0x3A97E0', 'category': 'take_damage_dispatch', 'description': 'Property dispatch path with hardpoint post-check'}, {'rva': '0x3AB8C6', 'function_rva': '0x3AB890', 'category': 'take_damage_impl', 'description': 'Core damage: old_hp - damage'}, {'rva': '0x3AB8EC', 'function_rva': '0x3AB890', 'category': 'take_damage_impl_prevent_death', 'description': 'Prevent-death clamp: max(1.0, old_hp)'}, {'rva': '0x42DD63', 'function_rva': '0x42DBD0', 'category': 'lua_set_hull', 'description': 'Lua Set_Hull direct write'}, {'rva': '0x4B0179', 'function_rva': '0x4AFBD0', 'category': 'spawn_hp_scaling', 'description': 'Spawn proportional rescale: old_hp * (new_max/old_max)'}, {'rva': '0x4B0E99', 'function_rva': '0x4B0DC0', 'category': 'spawn_hp_scaling_variant', 'description': 'Spawn rescale variant'}, {'rva': '0x4CBA8B', 'function_rva': '0x4CB6B0', 'category': 'behavior_attachment', 'description': 'HP set during behavior attach'}, {'rva': '0x5D7129', 'function_rva': '0x5D70F0', 'category': 'health_regen', 'description': 'Natural regen: set to 1.0 (revival)'}, {'rva': '0x5D73FC', 'function_rva': '0x5D70F0', 'category': 'health_regen_tick', 'description': 'Regen tick: min(current+rate, max)'}, {'rva': '0x6EE8EE', 'function_rva': '0x6EE840', 'category': 'absorb_blaster', 'description': 'AbsorbBlaster ability heal'}, {'rva': '0x6F4444', 'function_rva': '0x6F42D0', 'category': 'combat_bonus_hp_adjust', 'description': 'CombatBonus HP scaling after max_hp change'}, {'rva': '0x6F4DA2', 'function_rva': '0x6F4CC0', 'category': 'combat_bonus_deactivate', 'description': 'CombatBonus deactivation HP restore'}, {'rva': '0x6F8791', 'function_rva': '0x6F80D0', 'category': 'periodic_dot', 'description': 'DoT tick: current_hp + damage_amount'}, {'rva': '0x6FBAD1', 'function_rva': '0x6FB850', 'category': 'combat_event', 'description': 'Combat event handler'}, {'rva': '0x71B7BA', 'function_rva': '0x71B560', 'category': 'periodic_heal_tick', 'description': 'DrainLife heal: current_hp + heal_amount'}


### Damage Type System

- **description**: Damage types are integer IDs used throughout the combat system. The AnimationDispatch function (0x3A92F0) uses damage type to select animation and handle Berserker override. Specific type IDs observed:
- **known_types**:
  - **0x00**: Default/Normal (triggers default path in AnimationDispatch)
  - **0x05**: Special type 1 (falls through to default in AnimationDispatch switch)
  - **0x06**: Special type 2 (triggers special ability search in AnimationDispatch — BerserkerAbilityClass check)
  - **0x08**: Fire/Burn damage (death handler checks: selects death anim variant 0x0C)
  - **0x68**: Berserker/Eat forced type (overrides all other types when Berserker active, or used by EatAttack)
  - **0x74**: Construction/Build type (used by tactical building construction)
- **type_resolved_by**: AnimationDispatch (0x3A92F0) uses FUN_140264A40 to query animation count for the given type, then FUN_1401FFB40 for random selection within count.
- **berserker_override**: When AnimationDispatch detects the unit is in active fire-while-queued state (obj+0x100 -> +0x2CC != 0), it checks if current command is 0x68. If not, it iterates the ability list, dynamic_casts to BerserkerAbilityClass (RTTI), and if found, forces damage_type = 0x68.


### Game Speed Scaling

- **description**: Combat calculations are scaled by game speed to maintain consistent damage-per-second regardless of game speed setting.
- **numerator_global**: DAT_140B0A320 (current frame counter or time)
- **denominator_global**: DAT_140B0A340 (reference time base)
- **formula**: time_scale = (float)DAT_140B0A320 / (float)DAT_140B0A340
- **applied_in**: AnimationDispatch (0x3A92F0) passes time_scale to FUN_140265560 and FUN_140265490


### Modding Interception Points

- **description**: Recommended hooking points for combat mods, ordered by specificity.
- **hooks**: {'name': 'SetHP (universal HP intercept)', 'rva': '0x3A89D0', 'aob': '40 53 48 83 EC 60 0F 29 74 24 50 0F 57 C0 F3 0F 10 71 5C', 'params': 'RCX=GameObjectClass*, XMM1=new_hp (float32)', 'use_case': 'Intercept ALL HP changes. Filter by caller category for selective mods.'}, {'name': 'Take_Damage_Outer (combat damage gate)', 'rva': '0x38A350', 'params': 'RCX=GameObjectClass*, XMM1=damage_amount', 'use_case': 'Hook combat-only damage. Invulnerability bypass, damage multipliers.'}, {'name': 'Take_Damage_Impl (damage subtraction)', 'rva': '0x3AB890', 'params': 'RCX=obj, param_2=damage_type, XMM1(param_3)=damage_amount, param_4=attacker_ref', 'use_case': 'Modify damage amount just before HP subtraction. Armor/resistance mods.'}, {'name': 'GetMaxHealth (max HP override)', 'rva': '0x3727A0', 'params': 'RCX=GameObjectType*, RDX=GameObjectClass*', 'return': 'XMM0=max_hp (float32)', 'use_case': 'Modify max HP for units. Affects healing caps, shield scaling, visual thresholds.'}, {'name': 'FireControl_Dispatch (weapon targeting)', 'rva': '0x38D730', 'use_case': 'Modify targeting behavior, add custom damage types, force-attack allies.'}, {'name': 'AnimationDispatch (damage events)', 'rva': '0x3A92F0', 'use_case': 'Hook damage type routing, custom damage type handlers.'}

