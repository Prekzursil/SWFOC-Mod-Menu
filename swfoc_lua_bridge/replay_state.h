#pragma once
// replay_state.h -- shared ReplayState definitions for the offline replay
// harness and the bridge test harness.
//
// The replay observer/mutation helpers in replay_harness.cpp operate on a
// single in-memory `ReplayState` (`g_replay`). The test harness needs to
// exercise those helpers without spinning up the full pipe listener, so the
// data structures and the small set of helpers are factored out here.
//
// Keep this header header-only and dependency-free so both translation
// units can include it without changing the existing build commands.

#include <algorithm>
#include <limits>
#include <cstdint>
#include <map>
#include <string>
#include <utility>
#include <vector>

// ----- Shared replay record types -----

struct ReplayPlayer {
    uint32_t    slot;
    std::string faction_name;
    double      credits;
    int32_t     tech_level;
    std::string player_name;
};

struct ReplayGlobal {
    uint8_t  lua_type;
    uint64_t raw_value_or_ptr;
};

struct ReplayMetadataEntry {
    std::string key;
    std::string value;
};

// Per-planet record for SWFOC_ReplayPlanetCorruption + Task 141-143 observers.
struct ReplayPlanetInfo {
    std::string name;
    float       corruption     = 0.0f;
    int32_t     owner_slot     = -1;
    int32_t     tech_level     = 0;  // Task 143
    int32_t     building_count = 0;  // Task 143
    bool        is_capital     = false; // Task 143
};

// Per-task-force record for SWFOC_ReplayTaskForceCount observers.
struct ReplayTaskForceRecord {
    int32_t     owner_slot = -1;
    std::string name;
};

// Per-ability record for Task 139/140. index is the 0-based slot as the
// SpecialAbility catalogue exposes it; cooldown_remaining_ms counts down
// to 0 (engine does the ticking; replay fixtures pin snapshot values);
// usable tracks whether the game lets the player activate it now (may
// be false even at cooldown=0 if target conditions aren't met).
struct ReplayAbility {
    int32_t     index                  = 0;
    std::string name;
    int32_t     cooldown_remaining_ms  = 0;
    bool        usable                 = true;
};

// Per-hardpoint record used to model the SWFOC engine's per-hardpoint
// behavior-object chain. `Make_Invulnerable` in the real engine iterates
// hardpoints and calls BehaviorAttach(hp, "INVULNERABLE", 0) per entry
// (see ledger: fact_make_invulnerable_hardpoint_propagation). In the replay
// harness we mirror that by storing an ordered behavior-name list per
// hardpoint; damage simulation checks for "INVULNERABLE" to decide if the
// hardpoint short-circuits hull decrement. Section 12 carries these in
// fixtures captured with bridge v1.5-dev+b or later.
struct ReplayHardpoint {
    uint32_t                  index         = 0;    // hardpoint slot as returned by HardpointGet
    std::vector<std::string>  behaviors;             // active behavior type names (e.g. "INVULNERABLE")
};

// Per-unit record used by the replay harness to model a selected unit's
// damageable state and hardpoint chain. Mirrors GameObject fields read by
// the real bridge: HP at +0x5C, InvulnFlag at +0x3A7, PreventDeath bit at
// +0x3A1 (bit 0x80). `hardpoints` carries per-hardpoint behavior lists so
// damage simulation can distinguish "invulnerable flag was flipped" (no
// gameplay effect in the real engine) from "INVULNERABLE behavior is
// attached to every hardpoint" (real immunity). Fixture sections 11 + 12
// carry these.
struct ReplayUnitDetail {
    uint64_t                      obj_addr          = 0;
    std::string                   type_name;                     // e.g. "Aggressor_Destroyer"
    int32_t                       owner_slot        = -1;
    float                         hull              = 0.0f;
    float                         max_hull          = 0.0f;
    uint8_t                       invuln_flag       = 0;         // display byte at +0x3A7
    uint8_t                       prevent_death     = 0;         // bit 0x80 of +0x3A1
    // Task 130 (2026-04-23). Shield field offset is not yet pinned via
    // IDA; the replay mirror still models shield + max_shield so the
    // offline contract for SetUnitShield can ship (Phase 1) ahead of
    // the live memory-write path (Phase 2 once the offset lands in
    // knowledge-base/verified_facts.json).
    float                         shield            = 0.0f;
    float                         max_shield        = 0.0f;
    // Task 125 (2026-04-23). Locomotor speed. re-findings cite a two-
    // level deref (obj+0xA8 → +0x2A0) so the live path needs careful
    // safe-read handling; the replay contract ships ahead as a single
    // float so the Inspector / Speed tab can render live values from
    // either a replay fixture or a live capture once Phase 2 lands.
    float                         speed             = 0.0f;
    float                         max_speed         = 0.0f;
    // Task 139/140 (2026-04-23). Per-unit ability catalogue.
    std::vector<ReplayAbility> abilities;
    // Task 134 (2026-04-23). Hero flag. No IDA-pinned GameObject field
    // yet; the replay mirror flags heroes explicitly so Phase 1 of the
    // Hero Lab UI can filter. Phase 2 will correlate this with a live
    // detection (either a GameObject flag byte or RTTI-class match).
    bool                          is_hero           = false;
    // Respawn state (Task 135+136). respawn_remaining_ms <= 0 means
    // either alive (hull > 0) or permadeath (respawn_enabled=false).
    // Phase 1 models the fields so the Hero Lab can render them from
    // replay fixtures; the live probe helpers land once the hero
    // state machine is walked.
    int32_t                       respawn_remaining_ms = 0;
    bool                          respawn_enabled      = true;
    // Task 105 (2026-04-23). Unit's outgoing attack power. Phase 1 stores
    // the value so OHK's snapshot/restore pattern can model what the
    // live engine hook will actually do — inflate on enable, restore on
    // disable. Default 100 is a neutral mid-range value so fixtures
    // without an explicit attack_power still round-trip cleanly.
    float                         attack_power      = 100.0f;
    std::vector<ReplayHardpoint>  hardpoints;
};

struct ReplayState {
    uint32_t format_version       = 0;
    uint64_t capture_timestamp_ms = 0;
    uint8_t  engine_build_hash[32] = {0};
    uint8_t  game_mode            = 0;

    std::vector<ReplayPlayer>          players;
    std::vector<uint64_t>              lua_state_ptrs;
    std::map<std::string, uint32_t>    objects;
    std::map<std::string, ReplayGlobal> globals;
    std::map<std::string, std::string> metadata;

    // v2 section extensions (sections 6-10) -- all OPTIONAL.
    std::map<std::string, ReplayPlanetInfo>                       planets;
    std::map<std::pair<std::string, std::string>, std::string>    diplomacy;
    std::map<std::string, std::vector<float>>                     cooldowns;
    std::vector<ReplayTaskForceRecord>                            task_forces;
    std::map<std::string, std::vector<int32_t>>                   object_owners;

    // v2 section extensions (sections 11-13) — unit detail for Tasks 99/100.
    //
    // Added 2026-04-23 so the replay harness can exercise the hardpoint-
    // behavior invulnerability path (Task 99) and the damage-application
    // path (Task 100) against captured fixtures without requiring the game
    // to be running. Fixtures without these sections are still valid — the
    // unit map stays empty and Unit-Control helpers fail gracefully with
    // `ERR: unknown obj_addr`.
    //
    // Section 11: `selected_units`     list of obj_addrs currently selected
    // Section 12: `unit_detail`        per-unit hull/flags/hardpoints
    // Section 13: `behavior_attach`    per-hardpoint behavior name lists
    //                                  (merged into units[].hardpoints on load)
    std::vector<uint64_t>                                         selected_units;
    std::map<uint64_t, ReplayUnitDetail>                          units;

    // Mutation seam state (not in any snapshot section).
    std::string last_story_event;

    // Task 113 (2026-04-23) — fog-of-war reveal state. Each slot whose fog is
    // revealed is present in the set; absence means "fog still enabled for
    // this slot". Not part of any snapshot section — pure runtime toggle.
    std::vector<int32_t> revealed_slots;

    // Task 129 (2026-04-23) — damage multiplier. global_damage_mult applies
    // when no per-slot override is active. per_slot_damage_mult[slot] wins
    // over the global value when present. Default 1.0 everywhere (no
    // scaling). ReplayMutApplyDamage consults the effective multiplier
    // for the TARGET unit (i.e. incoming damage scaling, not outgoing):
    // setting slot=1 to 2.0 makes enemies take 2x damage, which is the
    // combat-demo expectation from the V2 UI.
    float                         global_damage_mult = 1.0f;
    std::map<int32_t, float>      per_slot_damage_mult;

    // Task 123 (2026-04-23) — income multiplier. Same shape as damage
    // mult but applied to per-tick credits delta in ReplayMutTickIncome.
    float                         global_income_mult = 1.0f;
    std::map<int32_t, float>      per_slot_income_mult;

    // Task 124 (2026-04-23) — build-speed multiplier. Applied per-tick to
    // production queues via ReplayMutTickBuildProgress (future helper) or
    // read directly by the Inspector / Economy tabs. Same global +
    // per-slot shape.
    float                         global_build_speed_mult = 1.0f;
    std::map<int32_t, float>      per_slot_build_speed_mult;

    // Task 126 (2026-04-23) — per-faction move-speed multiplier.
    // Distinct from Task 125's per-UNIT speed: this scales every unit
    // owned by `slot` on the next tick without overwriting each unit's
    // individual speed. Global fallback supported.
    float                         global_faction_speed_mult = 1.0f;
    std::map<int32_t, float>      per_faction_speed_mult;

    // Task 127 (2026-04-23) — global game speed (simulation rate). Only
    // a global value; there is no per-slot game speed in the engine.
    // Default 1.0 (normal pace). The replay simulator honours this in
    // ReplayMutTickIncome and similar tick-driven mutations so fixtures
    // can stress slow-mo / fast-forward without a real clock.
    float                         global_game_speed  = 1.0f;

    // Task 122 (2026-04-23) — FreezeCredits. When a slot is in the map,
    // every income tick restores credits to the frozen value. Absence of
    // the slot means "not frozen". Combines with income_mult so a frozen
    // slot ignores the mult entirely (frozen wins).
    std::map<int32_t, double>     frozen_credits_targets;

    // Task 131 (2026-04-23) — weapon fire-rate multiplier. Shape mirrors
    // damage_mult (global + per-slot with clear-on-1.0). Applied by
    // ReplayMutApplyFireRate which divides a base_cooldown_ms by the
    // effective multiplier, so higher multiplier = faster fire. Default
    // 1.0 everywhere means "no scaling" and is the Phase 1 safe state.
    float                         global_fire_rate_mult = 1.0f;
    std::map<int32_t, float>      per_slot_fire_rate_mult;

    // Task 132 (2026-04-23) — Area-damage toggle. When enabled, every
    // damage application also splashes a fraction of the amount onto
    // every other unit within the radius. Global bool only (no per-slot
    // override for Phase 1 — the engine branch is engine-wide). Default
    // false (normal targeting). ReplayMutApplyAreaSplash is the explicit
    // helper that fires splash when enabled; ReplayMutApplyDamage is
    // NOT auto-splashed (Phase 2 will move the call site).
    bool                          area_damage_enabled = false;
    float                         area_damage_radius  = 150.0f;    // synthetic tactical tiles
    float                         area_damage_falloff = 0.5f;      // splash = primary * falloff

    // Task 161 (2026-04-23) — Instant-build toggle. When enabled, the
    // Phase 2 AOB patch NOPs the per-tick build-progress increment and
    // writes full progress at build-queue submit time. Global bool only
    // (no per-slot — AOB patches are engine-wide by construction).
    // Default false (normal build times).
    bool                          instant_build_enabled = false;

    // Task 162 (2026-04-23) — Free-build toggle. When enabled, the
    // Phase 2 AOB patch NOPs the credits-deduction instruction in the
    // build-submit path. Complement to #160 SetBuildCost(slot, 0.0): both
    // achieve "build without paying", but via different mechanisms:
    //   * #160 is a per-slot multiplier (player-targeted, allows partial
    //     discounts across multiple slots);
    //   * #162 is an AOB flag that short-circuits the deduction entirely
    //     (engine-wide, simpler, loudly observable in the disassembly).
    // When both are enabled, #162 wins because the instruction is NOP'd
    // before the multiplier call site is reached.
    bool                          free_build_enabled    = false;

    // Task 160 (2026-04-23) — per-slot build-cost multiplier. 0.0 = free
    // build, 1.0 = normal cost, 2.0 = double cost. Default 1.0 globally.
    // Shape mirrors damage_mult: global fallback + per-slot override with
    // clear-on-1.0. Phase 2 hook sits on the credits-deduction call site
    // in the engine's build-progress tick and scales by the effective
    // multiplier. Negative values rejected (no "refund" via negative).
    float                         global_build_cost_mult = 1.0f;
    std::map<int32_t, float>      per_slot_build_cost_mult;

    // Task 163 (2026-04-23) — per-slot unit-cap override. When a slot is
    // in the map, the live unit-cap check returns the override value
    // instead of the engine's default. -1 in the map means "unlimited".
    // Absence from the map means "use engine default" — we don't track
    // the default here since it varies per faction and the UI consumer
    // reads the engine-default via separate query. Negative values
    // ≠ -1 are rejected (only -1 is a sentinel; other negatives would
    // confuse the live hook).
    std::map<int32_t, int32_t>    per_slot_unit_cap_override;

    // Task 114 (2026-04-23) — per-slot AI freeze. When a slot is in the
    // set, the Phase 2 hook will bypass that slot's AI decision-loop tick
    // (resulting in idle units that don't issue orders). Unlike enemy
    // READ-ONLY for unit state, freezing enemy AI is the whole POINT of
    // the feature — it's the player choosing "no enemy pressure", which
    // maps to a global toggle in the engine's AI scheduler rather than
    // per-unit mutation. All slots may be frozen.
    std::vector<int32_t> frozen_ai_slots;

    // Task 115 (2026-04-23) — camera unlock + position. Default:
    // cam_unlocked=false, engine drives camera. When true, SetCameraPos
    // overwrites the engine's view target. Phase 1 stores the pose in
    // the replay mirror so UI controls can bind-and-display even before
    // the memory write path lands. Rotation and zoom are carried for
    // completeness; they mirror what the V2 Camera tab will render.
    bool                          cam_unlocked      = false;
    float                         cam_pos_x         = 0.0f;
    float                         cam_pos_y         = 0.0f;
    float                         cam_pos_z         = 0.0f;
    float                         cam_rot           = 0.0f;
    float                         cam_zoom          = 1.0f;

    // Task 172 (2026-04-24) — per-slot orbital phase. 0 = ground/tactical,
    // 1 = orbital/space. The Phase 2 hook will pin the engine's
    // orbital-phase flag offset; until then the mirror keeps a per-slot
    // map so V2 UIs can toggle and read back the intended state.
    std::map<int32_t, uint8_t>    per_slot_orbital_phase;

    // Task 173 (2026-04-24) — music subsystem state. The engine's music
    // system has separate volume + currently-playing-track surfaces;
    // Phase 1 mirrors both as raw fields so the V2 panel binds to them
    // before the IDA-pinned sound-system hooks land.
    float                         music_volume       = 1.0f;   // 0.0 - 1.0
    bool                          music_paused       = false;
    std::string                   music_current_track;

    // Task 174 (2026-04-24) — veterancy ranks per unit. Engine layout
    // unknown until a veterancy field RE pass; Phase 1 stores rank
    // 0..3 in a per-obj_addr map so the Veterancy Manager VM can stage
    // edits today.
    std::map<uint64_t, uint8_t>   per_unit_veterancy;

    // Task 175 (2026-04-24) — map-hint sprite system. The engine's
    // hint-system pins minimap markers (objective dots, "go here"
    // arrows). Phase 1 keeps a list of MapHint records keyed by id;
    // mutations append/remove; Phase 2 wires through to the engine's
    // hint registration once the system is RE'd.
    struct MapHintRecord {
        std::string  hint_id;
        std::string  caption;
        float        world_x = 0.0f;
        float        world_y = 0.0f;
        float        world_z = 0.0f;
        int32_t      sprite_index = 0;
    };
    std::vector<MapHintRecord>    map_hints;

    // Task 105 (2026-04-23) — OHK (one-hit-kill) toggle. Inflates every
    // LOCAL unit's attack_power to a sky-high value while ohk_enabled is
    // true. Snapshot/restore: enabling saves each unit's base attack_power
    // into ohk_saved_attack_powers[obj_addr]; disabling restores from the
    // snapshot and clears the map. Enemy units are never touched (READ-ONLY
    // discipline — same gate as ReplayMutSweepGodMode). If a unit spawns
    // after OHK is enabled, the sweep has already returned so the new unit
    // keeps its natural attack_power; this matches live-engine behavior
    // where a toggle is a one-shot action, not a reactive daemon.
    bool                          ohk_enabled = false;
    static constexpr float        kOhkInflatedAttackPower = 99999.0f;
    std::map<uint64_t, float>     ohk_saved_attack_powers;

    // Task 133 (2026-04-23) — per-slot target filter bitmask. Bit 0 =
    // ENEMY, Bit 1 = FRIENDLY, Bit 2 = NEUTRAL. Default 0x7 (all). A
    // zero bitmask means "cannot fire on anything", which is a valid
    // disarm state. Enemy target-filter writes are rejected at the
    // bridge layer — the replay mutation accepts any slot but the live
    // stub enforces slot==local_slot (enemy READ-ONLY discipline).
    static constexpr uint32_t TARGET_ENEMY    = 0x1;
    static constexpr uint32_t TARGET_FRIENDLY = 0x2;
    static constexpr uint32_t TARGET_NEUTRAL  = 0x4;
    static constexpr uint32_t TARGET_ALL      = TARGET_ENEMY | TARGET_FRIENDLY | TARGET_NEUTRAL;
    std::map<int32_t, uint32_t>   per_slot_target_filter;

    // Task 112 (2026-04-23) — damage-event ring buffer (pure-state mirror
    // of the live g_eventRing in lua_bridge.cpp). Pushed by the replay
    // damage simulator; drained by ReplayObsEventStreamDrain.
    struct DamageEventRecord {
        uint64_t timestamp_ms = 0;
        uint64_t obj_addr     = 0;
        int32_t  owner_slot   = -1;
        float    requested_hp = 0.0f;
        float    current_hp   = 0.0f;
    };
    std::vector<DamageEventRecord> damage_event_log;

    int local_slot = -1;
};

// ----- Shared helpers -----

inline std::string ReplayUpper(const std::string& s) {
    std::string out(s.size(), '\0');
    for (size_t i = 0; i < s.size(); ++i) {
        unsigned char c = static_cast<unsigned char>(s[i]);
        out[i] = (c >= 'a' && c <= 'z')
            ? static_cast<char>(c - ('a' - 'A'))
            : static_cast<char>(c);
    }
    return out;
}

inline std::pair<std::string, std::string>
ReplayDiplomacyKey(const std::string& a, const std::string& b) {
    std::string ua = ReplayUpper(a);
    std::string ub = ReplayUpper(b);
    if (ua <= ub) return {ua, ub};
    return {ub, ua};
}

// ----- Pure-state observer implementations (no Lua dependency) -----
//
// These mirror the Lua_Replay* helpers in replay_harness.cpp but operate
// directly on a `ReplayState` reference so the test harness can call them
// without going through the fake Lua stack.

inline double ReplayObsPlayerCredits(const ReplayState& s, const std::string& faction) {
    std::string needle = ReplayUpper(faction);
    for (const auto& p : s.players) {
        if (ReplayUpper(p.faction_name) == needle) return p.credits;
    }
    return -1.0;
}

inline double ReplayObsPlayerTechLevel(const ReplayState& s, const std::string& faction) {
    std::string needle = ReplayUpper(faction);
    for (const auto& p : s.players) {
        if (ReplayUpper(p.faction_name) == needle) return static_cast<double>(p.tech_level);
    }
    return -1.0;
}

inline std::string ReplayObsLastStoryEvent(const ReplayState& s) {
    return s.last_story_event;
}

inline std::string
ReplayObsDiplomaticState(const ReplayState& s, const std::string& a, const std::string& b) {
    auto key = ReplayDiplomacyKey(a, b);
    auto it = s.diplomacy.find(key);
    if (it == s.diplomacy.end()) return "hostile";
    return it->second;
}

inline double ReplayObsPlanetCorruption(const ReplayState& s, const std::string& planet) {
    auto it = s.planets.find(ReplayUpper(planet));
    if (it == s.planets.end()) return -1.0;
    return static_cast<double>(it->second.corruption);
}

inline int32_t
ReplayObsUnitOwner(const ReplayState& s, const std::string& type_name, int index) {
    if (index < 0) return -1;
    auto it = s.object_owners.find(ReplayUpper(type_name));
    if (it == s.object_owners.end() || index >= static_cast<int>(it->second.size())) {
        return -1;
    }
    return it->second[index];
}

inline double
ReplayObsCooldownState(const ReplayState& s, const std::string& unit_type, int ability_idx) {
    if (ability_idx < 0) return -1.0;
    auto it = s.cooldowns.find(unit_type);
    if (it == s.cooldowns.end() || ability_idx >= static_cast<int>(it->second.size())) {
        return -1.0;
    }
    return static_cast<double>(it->second[ability_idx]);
}

inline int ReplayObsTaskForceCount(const ReplayState& s, int slot) {
    int count = 0;
    for (const auto& tf : s.task_forces) {
        if (tf.owner_slot == slot) count++;
    }
    return count;
}

inline int ReplayObsHumanPlayerSlot(const ReplayState& s) {
    return s.local_slot;
}

// ----- Pure-state mutation seam implementations -----

inline int ReplayMutPushStoryEvent(ReplayState& s, const std::string& event) {
    s.last_story_event = event;
    return 1;
}

inline int
ReplayMutSetDiplomacy(ReplayState& s, const std::string& a, const std::string& b, const std::string& state) {
    s.diplomacy[ReplayDiplomacyKey(a, b)] = state;
    return 1;
}

inline int
ReplayMutSetPlanetCorruption(ReplayState& s, const std::string& planet, double value) {
    std::string key = ReplayUpper(planet);
    auto& info = s.planets[key];
    if (info.name.empty()) info.name = planet;
    info.corruption = static_cast<float>(value);
    return 1;
}

inline int
ReplayMutSpawnUnit(ReplayState& s, const std::string& faction, const std::string& type_name, int count) {
    if (count <= 0) return 0;
    int32_t owner_slot = -1;
    std::string needle = ReplayUpper(faction);
    for (const auto& p : s.players) {
        if (ReplayUpper(p.faction_name) == needle) {
            owner_slot = static_cast<int32_t>(p.slot);
            break;
        }
    }
    std::string type_key = ReplayUpper(type_name);
    auto& owners = s.object_owners[type_key];
    for (int i = 0; i < count; i++) owners.push_back(owner_slot);
    s.objects[type_name] += static_cast<uint32_t>(count);
    return 1;
}

inline int
ReplayMutSetCooldown(ReplayState& s, const std::string& unit_type, int ability_idx, double value) {
    if (ability_idx < 0 || ability_idx > 256) return 0;
    auto& slots = s.cooldowns[unit_type];
    if (static_cast<int>(slots.size()) <= ability_idx) {
        slots.resize(static_cast<size_t>(ability_idx) + 1, 0.0f);
    }
    slots[static_cast<size_t>(ability_idx)] = static_cast<float>(value);
    return 1;
}

inline int
ReplayMutAddTaskForce(ReplayState& s, int slot, const std::string& name) {
    ReplayTaskForceRecord rec;
    rec.owner_slot = slot;
    rec.name = name;
    s.task_forces.push_back(std::move(rec));
    return 1;
}

inline int ReplayMutSwitchLocalPlayer(ReplayState& s, int slot) {
    if (slot == -1) {
        s.local_slot = -1;
        return 1;
    }
    for (const auto& p : s.players) {
        if (static_cast<int>(p.slot) == slot) {
            s.local_slot = slot;
            return 1;
        }
    }
    return 0;
}

// ----- Unit / hardpoint / behavior helpers (Task 101 schema) -----
//
// These mirror the SWFOC engine's behavior-object invulnerability path so
// the replay harness can exercise Tasks 99/100 against fixtures:
//
//   Real engine:  Make_Invulnerable(unit, true) -> for each hardpoint ->
//                 BehaviorAttach(hp, "INVULNERABLE", 0)
//   Real damage:  ApplyDamage(unit, amount) checks each hardpoint for an
//                 attached "INVULNERABLE" behavior; any INVULNERABLE
//                 hardpoint short-circuits hull decrement.
//
// Flag bytes (`invuln_flag`, `prevent_death`) are tracked independently
// because the 2026-04-23 live validation confirmed they are display flags
// with no gameplay effect — the test surface must prove that writing
// those bytes does NOT stop hull loss in the simulated damage tick.

inline bool ReplayHardpointHasBehavior(const ReplayHardpoint& hp, const std::string& name) {
    std::string needle = ReplayUpper(name);
    for (const auto& b : hp.behaviors) {
        if (ReplayUpper(b) == needle) return true;
    }
    return false;
}

inline bool ReplayUnitAnyHardpointHasBehavior(const ReplayUnitDetail& u, const std::string& name) {
    for (const auto& hp : u.hardpoints) {
        if (ReplayHardpointHasBehavior(hp, name)) return true;
    }
    return false;
}

inline bool ReplayUnitAllHardpointsHaveBehavior(const ReplayUnitDetail& u, const std::string& name) {
    if (u.hardpoints.empty()) return false;
    for (const auto& hp : u.hardpoints) {
        if (!ReplayHardpointHasBehavior(hp, name)) return false;
    }
    return true;
}

inline ReplayUnitDetail* ReplayFindUnit(ReplayState& s, uint64_t obj_addr) {
    auto it = s.units.find(obj_addr);
    return it == s.units.end() ? nullptr : &it->second;
}

inline const ReplayUnitDetail* ReplayFindUnit(const ReplayState& s, uint64_t obj_addr) {
    auto it = s.units.find(obj_addr);
    return it == s.units.end() ? nullptr : &it->second;
}

// Construct / upsert a unit. Used by both the test harness (to mock synthetic
// fixtures) and the snapshot loader (to rehydrate sections 11/12/13).
inline ReplayUnitDetail& ReplayMutMockUnit(
    ReplayState& s,
    uint64_t obj_addr,
    const std::string& type_name,
    int32_t owner_slot,
    float hull,
    float max_hull,
    uint32_t hardpoint_count
) {
    auto& u = s.units[obj_addr];
    u.obj_addr = obj_addr;
    u.type_name = type_name;
    u.owner_slot = owner_slot;
    u.hull = hull;
    u.max_hull = max_hull;
    if (u.hardpoints.size() != hardpoint_count) {
        u.hardpoints.clear();
        u.hardpoints.reserve(hardpoint_count);
        for (uint32_t i = 0; i < hardpoint_count; i++) {
            ReplayHardpoint hp;
            hp.index = i;
            u.hardpoints.push_back(std::move(hp));
        }
    }
    return u;
}

inline int ReplayMutAttachBehavior(ReplayState& s, uint64_t obj_addr, int hp_index, const std::string& behavior) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (hp_index < 0 || hp_index >= static_cast<int>(u->hardpoints.size())) return 0;
    auto& hp = u->hardpoints[static_cast<size_t>(hp_index)];
    if (!ReplayHardpointHasBehavior(hp, behavior)) {
        hp.behaviors.push_back(behavior);
    }
    return 1;
}

inline int ReplayMutDetachBehavior(ReplayState& s, uint64_t obj_addr, int hp_index, const std::string& behavior) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (hp_index < 0 || hp_index >= static_cast<int>(u->hardpoints.size())) return 0;
    auto& hp = u->hardpoints[static_cast<size_t>(hp_index)];
    std::string needle = ReplayUpper(behavior);
    auto before = hp.behaviors.size();
    hp.behaviors.erase(
        std::remove_if(
            hp.behaviors.begin(),
            hp.behaviors.end(),
            [&needle](const std::string& b) { return ReplayUpper(b) == needle; }),
        hp.behaviors.end());
    return hp.behaviors.size() != before ? 1 : 0;
}

// Simulate the real engine's Make_Invulnerable Lua wrapper:
// iterate every hardpoint and attach/detach the "INVULNERABLE" behavior.
// When `flag != 0`, behavior is attached; when 0, removed. This is the
// correct path for Task 99 — contrast with ReplayMutSetUnitInvulnFlag
// which only flips the display byte and does NOT confer immunity.
inline int ReplayMutMakeInvulnerable(ReplayState& s, uint64_t obj_addr, bool flag) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    static const std::string kBehavior = "INVULNERABLE";
    for (auto& hp : u->hardpoints) {
        if (flag) {
            if (!ReplayHardpointHasBehavior(hp, kBehavior)) {
                hp.behaviors.push_back(kBehavior);
            }
        } else {
            std::string needle = ReplayUpper(kBehavior);
            hp.behaviors.erase(
                std::remove_if(
                    hp.behaviors.begin(),
                    hp.behaviors.end(),
                    [&needle](const std::string& b) { return ReplayUpper(b) == needle; }),
                hp.behaviors.end());
        }
    }
    return 1;
}

// Flip the display-only invulnerability flag at +0x3A7. This matches the
// current Lua_SetUnitInvuln implementation; the damage simulator
// intentionally does NOT honour this flag (mirrors the 2026-04-23 live
// finding) so Task 99 regressions are detectable.
inline int ReplayMutSetUnitInvulnFlag(ReplayState& s, uint64_t obj_addr, uint8_t flag) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    u->invuln_flag = flag ? 1 : 0;
    return 1;
}

inline int ReplayMutSetPreventDeathBit(ReplayState& s, uint64_t obj_addr, bool set) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (set) u->prevent_death |= 0x80;
    else     u->prevent_death &= static_cast<uint8_t>(~0x80);
    return 1;
}

inline int ReplayMutSetUnitHull(ReplayState& s, uint64_t obj_addr, float value) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (u->max_hull > 0.0f && value > u->max_hull) value = u->max_hull;
    if (value < 0.0f) value = 0.0f;
    u->hull = value;
    return 1;
}

// Task 112 (2026-04-23). Pure-state mirror of the live event stream. Logging
// is a separate primitive from ReplayMutApplyDamage so future integrations
// (SetUnitHull hooks, scripted fixtures) can append entries without duplicating
// the timestamp handling. Declared BEFORE ReplayMutApplyDamage so the latter
// can call through without a forward declaration.
inline void ReplayMutLogDamageEvent(
    ReplayState& s,
    uint64_t obj_addr,
    int32_t owner_slot,
    float requested_hp,
    float current_hp,
    uint64_t timestamp_ms = 0) {
    ReplayState::DamageEventRecord ev;
    ev.timestamp_ms = timestamp_ms;
    ev.obj_addr     = obj_addr;
    ev.owner_slot   = owner_slot;
    ev.requested_hp = requested_hp;
    ev.current_hp   = current_hp;
    s.damage_event_log.push_back(ev);
}

inline std::string ReplayObsEventStreamDrain(ReplayState& s) {
    if (s.damage_event_log.empty()) return "count=0";
    std::string out;
    char header[32];
    int hlen = std::snprintf(header, sizeof(header), "count=%zu", s.damage_event_log.size());
    if (hlen > 0) out.append(header, static_cast<size_t>(hlen));
    for (const auto& ev : s.damage_event_log) {
        char row[192];
        int n = std::snprintf(
            row, sizeof(row),
            "|%llu;%llu;%d;%.3f;%.3f",
            static_cast<unsigned long long>(ev.timestamp_ms),
            static_cast<unsigned long long>(ev.obj_addr),
            static_cast<int>(ev.owner_slot),
            ev.requested_hp,
            ev.current_hp);
        if (n > 0) out.append(row, static_cast<size_t>(n));
    }
    s.damage_event_log.clear();
    return out;
}

inline int ReplayObsEventLogCount(const ReplayState& s) {
    return static_cast<int>(s.damage_event_log.size());
}

// Task 129 (2026-04-23) — per-slot and global damage multiplier mutation +
// observers. A negative slot selects the global value; any non-negative
// slot stores (or clears when mult == 1.0) the per-slot override.
// Declared BEFORE ReplayMutApplyDamage so damage application can call
// ReplayObsGetDamageMultiplier without a forward declaration.
inline int ReplayMutSetDamageMultiplier(ReplayState& s, int32_t slot, float mult) {
    if (mult < 0.0f) return 0;
    if (slot < 0) {
        s.global_damage_mult = mult;
        return 1;
    }
    if (mult == 1.0f) {
        s.per_slot_damage_mult.erase(slot);
    } else {
        s.per_slot_damage_mult[slot] = mult;
    }
    return 1;
}

inline float ReplayObsGetDamageMultiplier(const ReplayState& s, int32_t slot) {
    if (slot < 0) return s.global_damage_mult;
    auto it = s.per_slot_damage_mult.find(slot);
    if (it != s.per_slot_damage_mult.end()) return it->second;
    return s.global_damage_mult;
}

// Simulate a single damage tick. Damage application honours hardpoint
// behaviors only — any hardpoint with INVULNERABLE short-circuits the
// decrement. The `invuln_flag` and `prevent_death` fields are NOT
// checked, matching the 2026-04-23 live observation that flipping those
// bytes has no gameplay effect. Returns the post-tick hull value. Every
// call logs an entry into the Task 112 event stream so downstream
// observers can reconstruct the damage trace without extra bookkeeping.
//
// Task 129 (2026-04-23): incoming damage is scaled by the effective
// multiplier (per-slot if set, else global). The requested_hp field of
// the emitted event reflects the UNSCALED intent so the stream captures
// what the attacker asked for; consumers diff requested vs current to
// detect scaling (same way they detect god-mode clamping).
inline float ReplayMutApplyDamage(ReplayState& s, uint64_t obj_addr, float amount) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return -1.0f;
    float before = u->hull;
    if (amount <= 0.0f) {
        ReplayMutLogDamageEvent(s, obj_addr, u->owner_slot, before, before);
        return before;
    }
    if (ReplayUnitAnyHardpointHasBehavior(*u, "INVULNERABLE")) {
        ReplayMutLogDamageEvent(s, obj_addr, u->owner_slot, before - amount, before);
        return before;
    }
    float mult = ReplayObsGetDamageMultiplier(s, u->owner_slot);
    float scaled = amount * mult;
    if (scaled < 0.0f) scaled = 0.0f;
    u->hull -= scaled;
    if (u->hull < 0.0f) u->hull = 0.0f;
    ReplayMutLogDamageEvent(s, obj_addr, u->owner_slot, before - amount, u->hull);
    return u->hull;
}

inline int ReplayMutSetSelected(ReplayState& s, uint64_t obj_addr) {
    s.selected_units.clear();
    if (obj_addr != 0) s.selected_units.push_back(obj_addr);
    return 1;
}

inline int ReplayMutAppendSelected(ReplayState& s, uint64_t obj_addr) {
    if (obj_addr == 0) return 0;
    for (auto v : s.selected_units) {
        if (v == obj_addr) return 1;  // idempotent
    }
    s.selected_units.push_back(obj_addr);
    return 1;
}

inline int ReplayMutClearSelected(ReplayState& s) {
    s.selected_units.clear();
    return 1;
}

inline uint64_t ReplayObsGetSelectedUnit(const ReplayState& s) {
    return s.selected_units.empty() ? 0 : s.selected_units.front();
}

inline size_t ReplayObsSelectedCount(const ReplayState& s) {
    return s.selected_units.size();
}

// Task 111 (2026-04-23). Pure-state observer returning per-slot roster CSV
// that matches the live Lua_GetAllPlayers output. Row format:
//   slot;faction;credits;tech_level;is_human;is_local;unit_count
// Rows separated by '|'. Empty state returns "count=0".
//
// is_human and is_local are currently equivalent in the replay model — the
// harness carries `s.local_slot` but no per-player human flag. The live
// bridge distinguishes via PlayerObj+0x62 (LocalPlayer byte), which may
// differ from "this session's user" in multiplayer; the replay path
// deliberately collapses the two so a future UI gets a consistent shape.
inline std::string ReplayObsListAllPlayers(const ReplayState& s) {
    if (s.players.empty()) return "count=0";
    std::string out;
    char header[32];
    int hlen = std::snprintf(header, sizeof(header), "count=%zu", s.players.size());
    if (hlen > 0) out.append(header, static_cast<size_t>(hlen));
    for (const auto& p : s.players) {
        int is_local = (static_cast<int32_t>(p.slot) == s.local_slot) ? 1 : 0;
        int unit_count = 0;
        for (const auto& u : s.units) {
            if (u.second.owner_slot == static_cast<int32_t>(p.slot)) unit_count++;
        }
        char row[256];
        int n = std::snprintf(
            row, sizeof(row),
            "|%u;%s;%.3f;%d;%d;%d;%d",
            static_cast<unsigned>(p.slot),
            p.faction_name.empty() ? "UNKNOWN" : p.faction_name.c_str(),
            p.credits,
            p.tech_level,
            is_local,  // is_human (harness collapses)
            is_local,  // is_local
            unit_count);
        if (n > 0) out.append(row, static_cast<size_t>(n));
    }
    return out;
}

// Task 113 (2026-04-23). Pure-state mutation for fog-of-war reveal. The
// live engine exposes `FOW_Object:Reveal_All(player)` which calls
// sub_14035D4F0(TacticalGameManager, player_index). The replay harness
// mirrors the observable effect: a per-slot revealed bit that downstream
// observers can query.
inline int ReplayMutRevealAll(ReplayState& s, int32_t slot, bool enable) {
    if (slot < 0) return 0;
    auto it = std::find(s.revealed_slots.begin(), s.revealed_slots.end(), slot);
    if (enable) {
        if (it == s.revealed_slots.end()) s.revealed_slots.push_back(slot);
    } else {
        if (it != s.revealed_slots.end()) s.revealed_slots.erase(it);
    }
    return 1;
}

inline int ReplayObsIsRevealed(const ReplayState& s, int32_t slot) {
    if (slot < 0) return 0;
    return std::find(s.revealed_slots.begin(), s.revealed_slots.end(), slot)
               != s.revealed_slots.end() ? 1 : 0;
}

inline int ReplayObsRevealedCount(const ReplayState& s) {
    return static_cast<int>(s.revealed_slots.size());
}

// Sweep every mocked unit owned by `s.local_slot` and attach/remove the
// INVULNERABLE behavior on every hardpoint. Mirrors the live bridge's
// `SweepLocalUnitsInvulnerable` so Task 106 has a pure-state offline contract.
// Returns the number of units flipped (matches the live bridge's log line).
// Enemy-owned units (and unowned `-1`) are left untouched — enemy READ-ONLY
// discipline extends to God Mode: the feature never writes enemy state.
inline int ReplayMutSweepGodMode(ReplayState& s, bool enable) {
    if (s.local_slot < 0) return 0;
    int flipped = 0;
    for (auto& entry : s.units) {
        ReplayUnitDetail& u = entry.second;
        if (u.owner_slot != s.local_slot) continue;
        if (ReplayMutMakeInvulnerable(s, u.obj_addr, enable)) flipped++;
    }
    return flipped;
}

// Observer for God Mode state — returns 1 if every local-owned unit has the
// INVULNERABLE behavior attached to every hardpoint, 0 otherwise. Useful in
// tests to assert the sweep left the state coherent.
inline int ReplayObsGodModeFullyActive(const ReplayState& s) {
    if (s.local_slot < 0) return 0;
    int local_count = 0;
    for (const auto& entry : s.units) {
        const ReplayUnitDetail& u = entry.second;
        if (u.owner_slot != s.local_slot) continue;
        local_count++;
        if (u.hardpoints.empty()) return 0;
        if (!ReplayUnitAllHardpointsHaveBehavior(u, "INVULNERABLE")) return 0;
    }
    return local_count > 0 ? 1 : 0;
}

// Task 130 (2026-04-23) — shield set/get. The replay mirror clamps to
// max_shield when present (max==0 means "unit has no shield"); writes
// below zero floor at 0. Separate mutation (not folded into SetUnitHull)
// because the live bridge will call a different memory offset once the
// shield field is pinned, and the replay contract is used to assert the
// hull and shield paths are independent.
inline int ReplayMutSetUnitShield(ReplayState& s, uint64_t obj_addr, float value) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (u->max_shield > 0.0f && value > u->max_shield) value = u->max_shield;
    if (value < 0.0f) value = 0.0f;
    u->shield = value;
    return 1;
}

inline int ReplayMutSetUnitMaxShield(ReplayState& s, uint64_t obj_addr, float value) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (value < 0.0f) value = 0.0f;
    u->max_shield = value;
    // Snap current shield down if the new cap is smaller.
    if (u->shield > u->max_shield) u->shield = u->max_shield;
    return 1;
}

inline float ReplayObsGetUnitShield(const ReplayState& s, uint64_t obj_addr) {
    auto* u = ReplayFindUnit(s, obj_addr);
    return u ? u->shield : -1.0f;
}

inline float ReplayObsGetUnitMaxShield(const ReplayState& s, uint64_t obj_addr) {
    auto* u = ReplayFindUnit(s, obj_addr);
    return u ? u->max_shield : -1.0f;
}

// Task 134 (2026-04-23) — mark an existing mocked unit as a hero. Kept
// as a separate mutator from ReplayMutMockUnit because the snapshot
// loader may later want to flip is_hero without rewriting hardpoints.
inline int ReplayMutSetUnitIsHero(ReplayState& s, uint64_t obj_addr, bool is_hero) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    u->is_hero = is_hero;
    return 1;
}

inline int ReplayObsIsUnitHero(const ReplayState& s, uint64_t obj_addr) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return -1;  // unknown unit -- callers distinguish from false
    return u->is_hero ? 1 : 0;
}

// CSV shape: count=N|obj_addr;owner;hull;max_hull;respawn_ms;alive;respawn_enabled
// Selected/invuln/prevent_death are intentionally omitted -- the Hero Lab
// UI reads them separately via the existing ListTacticalUnits path and
// doesn't need them duplicated here. Respawn fields are 0/true by
// default on every mocked unit so the row is always well-formed even
// without a live hero-state capture.
inline std::string ReplayObsListHeroes(const ReplayState& s) {
    int count = 0;
    for (const auto& entry : s.units) {
        if (entry.second.is_hero) count++;
    }
    if (count == 0) return "count=0";
    std::string out;
    char header[32];
    int hlen = std::snprintf(header, sizeof(header), "count=%d", count);
    if (hlen > 0) out.append(header, static_cast<size_t>(hlen));
    for (const auto& entry : s.units) {
        const ReplayUnitDetail& u = entry.second;
        if (!u.is_hero) continue;
        bool alive = u.hull > 0.0f;
        char row[192];
        int n = std::snprintf(
            row, sizeof(row),
            "|%llu;%d;%.3f;%.3f;%d;%d;%d",
            static_cast<unsigned long long>(u.obj_addr),
            static_cast<int>(u.owner_slot),
            static_cast<double>(u.hull),
            static_cast<double>(u.max_hull),
            static_cast<int>(u.respawn_remaining_ms),
            alive ? 1 : 0,
            u.respawn_enabled ? 1 : 0);
        if (n > 0) out.append(row, static_cast<size_t>(n));
    }
    return out;
}

// Task 135 (2026-04-23) — respawn-timer set/get for heroes. The replay
// side sets an int32 milliseconds value; the Hero Lab slider emits this
// plus a small Lua wrapper that will write to the live hero state once
// that field is IDA-pinned. Non-hero units are rejected so a mistaken
// UI call doesn't corrupt a non-hero's unused respawn slot.
inline int ReplayMutSetHeroRespawnTimer(ReplayState& s, uint64_t obj_addr, int32_t ms) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (!u->is_hero) return 0;
    if (ms < 0) ms = 0;
    u->respawn_remaining_ms = ms;
    return 1;
}

inline int32_t ReplayObsGetHeroRespawnTimer(const ReplayState& s, uint64_t obj_addr) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u || !u->is_hero) return -1;
    return u->respawn_remaining_ms;
}

// Task 136 (2026-04-23) — permadeath toggle. respawn_enabled=false means
// once the hero dies, they stay dead. Non-hero units are rejected.
inline int ReplayMutSetPermadeath(ReplayState& s, uint64_t obj_addr, bool permadeath) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (!u->is_hero) return 0;
    u->respawn_enabled = !permadeath;
    return 1;
}

inline int ReplayObsIsPermadeath(const ReplayState& s, uint64_t obj_addr) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u || !u->is_hero) return -1;
    return u->respawn_enabled ? 0 : 1;
}

// Task 125 (2026-04-23) — locomotor speed set/get. Mirrors the shield
// pair in shape: clamp-to-max, floor-at-0, lowering max snaps current
// down. Kept as its own primitive (separate from SetUnitField-style
// generic setter) because the live memory path needs a two-level deref
// that a generic setter cannot safely perform.
inline int ReplayMutSetUnitSpeed(ReplayState& s, uint64_t obj_addr, float value) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (u->max_speed > 0.0f && value > u->max_speed) value = u->max_speed;
    if (value < 0.0f) value = 0.0f;
    u->speed = value;
    return 1;
}

inline int ReplayMutSetUnitMaxSpeed(ReplayState& s, uint64_t obj_addr, float value) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (value < 0.0f) value = 0.0f;
    u->max_speed = value;
    if (u->speed > u->max_speed) u->speed = u->max_speed;
    return 1;
}

inline float ReplayObsGetUnitSpeed(const ReplayState& s, uint64_t obj_addr) {
    auto* u = ReplayFindUnit(s, obj_addr);
    return u ? u->speed : -1.0f;
}

inline float ReplayObsGetUnitMaxSpeed(const ReplayState& s, uint64_t obj_addr) {
    auto* u = ReplayFindUnit(s, obj_addr);
    return u ? u->max_speed : -1.0f;
}

// Task 139 (2026-04-23) — abilities catalogue observer / mutation.
// ListAbilities row format: count=N|index;name;cooldown_ms;usable
// Empty unit returns "count=0". Unknown obj_addr returns "ERR: ..."
// (distinct sentinel from "count=0" so the UI can show a proper
// "select a unit" hint instead of an empty row).
inline int ReplayMutAddUnitAbility(
    ReplayState& s,
    uint64_t obj_addr,
    int32_t index,
    const std::string& name,
    int32_t cooldown_ms,
    bool usable
) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    for (auto& a : u->abilities) {
        if (a.index == index) {
            a.name = name;
            a.cooldown_remaining_ms = (cooldown_ms < 0) ? 0 : cooldown_ms;
            a.usable = usable;
            return 1;
        }
    }
    ReplayAbility ab;
    ab.index = index;
    ab.name = name;
    ab.cooldown_remaining_ms = (cooldown_ms < 0) ? 0 : cooldown_ms;
    ab.usable = usable;
    u->abilities.push_back(ab);
    return 1;
}

inline std::string ReplayObsListAbilities(const ReplayState& s, uint64_t obj_addr) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return "ERR: unknown obj_addr";
    if (u->abilities.empty()) return "count=0";
    std::string out;
    char header[32];
    int hlen = std::snprintf(header, sizeof(header), "count=%zu", u->abilities.size());
    if (hlen > 0) out.append(header, static_cast<size_t>(hlen));
    for (const auto& a : u->abilities) {
        char row[192];
        int n = std::snprintf(
            row, sizeof(row),
            "|%d;%s;%d;%d",
            static_cast<int>(a.index),
            a.name.empty() ? "UNKNOWN" : a.name.c_str(),
            static_cast<int>(a.cooldown_remaining_ms),
            a.usable ? 1 : 0);
        if (n > 0) out.append(row, static_cast<size_t>(n));
    }
    return out;
}

// Task 140 (2026-04-23) — TriggerAbility. Sets cooldown to the given
// post-trigger value (typically the ability's full cooldown) and flips
// usable=false. Returns 0 on unknown unit or unknown ability index.
// A separate ReplayMutTickAbilityCooldown advances time so tests can
// verify cooldown decay without a full engine tick simulation.
inline int ReplayMutTriggerAbility(
    ReplayState& s,
    uint64_t obj_addr,
    int32_t index,
    int32_t post_cooldown_ms
) {
    if (post_cooldown_ms < 0) return 0;
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    for (auto& a : u->abilities) {
        if (a.index == index) {
            if (!a.usable) return 0;  // already on cooldown — reject
            a.cooldown_remaining_ms = post_cooldown_ms;
            a.usable = (post_cooldown_ms == 0);
            return 1;
        }
    }
    return 0;
}

inline int ReplayMutTickAbilityCooldown(
    ReplayState& s,
    uint64_t obj_addr,
    int32_t delta_ms
) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    for (auto& a : u->abilities) {
        if (a.cooldown_remaining_ms > 0) {
            a.cooldown_remaining_ms -= delta_ms;
            if (a.cooldown_remaining_ms < 0) a.cooldown_remaining_ms = 0;
            if (a.cooldown_remaining_ms == 0) a.usable = true;
        }
    }
    return 1;
}

inline int32_t ReplayObsAbilityCooldown(const ReplayState& s, uint64_t obj_addr, int32_t index) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return -1;
    for (const auto& a : u->abilities) {
        if (a.index == index) return a.cooldown_remaining_ms;
    }
    return -1;
}

// Task 141 (2026-04-23) — galactic planet roster CSV observer.
// Row format: count=N|planet_name;owner_slot;corruption
// Empty state returns literal "count=0". Planets come from the v2
// snapshot section 6 (already loaded by replay_harness.cpp); this
// observer is just a formatter, not a new data source.
//
// Task 142 (2026-04-23) — ChangePlanetOwner mutation. Looks up the
// planet by case-insensitive name (matching the existing PlanetCorruption
// store) and updates owner_slot. Returns 0 on unknown planet or negative
// slot; otherwise returns 1. The live bridge routes through a Lua call
// to `Planet:Change_Owner(slot)` once the galactic API is verified; the
// replay contract ships ahead so the Galactic tab can be tested offline.
inline std::string ReplayObsListPlanets(const ReplayState& s) {
    if (s.planets.empty()) return "count=0";
    std::string out;
    char header[32];
    int hlen = std::snprintf(header, sizeof(header), "count=%zu", s.planets.size());
    if (hlen > 0) out.append(header, static_cast<size_t>(hlen));
    for (const auto& entry : s.planets) {
        const ReplayPlanetInfo& p = entry.second;
        // Use the stored (original-case) name when available so UI
        // rendering preserves the map's "NABOO" / "Kamino" capitalisation;
        // fall back to the uppercased key when the original is missing.
        const std::string& display = p.name.empty() ? entry.first : p.name;
        char row[192];
        int n = std::snprintf(
            row, sizeof(row),
            "|%s;%d;%.3f",
            display.c_str(),
            static_cast<int>(p.owner_slot),
            static_cast<double>(p.corruption));
        if (n > 0) out.append(row, static_cast<size_t>(n));
    }
    return out;
}

inline int ReplayMutChangePlanetOwner(ReplayState& s, const std::string& planet_name, int32_t new_slot) {
    if (new_slot < 0) return 0;
    std::string key = ReplayUpper(planet_name);
    auto it = s.planets.find(key);
    if (it == s.planets.end()) return 0;
    it->second.owner_slot = new_slot;
    return 1;
}

inline int32_t ReplayObsGetPlanetOwner(const ReplayState& s, const std::string& planet_name) {
    std::string key = ReplayUpper(planet_name);
    auto it = s.planets.find(key);
    if (it == s.planets.end()) return -1;
    return it->second.owner_slot;
}

// Task 143 (2026-04-23). Tech / building / capital observers + mutations.
// tech_level in SWFOC is 1..5 but the replay mirror doesn't clamp — a
// caller can store any int32 so fixtures can stress out-of-range values
// without the harness swallowing them. ReplayMutSetPlanetTech returns 0
// on unknown planet so case-insensitive keying stays consistent with
// ReplayMutChangePlanetOwner.
inline int ReplayMutSetPlanetTech(ReplayState& s, const std::string& planet_name, int32_t tech) {
    auto it = s.planets.find(ReplayUpper(planet_name));
    if (it == s.planets.end()) return 0;
    it->second.tech_level = tech;
    return 1;
}

inline int ReplayMutSetPlanetBuildings(ReplayState& s, const std::string& planet_name, int32_t count) {
    if (count < 0) return 0;
    auto it = s.planets.find(ReplayUpper(planet_name));
    if (it == s.planets.end()) return 0;
    it->second.building_count = count;
    return 1;
}

inline int ReplayMutSetPlanetCapital(ReplayState& s, const std::string& planet_name, bool is_capital) {
    auto it = s.planets.find(ReplayUpper(planet_name));
    if (it == s.planets.end()) return 0;
    it->second.is_capital = is_capital;
    return 1;
}

inline int32_t ReplayObsGetPlanetTech(const ReplayState& s, const std::string& planet_name) {
    auto it = s.planets.find(ReplayUpper(planet_name));
    if (it == s.planets.end()) return -1;
    return it->second.tech_level;
}

inline int32_t ReplayObsGetPlanetBuildings(const ReplayState& s, const std::string& planet_name) {
    auto it = s.planets.find(ReplayUpper(planet_name));
    if (it == s.planets.end()) return -1;
    return it->second.building_count;
}

// Combined CSV row: tech;buildings;is_capital. Returns "" on unknown
// planet so callers can distinguish from a legitimately tech=0 response.
inline std::string ReplayObsGetPlanetTechAndBuildings(const ReplayState& s, const std::string& planet_name) {
    auto it = s.planets.find(ReplayUpper(planet_name));
    if (it == s.planets.end()) return "";
    char row[96];
    int n = std::snprintf(
        row, sizeof(row),
        "%d;%d;%d",
        static_cast<int>(it->second.tech_level),
        static_cast<int>(it->second.building_count),
        it->second.is_capital ? 1 : 0);
    return (n > 0) ? std::string(row, static_cast<size_t>(n)) : std::string();
}

// Task 138 (2026-04-23) — hero stat-edit dispatcher. Routes (field, value)
// to the appropriate per-field mutation. Defined AFTER all per-field
// helpers so the dispatcher can call them without forward declarations.
// Unlike the individual mutators this dispatcher requires is_hero=true
// so the Hero Lab contract stays distinct from the Inspector.
inline int ReplayMutHeroStatEdit(
    ReplayState& s,
    uint64_t obj_addr,
    const std::string& field,
    float value
) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (!u->is_hero) return 0;
    if (field == "hull")        return ReplayMutSetUnitHull(s, obj_addr, value);
    if (field == "shield")      return ReplayMutSetUnitShield(s, obj_addr, value);
    if (field == "max_shield")  return ReplayMutSetUnitMaxShield(s, obj_addr, value);
    if (field == "speed")       return ReplayMutSetUnitSpeed(s, obj_addr, value);
    if (field == "max_speed")   return ReplayMutSetUnitMaxSpeed(s, obj_addr, value);
    if (field == "respawn_ms")  return ReplayMutSetHeroRespawnTimer(
                                       s, obj_addr, static_cast<int32_t>(value));
    return 0;
}

// Task 157 (2026-04-23) — Generic unit-field setter. Extends the
// #138 HeroStatEdit dispatcher to the full unit-field surface (hull,
// shields, speeds, attack_power, invuln/prevent-death flags, hero
// state). Unlike HeroStatEdit this one does NOT require is_hero;
// the caller picks fields appropriate for the unit kind.
// Returns 1 on success, 0 on unknown field or unit-not-found.
//
// Field name taxonomy (Phase 1):
//   float:   hull, max_hull, shield, max_shield, speed, max_speed,
//            attack_power
//   bool:    invuln_flag (0/1), prevent_death (0/1), is_hero (0/1),
//            respawn_enabled (0/1)
//   int:     respawn_ms (int32 cast of value)
//
// The `owner_slot` field is explicitly NOT in this dispatcher — the
// replay_state.h model treats ownership as immutable via the generic
// setter (ReplayMutSetOwnerSlot exists for the KillOrRevive path but
// the dispatcher won't expose it, because live ownership changes
// require the engine's full Change_Owner side-effects which this
// dispatcher doesn't attempt).
//
// Helper for max_hull; not previously needed but the generic setter
// completes the hull/max_hull pair.
inline int ReplayMutSetUnitMaxHull(ReplayState& s, uint64_t obj_addr, float value) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (value < 0.0f) return 0;
    u->max_hull = value;
    if (u->hull > u->max_hull) u->hull = u->max_hull;   // clamp-down
    return 1;
}

// NOTE: ReplayMutSetUnitIsHero already exists above (added for Task 134);
// the dispatcher below reuses that existing helper.

// Direct setter for attack_power (for the generic dispatcher). Rejects
// negative values; 0.0 is allowed (unit can't damage anything).
inline int ReplayMutSetUnitAttackPower(ReplayState& s, uint64_t obj_addr, float value) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (value < 0.0f) return 0;
    u->attack_power = value;
    return 1;
}

// Direct setter for respawn_enabled. Mirrors SetPermadeath inverse.
inline int ReplayMutSetUnitRespawnEnabled(ReplayState& s, uint64_t obj_addr, bool enabled) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    u->respawn_enabled = enabled;
    return 1;
}

inline int ReplayMutSetUnitField(
    ReplayState& s,
    uint64_t obj_addr,
    const std::string& field,
    float value
) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    // Float fields
    if (field == "hull")            return ReplayMutSetUnitHull(s, obj_addr, value);
    if (field == "max_hull")        return ReplayMutSetUnitMaxHull(s, obj_addr, value);
    if (field == "shield")          return ReplayMutSetUnitShield(s, obj_addr, value);
    if (field == "max_shield")      return ReplayMutSetUnitMaxShield(s, obj_addr, value);
    if (field == "speed")           return ReplayMutSetUnitSpeed(s, obj_addr, value);
    if (field == "max_speed")       return ReplayMutSetUnitMaxSpeed(s, obj_addr, value);
    if (field == "attack_power")    return ReplayMutSetUnitAttackPower(s, obj_addr, value);
    // Int field
    if (field == "respawn_ms")      return ReplayMutSetHeroRespawnTimer(
                                               s, obj_addr, static_cast<int32_t>(value));
    // Bool fields (value treated as 0/!=0)
    if (field == "invuln_flag")     return ReplayMutSetUnitInvulnFlag(
                                               s, obj_addr, value != 0.0f ? 1 : 0);
    if (field == "prevent_death")   return ReplayMutSetPreventDeathBit(
                                               s, obj_addr, value != 0.0f);
    if (field == "is_hero")         return ReplayMutSetUnitIsHero(
                                               s, obj_addr, value != 0.0f);
    if (field == "respawn_enabled") return ReplayMutSetUnitRespawnEnabled(
                                               s, obj_addr, value != 0.0f);
    return 0;
}

// Observer counterpart: return the current value of a named field, or
// a sentinel for unknown field / unit. Booleans return 0.0/1.0; ints
// return their int value cast to float. Unknown field returns NaN so
// callers can distinguish from a legitimate 0.0. Unit-not-found
// returns NaN as well.
inline float ReplayObsGetUnitField(
    const ReplayState& s,
    uint64_t obj_addr,
    const std::string& field
) {
    const auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return std::numeric_limits<float>::quiet_NaN();
    if (field == "hull")            return u->hull;
    if (field == "max_hull")        return u->max_hull;
    if (field == "shield")          return u->shield;
    if (field == "max_shield")      return u->max_shield;
    if (field == "speed")           return u->speed;
    if (field == "max_speed")       return u->max_speed;
    if (field == "attack_power")    return u->attack_power;
    if (field == "respawn_ms")      return static_cast<float>(u->respawn_remaining_ms);
    if (field == "invuln_flag")     return static_cast<float>(u->invuln_flag);
    if (field == "prevent_death")   return static_cast<float>(u->prevent_death);
    if (field == "is_hero")         return u->is_hero ? 1.0f : 0.0f;
    if (field == "respawn_enabled") return u->respawn_enabled ? 1.0f : 0.0f;
    if (field == "owner_slot")      return static_cast<float>(u->owner_slot);  // read-only peek
    return std::numeric_limits<float>::quiet_NaN();
}

// Task 137 (2026-04-23) — Kill / revive a single unit.
// KillUnit drives hull to zero via the normal damage path so the event
// stream captures the transition. Revive writes hull back to max_hull;
// an already-dead unit must be explicitly revived (kill does not
// unset ``prevent_death`` or alter behaviors -- it is strictly a hull
// delta, matching what the live SetHP path does).
// Enemy READ-ONLY protection lives in the LIVE helpers, not in the
// pure-state mutation -- the replay harness is a state oracle, not
// an authorisation layer. Callers that want to enforce the rule use
// the owner_slot check before calling the mutation.
inline int ReplayMutKillUnit(ReplayState& s, uint64_t obj_addr) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (u->hull <= 0.0f) return 0;  // already dead -- treat as no-op
    float before = u->hull;
    u->hull = 0.0f;
    ReplayMutLogDamageEvent(s, obj_addr, u->owner_slot, 0.0f, 0.0f);
    (void)before;  // reserved for a future "killed_from_hull" field
    return 1;
}

inline int ReplayMutReviveUnit(ReplayState& s, uint64_t obj_addr) {
    auto* u = ReplayFindUnit(s, obj_addr);
    if (!u) return 0;
    if (u->max_hull <= 0.0f) return 0;  // cannot revive without max_hull
    if (u->hull >= u->max_hull) return 0;  // already full -- no-op
    u->hull = u->max_hull;
    ReplayMutLogDamageEvent(s, obj_addr, u->owner_slot, u->max_hull, u->max_hull);
    return 1;
}

// Task 123 (2026-04-23) — income multiplier. Mirrors the DamageMultiplier
// shape: negative slots select the global value; non-negative slots store
// per-slot overrides; mult == 1.0 erases the per-slot entry so diagnostic
// output doesn't drown in noise.
inline int ReplayMutSetIncomeMultiplier(ReplayState& s, int32_t slot, float mult) {
    if (mult < 0.0f) return 0;
    if (slot < 0) {
        s.global_income_mult = mult;
        return 1;
    }
    if (mult == 1.0f) s.per_slot_income_mult.erase(slot);
    else              s.per_slot_income_mult[slot] = mult;
    return 1;
}

inline float ReplayObsGetIncomeMultiplier(const ReplayState& s, int32_t slot) {
    if (slot < 0) return s.global_income_mult;
    auto it = s.per_slot_income_mult.find(slot);
    if (it != s.per_slot_income_mult.end()) return it->second;
    return s.global_income_mult;
}

// Task 124 (2026-04-23) — build-speed multiplier (per-slot + global).
// Same reject/clear-on-1.0 shape as income mult.
inline int ReplayMutSetBuildSpeed(ReplayState& s, int32_t slot, float mult) {
    if (mult < 0.0f) return 0;
    if (slot < 0) {
        s.global_build_speed_mult = mult;
        return 1;
    }
    if (mult == 1.0f) s.per_slot_build_speed_mult.erase(slot);
    else              s.per_slot_build_speed_mult[slot] = mult;
    return 1;
}

inline float ReplayObsGetBuildSpeed(const ReplayState& s, int32_t slot) {
    if (slot < 0) return s.global_build_speed_mult;
    auto it = s.per_slot_build_speed_mult.find(slot);
    if (it != s.per_slot_build_speed_mult.end()) return it->second;
    return s.global_build_speed_mult;
}

// Task 126 (2026-04-23) — per-faction move-speed multiplier. Shape
// matches build-speed. Task 125's per-UNIT SetUnitSpeed is independent
// (direct unit speed) while this applies a multiplier across all units
// owned by the target slot on the next tick. Effective multiplier for
// a unit's slot is what ReplayObsGetFactionSpeedMult returns.
inline int ReplayMutSetFactionSpeedMult(ReplayState& s, int32_t slot, float mult) {
    if (mult < 0.0f) return 0;
    if (slot < 0) {
        s.global_faction_speed_mult = mult;
        return 1;
    }
    if (mult == 1.0f) s.per_faction_speed_mult.erase(slot);
    else              s.per_faction_speed_mult[slot] = mult;
    return 1;
}

inline float ReplayObsGetFactionSpeedMult(const ReplayState& s, int32_t slot) {
    if (slot < 0) return s.global_faction_speed_mult;
    auto it = s.per_faction_speed_mult.find(slot);
    if (it != s.per_faction_speed_mult.end()) return it->second;
    return s.global_faction_speed_mult;
}

// Task 127 (2026-04-23) — global game speed. Negative values rejected
// (no time reversal), 0.0 supported (pause semantics). Stored as a
// single global float — no per-slot concept.
inline int ReplayMutSetGameSpeed(ReplayState& s, float speed) {
    if (speed < 0.0f) return 0;
    s.global_game_speed = speed;
    return 1;
}

inline float ReplayObsGetGameSpeed(const ReplayState& s) {
    return s.global_game_speed;
}

// Task 122 (2026-04-23) — freeze credits. Non-negative slot + value stores
// the frozen target; passing `false` for `enable` removes the freeze and
// discards the stored target.
inline int ReplayMutSetFreezeCredits(ReplayState& s, int32_t slot, bool enable, double target) {
    if (slot < 0) return 0;
    if (enable) {
        if (target < 0.0) return 0;
        s.frozen_credits_targets[slot] = target;
    } else {
        s.frozen_credits_targets.erase(slot);
    }
    return 1;
}

inline int ReplayObsIsFreezeCredits(const ReplayState& s, int32_t slot) {
    if (slot < 0) return 0;
    return s.frozen_credits_targets.count(slot) ? 1 : 0;
}

inline double ReplayObsGetFreezeCreditsTarget(const ReplayState& s, int32_t slot) {
    if (slot < 0) return -1.0;
    auto it = s.frozen_credits_targets.find(slot);
    return (it == s.frozen_credits_targets.end()) ? -1.0 : it->second;
}

// Apply one simulated income tick: for each player, add
// base_income_per_tick * effective_income_multiplier(slot) * game_speed
// to the player's credits, UNLESS that slot is frozen (in which case
// credits snap back to the frozen target). Returns the number of
// players whose credits were touched.
inline int ReplayMutTickIncome(ReplayState& s, double base_income_per_tick) {
    int touched = 0;
    for (auto& p : s.players) {
        int32_t slot = static_cast<int32_t>(p.slot);
        auto frozen_it = s.frozen_credits_targets.find(slot);
        if (frozen_it != s.frozen_credits_targets.end()) {
            p.credits = frozen_it->second;
            touched++;
            continue;
        }
        float mult = ReplayObsGetIncomeMultiplier(s, slot);
        double delta = base_income_per_tick
                     * static_cast<double>(mult)
                     * static_cast<double>(s.global_game_speed);
        if (delta != 0.0) {
            p.credits += delta;
            touched++;
        }
    }
    return touched;
}

// Task 131 (2026-04-23) — weapon fire-rate multiplier. Same shape as
// damage_mult: negative slot writes the global; slot >= 0 writes the
// per-slot override (clear-on-1.0). ReplayMutApplyFireRate divides a
// caller-provided base cooldown by the effective multiplier so higher
// multiplier = faster fire. Reject negative multipliers (engine uses
// unsigned cooldown timers). A multiplier of 0 is explicitly rejected
// because dividing a cooldown by zero produces undefined behavior in
// the engine path the Phase 2 hook will sit on.
inline int ReplayMutSetFireRate(ReplayState& s, int32_t slot, float mult) {
    if (mult <= 0.0f) return 0;
    if (slot < 0) {
        s.global_fire_rate_mult = mult;
        return 1;
    }
    if (mult == 1.0f) {
        s.per_slot_fire_rate_mult.erase(slot);
    } else {
        s.per_slot_fire_rate_mult[slot] = mult;
    }
    return 1;
}

inline float ReplayObsGetFireRate(const ReplayState& s, int32_t slot) {
    if (slot < 0) return s.global_fire_rate_mult;
    auto it = s.per_slot_fire_rate_mult.find(slot);
    if (it != s.per_slot_fire_rate_mult.end()) return it->second;
    return s.global_fire_rate_mult;
}

// Given a base cooldown in ms, return the scaled cooldown honouring the
// effective fire-rate multiplier for `slot`. Returns 0.0f for a negative
// base cooldown (nonsensical input). The contract: 3× multiplier means
// the weapon fires three times in the window a 1× weapon fires once.
inline float ReplayMutApplyFireRate(const ReplayState& s, int32_t slot, float base_cooldown_ms) {
    if (base_cooldown_ms < 0.0f) return 0.0f;
    float mult = ReplayObsGetFireRate(s, slot);
    if (mult <= 0.0f) return base_cooldown_ms;  // safety fallback
    return base_cooldown_ms / mult;
}

// Task 132 (2026-04-23) — area-damage toggle. Global bool with a
// configurable falloff factor (splash_amount = primary_amount * falloff).
// radius is carried for Phase 2 when position data lands in the replay
// state; Phase 1 treats every OTHER unit on the map as in-range.
inline int ReplayMutSetAreaDamageEnabled(ReplayState& s, bool enabled) {
    s.area_damage_enabled = enabled;
    return 1;
}

inline bool ReplayObsIsAreaDamageEnabled(const ReplayState& s) {
    return s.area_damage_enabled;
}

// Splash the primary damage amount onto every OTHER unit on the map.
// Honours hardpoint invulnerability (same INVULNERABLE shortcut as
// ReplayMutApplyDamage) AND the damage multiplier of each splash target
// (so enabling both 2× damage and area damage causes splash scaled at
// 2× per victim). Emits a damage-stream event per victim with
// requested_hp reflecting the UNSCALED splash intent. Returns the
// number of units that actually took splash damage. When area damage
// is disabled this is a no-op that returns 0.
inline int ReplayMutApplyAreaSplash(ReplayState& s, uint64_t primary_obj_addr, float primary_amount) {
    if (!s.area_damage_enabled) return 0;
    if (primary_amount <= 0.0f) return 0;
    float splash = primary_amount * s.area_damage_falloff;
    if (splash <= 0.0f) return 0;
    int affected = 0;
    for (auto& entry : s.units) {
        uint64_t addr = entry.first;
        if (addr == primary_obj_addr) continue;     // primary target already took damage
        ReplayUnitDetail& u = entry.second;
        float before = u.hull;
        if (ReplayUnitAnyHardpointHasBehavior(u, "INVULNERABLE")) {
            ReplayMutLogDamageEvent(s, addr, u.owner_slot, before - splash, before);
            continue;
        }
        float mult = ReplayObsGetDamageMultiplier(s, u.owner_slot);
        float scaled = splash * mult;
        if (scaled < 0.0f) scaled = 0.0f;
        u.hull -= scaled;
        if (u.hull < 0.0f) u.hull = 0.0f;
        ReplayMutLogDamageEvent(s, addr, u.owner_slot, before - splash, u.hull);
        affected++;
    }
    return affected;
}

// Task 133 (2026-04-23) — per-slot target-filter bitmask. Default (when
// a slot is absent from the map) is TARGET_ALL; setting TARGET_ALL
// explicitly erases the entry to keep the state canonical. Any other
// bitmask value stores the override. Bitmask values outside the 3-bit
// space are masked down to the known bits — passing 0xFF becomes 0x7.
inline int ReplayMutSetTargetFilter(ReplayState& s, int32_t slot, uint32_t bitmask) {
    if (slot < 0) return 0;
    uint32_t masked = bitmask & ReplayState::TARGET_ALL;
    if (masked == ReplayState::TARGET_ALL) {
        s.per_slot_target_filter.erase(slot);
    } else {
        s.per_slot_target_filter[slot] = masked;
    }
    return 1;
}

inline uint32_t ReplayObsGetTargetFilter(const ReplayState& s, int32_t slot) {
    if (slot < 0) return ReplayState::TARGET_ALL;
    auto it = s.per_slot_target_filter.find(slot);
    if (it != s.per_slot_target_filter.end()) return it->second;
    return ReplayState::TARGET_ALL;
}

// Check whether an attacker owned by `attacker_slot` is allowed to fire
// on a target of `target_kind` (one of ReplayState::TARGET_ENEMY,
// TARGET_FRIENDLY, TARGET_NEUTRAL). Returns true when the bit is set.
// The helper exposes both the stored filter AND the engine-natural
// question the Phase 2 hook will ask: "can I shoot this?". Passing a
// target_kind that isn't a single-bit value returns false (defensive
// against accidental ALL-checks).
inline bool ReplayObsIsTargetAllowed(const ReplayState& s, int32_t attacker_slot, uint32_t target_kind) {
    if (target_kind == 0) return false;
    if (target_kind & (target_kind - 1)) return false;  // not a single bit
    if ((target_kind & ReplayState::TARGET_ALL) == 0) return false;
    uint32_t filter = ReplayObsGetTargetFilter(s, attacker_slot);
    return (filter & target_kind) != 0;
}

// Task 159 (2026-04-23) — SpawnUnit wrapper over ReplayMutMockUnit.
// The live helper will call the engine's Spawn_Unit Lua function at
// the provided world-space position; the replay mirror models only the
// unit-count semantics since Phase 1 has no position data yet. Returns
// the count of units actually spawned (some may fail — e.g., type=""
// rejected). obj_addrs are synthesized from a base sentinel + count.
inline int ReplayMutSpawnUnits(
    ReplayState& s,
    const std::string& type_name,
    int32_t owner_slot,
    int32_t count
) {
    if (count <= 0) return 0;
    if (type_name.empty()) return 0;
    if (owner_slot < 0) return 0;
    int spawned = 0;
    // Synthesize a deterministic obj_addr range so tests can address the
    // freshly-spawned units. Base is 0xDEADBEEF00000000 OR'd with the
    // current unit count so consecutive spawns don't collide.
    uint64_t base = 0xDEADBEEF00000000ULL | static_cast<uint64_t>(s.units.size());
    for (int i = 0; i < count; i++) {
        uint64_t addr = base + static_cast<uint64_t>(i);
        ReplayMutMockUnit(s, addr, type_name, owner_slot, 100.0f, 100.0f, 0);
        spawned++;
    }
    return spawned;
}

// Task 160 (2026-04-23) — per-slot build-cost multiplier. Same shape as
// income_mult / damage_mult: negative slot writes global, clear-on-1.0
// for per-slot. Negative multiplier rejected (0.0 is ALLOWED — it's the
// "free build" configuration). The live hook will multiply the engine's
// credit-deduction amount by this value at the build-progress call site.
inline int ReplayMutSetBuildCost(ReplayState& s, int32_t slot, float mult) {
    if (mult < 0.0f) return 0;
    if (slot < 0) {
        s.global_build_cost_mult = mult;
        return 1;
    }
    if (mult == 1.0f) {
        s.per_slot_build_cost_mult.erase(slot);
    } else {
        s.per_slot_build_cost_mult[slot] = mult;
    }
    return 1;
}

inline float ReplayObsGetBuildCost(const ReplayState& s, int32_t slot) {
    if (slot < 0) return s.global_build_cost_mult;
    auto it = s.per_slot_build_cost_mult.find(slot);
    if (it != s.per_slot_build_cost_mult.end()) return it->second;
    return s.global_build_cost_mult;
}

// Task 163 (2026-04-23) — per-slot unit-cap override. cap == -1 means
// unlimited; other negative values rejected. Writing cap == 0 is valid
// (prevents building any unit). There's no "clear" sentinel other than
// explicit erase via the reset helper below — we don't overload a
// specific cap value as "clear" because any engine-meaningful cap is
// potentially valid.
inline int ReplayMutSetUnitCapOverride(ReplayState& s, int32_t slot, int32_t cap) {
    if (slot < 0) return 0;
    if (cap < -1) return 0;  // only -1 is the unlimited sentinel
    s.per_slot_unit_cap_override[slot] = cap;
    return 1;
}

inline int ReplayMutClearUnitCapOverride(ReplayState& s, int32_t slot) {
    if (slot < 0) return 0;
    s.per_slot_unit_cap_override.erase(slot);
    return 1;
}

// Returns the override value when set, or -2 when no override exists
// (caller uses -2 as the "use engine default" signal). -1 means
// unlimited (explicit override), 0 means "no building allowed".
inline int32_t ReplayObsGetUnitCapOverride(const ReplayState& s, int32_t slot) {
    if (slot < 0) return -2;
    auto it = s.per_slot_unit_cap_override.find(slot);
    if (it != s.per_slot_unit_cap_override.end()) return it->second;
    return -2;
}

// Task 161 (2026-04-23) — Instant-build toggle + "should the next
// build tick complete this unit?" observer. Phase 2's AOB patch NOPs
// the progress-increment instruction so the queued unit jumps to
// complete on the first tick; the replay mirror exposes the predicate
// directly as `ReplayObsShouldBuildComplete(s, queue_time_ms, elapsed_ms)`
// which returns true when instant_build_enabled (regardless of elapsed
// time) OR when elapsed_ms >= queue_time_ms (normal completion).
inline int ReplayMutSetInstantBuild(ReplayState& s, bool enable) {
    s.instant_build_enabled = enable;
    return 1;
}

inline bool ReplayObsIsInstantBuildEnabled(const ReplayState& s) {
    return s.instant_build_enabled;
}

inline bool ReplayObsShouldBuildComplete(
    const ReplayState& s,
    int32_t queue_time_ms,
    int32_t elapsed_ms
) {
    if (s.instant_build_enabled) return true;
    if (queue_time_ms <= 0) return true;             // zero-cost build always completes
    return elapsed_ms >= queue_time_ms;
}

// Task 162 (2026-04-23) — Free-build toggle + cost-computation observer.
// Free-build wins over the per-slot multiplier (#160) so the engine
// tick shortcut-path doesn't even evaluate the mult. Returns the
// amount that SHOULD be deducted given a base_cost and slot; when
// free_build_enabled returns 0.
inline int ReplayMutSetFreeBuild(ReplayState& s, bool enable) {
    s.free_build_enabled = enable;
    return 1;
}

inline bool ReplayObsIsFreeBuildEnabled(const ReplayState& s) {
    return s.free_build_enabled;
}

inline float ReplayObsComputeBuildCost(
    const ReplayState& s,
    int32_t slot,
    float base_cost
) {
    if (s.free_build_enabled) return 0.0f;
    if (base_cost < 0.0f) return 0.0f;
    float mult = ReplayObsGetBuildCost(s, slot);
    return base_cost * mult;
}

// Task 114 (2026-04-23) — per-slot AI freeze. Set-membership model:
// present in frozen_ai_slots = AI frozen for that slot. Calling
// SetFrozen(slot, true) when already frozen is idempotent (no dup).
// Calling SetFrozen(slot, false) when not frozen is idempotent (no-op).
// Negative slot rejected (returns 0); observer on negative slot returns
// false (unfrozen) since the set only holds valid slot IDs.
inline int ReplayMutSetAiFrozen(ReplayState& s, int32_t slot, bool frozen) {
    if (slot < 0) return 0;
    auto it = std::find(s.frozen_ai_slots.begin(), s.frozen_ai_slots.end(), slot);
    if (frozen) {
        if (it == s.frozen_ai_slots.end()) s.frozen_ai_slots.push_back(slot);
    } else {
        if (it != s.frozen_ai_slots.end()) s.frozen_ai_slots.erase(it);
    }
    return 1;
}

inline bool ReplayObsIsAiFrozen(const ReplayState& s, int32_t slot) {
    if (slot < 0) return false;
    return std::find(s.frozen_ai_slots.begin(), s.frozen_ai_slots.end(), slot)
        != s.frozen_ai_slots.end();
}

inline int ReplayObsFrozenAiCount(const ReplayState& s) {
    return static_cast<int>(s.frozen_ai_slots.size());
}

// Task 115 (2026-04-23) — camera unlock + position/rot/zoom. Setting
// unlocked to false does NOT reset the stored pose — the pose remains
// as a last-known-good state, which matches what the Phase 2 hook will
// do on toggle-off (restore the engine's own camera without clobbering
// the user's saved pose for next toggle-on). Pos/rot/zoom observers
// return the stored state regardless of unlocked; they're pure
// accessors. SetCameraPos accepts any float — the engine clamps its
// own; the replay mirror doesn't bound-check because we don't have
// the clamp ranges pinned yet.
inline int ReplayMutSetCameraUnlocked(ReplayState& s, bool unlocked) {
    s.cam_unlocked = unlocked;
    return 1;
}

inline bool ReplayObsIsCameraUnlocked(const ReplayState& s) {
    return s.cam_unlocked;
}

inline int ReplayMutSetCameraPos(ReplayState& s, float x, float y, float z) {
    s.cam_pos_x = x;
    s.cam_pos_y = y;
    s.cam_pos_z = z;
    return 1;
}

inline float ReplayObsGetCameraX(const ReplayState& s) { return s.cam_pos_x; }
inline float ReplayObsGetCameraY(const ReplayState& s) { return s.cam_pos_y; }
inline float ReplayObsGetCameraZ(const ReplayState& s) { return s.cam_pos_z; }

inline int ReplayMutSetCameraRot(ReplayState& s, float rot)   { s.cam_rot  = rot;  return 1; }
inline int ReplayMutSetCameraZoom(ReplayState& s, float zoom) {
    if (zoom <= 0.0f) return 0;   // zero or negative zoom is nonsensical
    s.cam_zoom = zoom;
    return 1;
}
inline float ReplayObsGetCameraRot(const ReplayState& s)  { return s.cam_rot; }
inline float ReplayObsGetCameraZoom(const ReplayState& s) { return s.cam_zoom; }

// Task 105 (2026-04-23) — OHK (one-hit-kill) toggle over local units.
// Pattern mirrors ReplayMutSweepGodMode: iterate every local-owned unit,
// save its current attack_power into ohk_saved_attack_powers on enable,
// then set attack_power to the inflated sentinel. On disable, restore
// the saved values and clear the map. Enemy units (owner_slot !=
// local_slot) are NEVER touched — the same READ-ONLY gate God Mode
// uses. Returns the number of units that were actually flipped.
//
// Snapshot/restore discipline: enabling twice is a no-op (the second
// enable would overwrite the snapshots with the already-inflated
// values; guarded by checking ohk_enabled first). Disabling without
// a prior enable is a no-op.
inline int ReplayMutSetOHK(ReplayState& s, bool enable) {
    if (s.local_slot < 0) return 0;
    if (enable == s.ohk_enabled) return 0;  // idempotent — already in target state
    int flipped = 0;
    if (enable) {
        s.ohk_saved_attack_powers.clear();
        for (auto& entry : s.units) {
            ReplayUnitDetail& u = entry.second;
            if (u.owner_slot != s.local_slot) continue;
            s.ohk_saved_attack_powers[u.obj_addr] = u.attack_power;
            u.attack_power = ReplayState::kOhkInflatedAttackPower;
            flipped++;
        }
        s.ohk_enabled = true;
    } else {
        for (auto& pair : s.ohk_saved_attack_powers) {
            uint64_t addr = pair.first;
            float    saved = pair.second;
            auto* u = ReplayFindUnit(s, addr);
            if (!u) continue;                        // unit despawned during OHK
            if (u->owner_slot != s.local_slot) continue;  // ownership flipped mid-OHK
            u->attack_power = saved;
            flipped++;
        }
        s.ohk_saved_attack_powers.clear();
        s.ohk_enabled = false;
    }
    return flipped;
}

inline bool ReplayObsIsOHK(const ReplayState& s) {
    return s.ohk_enabled;
}

inline float ReplayObsGetAttackPower(const ReplayState& s, uint64_t obj_addr) {
    const auto* u = ReplayFindUnit(s, obj_addr);
    return u ? u->attack_power : -1.0f;
}

// Task 172 (2026-04-24) — orbital-phase per-slot toggle. 0 = tactical
// land, 1 = orbital space. Negative slot rejected (no global orbital
// phase — each slot has its own). Phase 2 hooks into the engine's
// orbital-phase flag once the IDA pass lands its offset.
inline int ReplayMutSetOrbitalPhase(ReplayState& s, int32_t slot, uint8_t phase) {
    if (slot < 0) return 0;
    if (phase > 1) return 0;       // only 0/1 valid in Phase 1
    s.per_slot_orbital_phase[slot] = phase;
    return 1;
}
inline uint8_t ReplayObsGetOrbitalPhase(const ReplayState& s, int32_t slot) {
    if (slot < 0) return 0;
    auto it = s.per_slot_orbital_phase.find(slot);
    return it == s.per_slot_orbital_phase.end() ? 0 : it->second;
}

// Task 173 (2026-04-24) — music subsystem mirror. Volume 0..1; pause
// is a bool; current track is a free-form string the Phase 2 hook
// will populate from the engine's now-playing global. Negative or
// >1 volume is clamped to the valid range so Phase 1 can stress
// edge values without staging invalid bridge calls.
inline int ReplayMutSetMusicVolume(ReplayState& s, float volume) {
    if (volume < 0.0f) volume = 0.0f;
    if (volume > 1.0f) volume = 1.0f;
    s.music_volume = volume;
    return 1;
}
inline float ReplayObsGetMusicVolume(const ReplayState& s) { return s.music_volume; }
inline int ReplayMutSetMusicPaused(ReplayState& s, bool paused) {
    s.music_paused = paused;
    return 1;
}
inline bool ReplayObsIsMusicPaused(const ReplayState& s) { return s.music_paused; }
inline int ReplayMutSetCurrentTrack(ReplayState& s, const std::string& track_id) {
    s.music_current_track = track_id;
    return 1;
}
inline const std::string& ReplayObsGetCurrentTrack(const ReplayState& s) {
    return s.music_current_track;
}

// Task 174 (2026-04-24) — veterancy rank per unit (0..3 inclusive).
// Engine ranks: 0=Recruit, 1=Veteran, 2=Elite, 3=Legendary. Phase 2
// will write the engine's rank field for the unit; Phase 1 carries
// the value in a per-obj_addr map. Out-of-range values rejected.
inline int ReplayMutSetVeterancy(ReplayState& s, uint64_t obj_addr, uint8_t rank) {
    if (obj_addr == 0) return 0;
    if (rank > 3) return 0;
    s.per_unit_veterancy[obj_addr] = rank;
    return 1;
}
inline int32_t ReplayObsGetVeterancy(const ReplayState& s, uint64_t obj_addr) {
    auto it = s.per_unit_veterancy.find(obj_addr);
    return it == s.per_unit_veterancy.end() ? -1 : static_cast<int32_t>(it->second);
}

// Task 175 (2026-04-24) — map-hint sprite registry. AddHint appends
// a new hint; RemoveHint searches by hint_id (case-sensitive).
// Returns the count of hints that landed in the resulting state.
inline int ReplayMutAddMapHint(
    ReplayState& s,
    const std::string& hint_id,
    const std::string& caption,
    float x, float y, float z,
    int32_t sprite_index
) {
    if (hint_id.empty()) return 0;
    // Reject duplicates — adding two hints with the same id would
    // confuse the Phase 2 hook's id->engine-handle mapping.
    for (const auto& existing : s.map_hints) {
        if (existing.hint_id == hint_id) return 0;
    }
    ReplayState::MapHintRecord rec;
    rec.hint_id = hint_id;
    rec.caption = caption;
    rec.world_x = x; rec.world_y = y; rec.world_z = z;
    rec.sprite_index = sprite_index;
    s.map_hints.push_back(std::move(rec));
    return static_cast<int>(s.map_hints.size());
}
inline int ReplayMutRemoveMapHint(ReplayState& s, const std::string& hint_id) {
    auto before = s.map_hints.size();
    s.map_hints.erase(
        std::remove_if(s.map_hints.begin(), s.map_hints.end(),
            [&hint_id](const ReplayState::MapHintRecord& r) { return r.hint_id == hint_id; }),
        s.map_hints.end());
    return static_cast<int>(before - s.map_hints.size());
}
inline int32_t ReplayObsMapHintCount(const ReplayState& s) {
    return static_cast<int32_t>(s.map_hints.size());
}
inline int ReplayMutClearMapHints(ReplayState& s) {
    auto n = static_cast<int>(s.map_hints.size());
    s.map_hints.clear();
    return n;
}

// Task 98 (2026-04-23) — HealAllLocal port from the CE trainer.
// Iterate `s.units`, match on `owner_slot == s.local_slot`, restore hull
// to max_hull for each match. Returns the count of units healed. Enemy
// units are never touched (READ-ONLY discipline). In live code the
// bridge writes 99999 and lets the engine clamp to max_hull, but the
// replay model has explicit max_hull so the mirror is honest.
inline int ReplayMutHealAllLocal(ReplayState& s) {
    if (s.local_slot < 0) return 0;
    int healed = 0;
    for (auto& entry : s.units) {
        ReplayUnitDetail& u = entry.second;
        if (u.owner_slot != s.local_slot) continue;
        if (u.max_hull <= 0.0f) continue;  // guard against mocked-without-max units
        if (u.hull >= u.max_hull) continue; // already full — do not count as healed
        u.hull = u.max_hull;
        healed++;
    }
    return healed;
}

// Task 158 (2026-04-23). Pure-state observer returning only the rows whose
// owner_slot matches the query. Same CSV row format as
// ReplayObsListTacticalUnits, with "count=N" reflecting the filtered count
// rather than the total tactical unit count. Owner slot -1 never matches --
// the filter rejects enqueued-as-unowned units, matching the live
// IsValidObjAddr + OwnerPlayerID read path.
inline std::string ReplayObsEnumerateUnitsForSlot(const ReplayState& s, int32_t faction_slot) {
    if (s.units.empty() || faction_slot < 0) return "count=0";
    std::string out;
    int emit_count = 0;
    for (const auto& entry : s.units) {
        if (entry.second.owner_slot == faction_slot) emit_count++;
    }
    char header[32];
    int hlen = std::snprintf(header, sizeof(header), "count=%d", emit_count);
    if (hlen > 0) out.append(header, static_cast<size_t>(hlen));
    if (emit_count == 0) return out;
    for (const auto& entry : s.units) {
        const ReplayUnitDetail& u = entry.second;
        if (u.owner_slot != faction_slot) continue;
        bool is_local = (s.local_slot >= 0 && u.owner_slot == s.local_slot);
        bool is_sel = false;
        for (uint64_t a : s.selected_units) {
            if (a == u.obj_addr) { is_sel = true; break; }
        }
        char row[192];
        int n = std::snprintf(
            row, sizeof(row),
            "|%llu;%d;%.3f;%u;%u;%d;%d",
            static_cast<unsigned long long>(u.obj_addr),
            static_cast<int>(u.owner_slot),
            static_cast<double>(u.hull),
            static_cast<unsigned>(u.invuln_flag),
            static_cast<unsigned>((u.prevent_death & 0x80) ? 1 : 0),
            is_local ? 1 : 0,
            is_sel ? 1 : 0);
        if (n > 0) out.append(row, static_cast<size_t>(n));
    }
    return out;
}

// Pure-state observer for SWFOC_ListTacticalUnits (Task 104, 2026-04-23).
// Mirrors the live Lua_ListTacticalUnits row format so the V2 Tactical Units
// DataGrid (Task 107) can consume the same CSV shape whether it is reading
// live data or a replayed snapshot.
//
// Row contract (semicolon-separated fields, rows separated by '|'):
//   obj_addr_decimal;owner_slot;hull;invuln_flag;prevent_death_bit;is_local;is_selected
//
// Where:
//   obj_addr_decimal  — uint64 as decimal
//   owner_slot        — int32 (may be -1 if ReplayUnitDetail is unowned)
//   hull              — float, 3 decimals (no trailing zeros beyond that)
//   invuln_flag       — 0/1 (display byte, no gameplay effect — see Task 99)
//   prevent_death_bit — 0/1 (bit 0x80 of +0x3A1)
//   is_local          — 1 if owner_slot == s.local_slot, 0 otherwise
//   is_selected       — 1 if obj_addr ∈ s.selected_units, 0 otherwise
//
// Returns "count=0" when the units map is empty. The caller should treat this
// as "no tactical units / not in tactical mode" rather than an error.
inline std::string ReplayObsListTacticalUnits(const ReplayState& s) {
    if (s.units.empty()) return "count=0";
    std::string out;
    char header[32];
    int hlen = std::snprintf(header, sizeof(header), "count=%zu", s.units.size());
    if (hlen > 0) out.append(header, static_cast<size_t>(hlen));
    for (const auto& entry : s.units) {
        const ReplayUnitDetail& u = entry.second;
        bool is_local = (s.local_slot >= 0 && u.owner_slot == s.local_slot);
        bool is_sel = false;
        for (uint64_t a : s.selected_units) {
            if (a == u.obj_addr) { is_sel = true; break; }
        }
        char row[192];
        int n = std::snprintf(
            row, sizeof(row),
            "|%llu;%d;%.3f;%u;%u;%d;%d",
            static_cast<unsigned long long>(u.obj_addr),
            static_cast<int>(u.owner_slot),
            static_cast<double>(u.hull),
            static_cast<unsigned>(u.invuln_flag),
            static_cast<unsigned>((u.prevent_death & 0x80) ? 1 : 0),
            is_local ? 1 : 0,
            is_sel ? 1 : 0);
        if (n > 0) out.append(row, static_cast<size_t>(n));
    }
    return out;
}
