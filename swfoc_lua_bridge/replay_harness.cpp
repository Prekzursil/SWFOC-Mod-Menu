// replay_harness.cpp -- SWFOC Phase 6 offline replay harness.
//
// Standalone executable that loads a `.swfocsnap` file captured by the live
// bridge (SWFOC_DumpState in lua_bridge.cpp), rebuilds the relevant slices of
// game state in memory, embeds a Lua 5.0.2-shaped stub VM (reusing
// fake_lua.cpp + fake_memory.cpp from the test harness), re-registers the
// SWFOC_* helpers against the replay state, and hosts a named-pipe listener
// on `\\.\pipe\swfoc_bridge_replay` that speaks the SAME protocol as the
// live bridge.
//
// Build:
//   x86_64-w64-mingw32-g++ -O2 -std=c++17 -static -o swfoc_replay.exe
//       replay_harness.cpp fake_lua.cpp fake_memory.cpp -lws2_32
//
// Usage:
//   swfoc_replay.exe <path-to-snapshot.swfocsnap>
//
// The live bridge continues to own `\\.\pipe\swfoc_bridge`; this harness
// uses a distinct pipe name so it can run alongside the live game without
// competing for the same pipe.

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cstdint>
#include <cstdarg>
#include <string>
#include <vector>
#include <map>
#include <algorithm>
#include <functional>
#include <utility>

#include "lua_types.h"
#include "fake_lua.h"
#include "replay_state.h"

// ======================================================================
// Pipe protocol constants
// ======================================================================

#define REPLAY_PIPE_NAME "\\\\.\\pipe\\swfoc_bridge_replay"
#define PIPE_CMD_MAX     4096

// ======================================================================
// ReplayState -- the in-memory projection of a decoded .swfocsnap file
// ======================================================================

// ReplayPlayer / ReplayGlobal / ReplayState etc. are defined in
// replay_state.h so the bridge test harness can include them too.
// `g_replay` is the single in-memory snapshot the pipe listener mutates.

static ReplayState g_replay;

// File-local convenience aliases so existing call sites stay readable.
// (The test harness uses Replay* prefixed names from replay_state.h.)
static inline std::string ToUpperAscii(const std::string& s) {
    return ReplayUpper(s);
}

static inline std::pair<std::string, std::string>
MakeDiplomacyKey(const std::string& a, const std::string& b) {
    return ReplayDiplomacyKey(a, b);
}

// ======================================================================
// Logging
// ======================================================================

static void LogErr(const char* fmt, ...) {
    va_list ap;
    va_start(ap, fmt);
    vfprintf(stderr, fmt, ap);
    va_end(ap);
    fflush(stderr);
}

static void LogOut(const char* fmt, ...) {
    va_list ap;
    va_start(ap, fmt);
    vfprintf(stdout, fmt, ap);
    va_end(ap);
    fflush(stdout);
}

// ======================================================================
// CRC32 (matches the live bridge writer: polynomial 0xEDB88320, reflected,
// init/xor 0xFFFFFFFF -- the zlib/PKZIP variant).
// ======================================================================

static uint32_t g_crc32Table[256];
static bool     g_crc32TableReady = false;

static void Crc32_BuildTable() {
    for (uint32_t i = 0; i < 256; i++) {
        uint32_t c = i;
        for (int k = 0; k < 8; k++)
            c = (c & 1) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
        g_crc32Table[i] = c;
    }
    g_crc32TableReady = true;
}

static uint32_t Crc32_Compute(const void* data, size_t len) {
    if (!g_crc32TableReady) Crc32_BuildTable();
    uint32_t crc = 0xFFFFFFFFu;
    const uint8_t* p = static_cast<const uint8_t*>(data);
    for (size_t i = 0; i < len; i++)
        crc = g_crc32Table[(crc ^ p[i]) & 0xFFu] ^ (crc >> 8);
    return crc ^ 0xFFFFFFFFu;
}

// ======================================================================
// Snapshot reader (matches SNAPSHOT_FORMAT.md byte layout exactly)
// ======================================================================

// Small cursor over a raw byte buffer, little-endian reads.
class SnapCursor {
public:
    SnapCursor(const uint8_t* data, size_t size) : p_(data), size_(size) {}

    bool ok() const { return ok_; }
    size_t pos() const { return off_; }
    size_t remaining() const { return off_ < size_ ? size_ - off_ : 0; }

    bool read_bytes(void* out, size_t n) {
        if (!ok_) return false;
        if (off_ + n > size_) { ok_ = false; return false; }
        memcpy(out, p_ + off_, n);
        off_ += n;
        return true;
    }

    bool read_u8 (uint8_t*  v) { return read_bytes(v, 1); }
    bool read_u16(uint16_t* v) { return read_bytes(v, 2); }
    bool read_u32(uint32_t* v) { return read_bytes(v, 4); }
    bool read_u64(uint64_t* v) { return read_bytes(v, 8); }
    bool read_i32(int32_t*  v) { return read_bytes(v, 4); }
    bool read_f64(double*   v) { return read_bytes(v, 8); }

    bool skip(size_t n) {
        if (!ok_) return false;
        if (off_ + n > size_) { ok_ = false; return false; }
        off_ += n;
        return true;
    }

    // Read a fixed-width, null-padded ASCII field and trim at the first null.
    bool read_fixed_str(std::string* out, size_t width) {
        std::vector<char> tmp(width + 1, 0);
        if (!read_bytes(tmp.data(), width)) return false;
        tmp[width] = '\0';
        *out = std::string(tmp.data());
        return true;
    }

private:
    const uint8_t* p_;
    size_t         size_ = 0;
    size_t         off_  = 0;
    bool           ok_   = true;
};

struct SnapshotLoadResult {
    bool        ok = false;
    std::string error;
    size_t      total_bytes = 0;
};

static SnapshotLoadResult LoadSnapshot(const char* path, ReplayState& out) {
    SnapshotLoadResult r;

    FILE* f = fopen(path, "rb");
    if (!f) {
        r.error = std::string("could not open snapshot file: ") + path;
        return r;
    }

    fseek(f, 0, SEEK_END);
    long sz = ftell(f);
    fseek(f, 0, SEEK_SET);
    if (sz < 68 + 12) {                 // header + minimal end marker
        r.error = "snapshot file is smaller than the minimum valid size (80 bytes)";
        fclose(f);
        return r;
    }

    std::vector<uint8_t> bytes(static_cast<size_t>(sz));
    size_t got = fread(bytes.data(), 1, bytes.size(), f);
    fclose(f);
    if (got != bytes.size()) {
        r.error = "short read of snapshot file";
        return r;
    }
    r.total_bytes = bytes.size();

    SnapCursor c(bytes.data(), bytes.size());

    // ---- Header ----
    uint8_t magic[16];
    if (!c.read_bytes(magic, 16)) { r.error = "header truncated"; return r; }
    const uint8_t kMagicV1[16] = {
        'S','W','F','O','C','S','N','A','P','v','1', 0, 0, 0, 0, 0
    };
    const uint8_t kMagicV2[16] = {
        'S','W','F','O','C','S','N','A','P','v','2', 0, 0, 0, 0, 0
    };
    bool isV1 = memcmp(magic, kMagicV1, 16) == 0;
    bool isV2 = memcmp(magic, kMagicV2, 16) == 0;
    if (!isV1 && !isV2) {
        r.error = "magic mismatch (expected 'SWFOCSNAPv1' or 'SWFOCSNAPv2')";
        return r;
    }

    if (!c.read_u32(&out.format_version)) { r.error = "format_version truncated"; return r; }
    // v1 = legacy (no explicit local_slot in section 1; derived from first player)
    // v2 = current (explicit local_slot in section 1, added 2026-04-08)
    if (out.format_version != 1 && out.format_version != 2) {
        char buf[128];
        snprintf(buf, sizeof(buf),
                 "unsupported format_version=%u (expected 1 or 2)",
                 out.format_version);
        r.error = buf;
        return r;
    }
    // Cross-check: magic and format_version must agree.
    if ((isV1 && out.format_version != 1) || (isV2 && out.format_version != 2)) {
        r.error = "magic/format_version mismatch";
        return r;
    }

    if (!c.read_u64(&out.capture_timestamp_ms)) { r.error = "timestamp truncated"; return r; }
    if (!c.read_bytes(out.engine_build_hash, 32)) { r.error = "engine_build_hash truncated"; return r; }
    if (!c.read_u8(&out.game_mode)) { r.error = "game_mode truncated"; return r; }
    if (!c.skip(7)) { r.error = "reserved header padding truncated"; return r; }

    // Header should be exactly 68 bytes.
    if (c.pos() != 68) {
        r.error = "header did not end at offset 68";
        return r;
    }

    // ---- Sections ----
    while (c.remaining() >= 8) {
        size_t sectionStart = c.pos();
        uint32_t section_id = 0;
        uint32_t section_len = 0;
        if (!c.read_u32(&section_id))  { r.error = "section header id truncated"; return r; }
        if (!c.read_u32(&section_len)) { r.error = "section header length truncated"; return r; }

        if (section_id == 0xFFFFFFFFu) {
            // End marker: payload is the 4-byte CRC32 over everything before it.
            if (section_len != 4) {
                r.error = "end marker section_length != 4";
                return r;
            }
            if (c.remaining() < 4) { r.error = "CRC32 truncated"; return r; }
            uint32_t fileCrc = 0;
            if (!c.read_u32(&fileCrc)) { r.error = "CRC32 read failed"; return r; }
            // CRC covers [0 .. sectionStart + 8), i.e. end-marker header included.
            uint32_t expected = Crc32_Compute(bytes.data(), sectionStart + 8);
            if (expected != fileCrc) {
                char buf[128];
                snprintf(buf, sizeof(buf),
                         "CRC32 mismatch (file=0x%08X computed=0x%08X)",
                         fileCrc, expected);
                r.error = buf;
                return r;
            }
            // Success.
            r.ok = true;
            return r;
        }

        // Defensive: bound the section length
        if (c.remaining() < section_len) {
            r.error = "section payload runs past end of file";
            return r;
        }
        size_t bodyStart = c.pos();

        if (section_id == 1) {
            // player_array
            uint32_t player_count = 0;
            if (!c.read_u32(&player_count)) { r.error = "player_count truncated"; return r; }
            // Defensive clamp (capture already clamps to 8).
            if (player_count > 4096) {
                r.error = "player_count out of sane bound (>4096)";
                return r;
            }
            // v2 addition: explicit local_slot. Read only when format_version
            // says so. v1 snapshots derive the local slot from the first
            // player after the loop.
            uint32_t explicit_local_slot = 0xFFFFFFFFu;
            if (out.format_version >= 2) {
                if (!c.read_u32(&explicit_local_slot)) {
                    r.error = "local_slot truncated (v2)";
                    return r;
                }
            }
            out.players.clear();
            out.players.reserve(player_count);
            for (uint32_t i = 0; i < player_count; i++) {
                ReplayPlayer p;
                if (!c.read_u32(&p.slot))                     { r.error = "player slot truncated"; return r; }
                if (!c.read_fixed_str(&p.faction_name, 64))   { r.error = "player faction truncated"; return r; }
                if (!c.read_f64(&p.credits))                  { r.error = "player credits truncated"; return r; }
                if (!c.read_i32(&p.tech_level))               { r.error = "player tech_level truncated"; return r; }
                if (!c.read_fixed_str(&p.player_name, 64))    { r.error = "player name truncated"; return r; }
                out.players.push_back(std::move(p));
            }
            // Resolve local_slot: v2 uses the explicit field; v1 falls back
            // to the first player. UINT32_MAX means "no local player".
            if (out.format_version >= 2) {
                if (explicit_local_slot == 0xFFFFFFFFu) {
                    out.local_slot = -1;
                } else {
                    out.local_slot = static_cast<int>(explicit_local_slot);
                }
            } else {
                out.local_slot = out.players.empty()
                    ? -1
                    : static_cast<int>(out.players.front().slot);
            }
        } else if (section_id == 2) {
            uint32_t state_count = 0;
            if (!c.read_u32(&state_count)) { r.error = "state_count truncated"; return r; }
            if (state_count > 1024 * 64) {
                r.error = "state_count out of sane bound";
                return r;
            }
            out.lua_state_ptrs.clear();
            out.lua_state_ptrs.reserve(state_count);
            for (uint32_t i = 0; i < state_count; i++) {
                uint64_t ptr = 0;
                if (!c.read_u64(&ptr)) { r.error = "lua_state pointer truncated"; return r; }
                out.lua_state_ptrs.push_back(ptr);
            }
        } else if (section_id == 3) {
            uint32_t type_count = 0;
            if (!c.read_u32(&type_count)) { r.error = "object type_count truncated"; return r; }
            if (type_count > 1024 * 1024) { r.error = "object type_count out of sane bound"; return r; }
            out.objects.clear();
            for (uint32_t i = 0; i < type_count; i++) {
                std::string name;
                uint32_t count = 0;
                if (!c.read_fixed_str(&name, 64)) { r.error = "object type name truncated"; return r; }
                if (!c.read_u32(&count))          { r.error = "object instance_count truncated"; return r; }
                out.objects[name] = count;
            }
        } else if (section_id == 4) {
            uint32_t global_count = 0;
            if (!c.read_u32(&global_count)) { r.error = "global_count truncated"; return r; }
            if (global_count > 1024 * 1024) { r.error = "global_count out of sane bound"; return r; }
            out.globals.clear();
            for (uint32_t i = 0; i < global_count; i++) {
                std::string name;
                ReplayGlobal g{};
                uint8_t pad[7];
                if (!c.read_fixed_str(&name, 64)) { r.error = "global name truncated"; return r; }
                if (!c.read_u8(&g.lua_type))      { r.error = "global lua_type truncated"; return r; }
                if (!c.read_bytes(pad, 7))        { r.error = "global pad truncated"; return r; }
                if (!c.read_u64(&g.raw_value_or_ptr)) { r.error = "global raw_value truncated"; return r; }
                out.globals[name] = g;
            }
        } else if (section_id == 5) {
            uint32_t entry_count = 0;
            if (!c.read_u32(&entry_count)) { r.error = "metadata entry_count truncated"; return r; }
            if (entry_count > 65536) { r.error = "metadata entry_count out of sane bound"; return r; }
            out.metadata.clear();
            for (uint32_t i = 0; i < entry_count; i++) {
                uint16_t kl = 0, vl = 0;
                if (!c.read_u16(&kl)) { r.error = "metadata key_length truncated"; return r; }
                std::string k(kl, '\0');
                if (kl && !c.read_bytes(&k[0], kl)) { r.error = "metadata key truncated"; return r; }
                if (!c.read_u16(&vl)) { r.error = "metadata value_length truncated"; return r; }
                std::string v(vl, '\0');
                if (vl && !c.read_bytes(&v[0], vl)) { r.error = "metadata value truncated"; return r; }
                out.metadata[k] = v;
            }
        } else if (section_id == 6) {
            // section 6: planet_state (added v2 extension, 2026-04-08)
            // Layout:
            //   uint32 planet_count
            //   for i in 0..planet_count:
            //       char    name[64]
            //       float32 corruption
            //       int32   owner_slot   (-1 = no owner)
            uint32_t planet_count = 0;
            if (!c.read_u32(&planet_count)) { r.error = "planet_count truncated"; return r; }
            if (planet_count > 4096) { r.error = "planet_count out of sane bound"; return r; }
            out.planets.clear();
            for (uint32_t i = 0; i < planet_count; i++) {
                std::string name;
                if (!c.read_fixed_str(&name, 64)) { r.error = "planet name truncated"; return r; }
                uint32_t corr_bits = 0;
                if (!c.read_u32(&corr_bits)) { r.error = "planet corruption truncated"; return r; }
                int32_t owner = 0;
                if (!c.read_i32(&owner))     { r.error = "planet owner truncated"; return r; }
                ReplayPlanetInfo info;
                info.name = name;
                memcpy(&info.corruption, &corr_bits, 4);
                info.owner_slot = owner;
                out.planets[ToUpperAscii(name)] = info;
            }
        } else if (section_id == 7) {
            // section 7: diplomacy (added v2 extension, 2026-04-08)
            // Layout:
            //   uint32 pair_count
            //   for i in 0..pair_count:
            //       char  faction_a[32]
            //       char  faction_b[32]
            //       char  state[16]      // "allied" / "hostile" / "neutral"
            uint32_t pair_count = 0;
            if (!c.read_u32(&pair_count)) { r.error = "diplomacy pair_count truncated"; return r; }
            if (pair_count > 4096) { r.error = "diplomacy pair_count out of sane bound"; return r; }
            out.diplomacy.clear();
            for (uint32_t i = 0; i < pair_count; i++) {
                std::string fa, fb, st;
                if (!c.read_fixed_str(&fa, 32)) { r.error = "diplomacy faction_a truncated"; return r; }
                if (!c.read_fixed_str(&fb, 32)) { r.error = "diplomacy faction_b truncated"; return r; }
                if (!c.read_fixed_str(&st, 16)) { r.error = "diplomacy state truncated"; return r; }
                out.diplomacy[MakeDiplomacyKey(fa, fb)] = st;
            }
        } else if (section_id == 8) {
            // section 8: cooldowns (added v2 extension, 2026-04-08)
            // Layout:
            //   uint32 type_count
            //   for i in 0..type_count:
            //       char     type_name[64]
            //       uint32   ability_count
            //       float32  cooldown[ability_count]
            uint32_t type_count = 0;
            if (!c.read_u32(&type_count)) { r.error = "cooldown type_count truncated"; return r; }
            if (type_count > 4096) { r.error = "cooldown type_count out of sane bound"; return r; }
            out.cooldowns.clear();
            for (uint32_t i = 0; i < type_count; i++) {
                std::string name;
                uint32_t ability_count = 0;
                if (!c.read_fixed_str(&name, 64)) { r.error = "cooldown type name truncated"; return r; }
                if (!c.read_u32(&ability_count)) { r.error = "cooldown ability_count truncated"; return r; }
                if (ability_count > 256) { r.error = "cooldown ability_count out of sane bound"; return r; }
                std::vector<float> values;
                values.reserve(ability_count);
                for (uint32_t j = 0; j < ability_count; j++) {
                    uint32_t bits = 0;
                    if (!c.read_u32(&bits)) { r.error = "cooldown value truncated"; return r; }
                    float v = 0.0f;
                    memcpy(&v, &bits, 4);
                    values.push_back(v);
                }
                out.cooldowns[name] = std::move(values);
            }
        } else if (section_id == 9) {
            // section 9: task_forces (added v2 extension, 2026-04-08)
            // Layout:
            //   uint32 force_count
            //   for i in 0..force_count:
            //       int32 owner_slot
            //       char  name[64]
            uint32_t force_count = 0;
            if (!c.read_u32(&force_count)) { r.error = "task_force count truncated"; return r; }
            if (force_count > 4096) { r.error = "task_force count out of sane bound"; return r; }
            out.task_forces.clear();
            for (uint32_t i = 0; i < force_count; i++) {
                int32_t owner = 0;
                std::string name;
                if (!c.read_i32(&owner)) { r.error = "task_force owner truncated"; return r; }
                if (!c.read_fixed_str(&name, 64)) { r.error = "task_force name truncated"; return r; }
                ReplayTaskForceRecord rec;
                rec.owner_slot = owner;
                rec.name = name;
                out.task_forces.push_back(std::move(rec));
            }
        } else if (section_id == 10) {
            // section 10: object_owners (added v2 extension, 2026-04-08)
            // Layout:
            //   uint32 type_count
            //   for i in 0..type_count:
            //       char    type_name[64]
            //       uint32  instance_count
            //       int32   owner_slot[instance_count]
            uint32_t type_count = 0;
            if (!c.read_u32(&type_count)) { r.error = "object_owners type_count truncated"; return r; }
            if (type_count > 4096) { r.error = "object_owners type_count out of sane bound"; return r; }
            out.object_owners.clear();
            for (uint32_t i = 0; i < type_count; i++) {
                std::string name;
                uint32_t instance_count = 0;
                if (!c.read_fixed_str(&name, 64)) { r.error = "object_owners type name truncated"; return r; }
                if (!c.read_u32(&instance_count)) { r.error = "object_owners instance_count truncated"; return r; }
                if (instance_count > 65536) { r.error = "object_owners instance_count out of sane bound"; return r; }
                std::vector<int32_t> owners;
                owners.reserve(instance_count);
                for (uint32_t j = 0; j < instance_count; j++) {
                    int32_t s = 0;
                    if (!c.read_i32(&s)) { r.error = "object_owners owner truncated"; return r; }
                    owners.push_back(s);
                }
                out.object_owners[ToUpperAscii(name)] = std::move(owners);
            }
        } else if (section_id == 11) {
            // section 11: selected_units (added 2026-04-23 for Task 101)
            // Layout:
            //   uint32 count
            //   uint64 obj_addr[count]
            uint32_t count = 0;
            if (!c.read_u32(&count)) { r.error = "selected_units count truncated"; return r; }
            if (count > 4096) { r.error = "selected_units count out of sane bound"; return r; }
            out.selected_units.clear();
            out.selected_units.reserve(count);
            for (uint32_t i = 0; i < count; i++) {
                uint64_t obj = 0;
                if (!c.read_u64(&obj)) { r.error = "selected_units obj_addr truncated"; return r; }
                out.selected_units.push_back(obj);
            }
        } else if (section_id == 12) {
            // section 12: unit_detail (added 2026-04-23 for Task 101)
            // Layout per unit: 102 bytes fixed + hardpoint indices.
            //   uint64  obj_addr
            //   char    type_name[64]
            //   int32   owner_slot
            //   float32 hull
            //   float32 max_hull
            //   uint8   invuln_flag
            //   uint8   prevent_death
            //   uint8   reserved[6]
            //   uint32  hardpoint_count
            //   uint32  hardpoint_indices[hardpoint_count]
            // Section 13 (behavior_attach) is responsible for the per-HP
            // behavior name lists.
            uint32_t unit_count = 0;
            if (!c.read_u32(&unit_count)) { r.error = "unit_detail count truncated"; return r; }
            if (unit_count > 4096) { r.error = "unit_detail count out of sane bound"; return r; }
            for (uint32_t i = 0; i < unit_count; i++) {
                uint64_t obj_addr = 0;
                std::string type_name;
                int32_t owner_slot = 0;
                uint32_t hull_bits = 0, max_hull_bits = 0;
                uint8_t invuln_flag = 0, prevent_death = 0;
                uint8_t reserved[6];
                if (!c.read_u64(&obj_addr))               { r.error = "unit_detail obj_addr truncated"; return r; }
                if (!c.read_fixed_str(&type_name, 64))    { r.error = "unit_detail type_name truncated"; return r; }
                if (!c.read_i32(&owner_slot))             { r.error = "unit_detail owner_slot truncated"; return r; }
                if (!c.read_u32(&hull_bits))              { r.error = "unit_detail hull truncated"; return r; }
                if (!c.read_u32(&max_hull_bits))          { r.error = "unit_detail max_hull truncated"; return r; }
                if (!c.read_u8(&invuln_flag))             { r.error = "unit_detail invuln_flag truncated"; return r; }
                if (!c.read_u8(&prevent_death))           { r.error = "unit_detail prevent_death truncated"; return r; }
                if (!c.read_bytes(reserved, 6))           { r.error = "unit_detail reserved truncated"; return r; }
                uint32_t hp_count = 0;
                if (!c.read_u32(&hp_count))               { r.error = "unit_detail hp_count truncated"; return r; }
                if (hp_count > 1024) { r.error = "unit_detail hp_count out of sane bound"; return r; }
                float hull = 0.0f, max_hull = 0.0f;
                memcpy(&hull, &hull_bits, 4);
                memcpy(&max_hull, &max_hull_bits, 4);
                auto& u = ReplayMutMockUnit(out, obj_addr, type_name, owner_slot, hull, max_hull, hp_count);
                u.invuln_flag = invuln_flag;
                u.prevent_death = prevent_death;
                for (uint32_t j = 0; j < hp_count; j++) {
                    uint32_t hp_index = 0;
                    if (!c.read_u32(&hp_index)) { r.error = "unit_detail hp_index truncated"; return r; }
                    if (j < u.hardpoints.size()) u.hardpoints[j].index = hp_index;
                }
            }
        } else if (section_id == 13) {
            // section 13: behavior_attach (added 2026-04-23 for Task 101)
            // Flat list of (obj_addr, hp_index, behavior_name) triples.
            //   uint32 entry_count
            //   for i in 0..entry_count:
            //       uint64 obj_addr
            //       uint32 hp_index
            //       char   behavior_name[32]
            // Entries referring to units not present in section 12 are
            // ignored (forward-compat: a capture that emits behaviors for
            // units outside the selection should not fail loading).
            uint32_t entry_count = 0;
            if (!c.read_u32(&entry_count)) { r.error = "behavior_attach count truncated"; return r; }
            if (entry_count > 65536) { r.error = "behavior_attach count out of sane bound"; return r; }
            for (uint32_t i = 0; i < entry_count; i++) {
                uint64_t obj_addr = 0;
                uint32_t hp_index = 0;
                std::string behavior;
                if (!c.read_u64(&obj_addr))             { r.error = "behavior_attach obj_addr truncated"; return r; }
                if (!c.read_u32(&hp_index))             { r.error = "behavior_attach hp_index truncated"; return r; }
                if (!c.read_fixed_str(&behavior, 32))   { r.error = "behavior_attach name truncated"; return r; }
                // Silently skip entries that do not match a loaded unit.
                ReplayMutAttachBehavior(out, obj_addr, static_cast<int>(hp_index), behavior);
            }
        } else {
            // Unknown / forward-compatible section: skip payload.
            if (!c.skip(section_len)) { r.error = "unknown section skip failed"; return r; }
        }

        // Make sure we consumed exactly section_len bytes.
        size_t bodyEnd = c.pos();
        if (bodyEnd - bodyStart != section_len) {
            // Advance to end of the declared payload if we under-consumed it.
            size_t delta = (bodyStart + section_len) - bodyEnd;
            if (!c.skip(delta)) { r.error = "section payload alignment failed"; return r; }
        }
    }

    r.error = "reached end of file without seeing end marker";
    return r;
}

// ======================================================================
// Embedded fake Lua VM + function pointer wiring
// ======================================================================
//
// We reuse fake_lua.cpp / fake_memory.cpp from the existing test harness.
// Those implementations take a `FakeLuaState*` as their first argument,
// but the bridge-style lua_CFunction signature expects `lua_State*`.
// `lua_State` is an opaque forward declaration -- at the ABI level
// FakeLuaState* and lua_State* are just pointers, so a reinterpret_cast
// at the wire-up site is safe (and mirrors what test_harness.cpp does).

static pfn_lua_pushstring   fn_pushstring   = nullptr;
static pfn_lua_pushcclosure fn_pushcclosure = nullptr;
static pfn_lua_settop       fn_settop       = nullptr;
static pfn_lua_tonumber     fn_tonumber     = nullptr;
static pfn_lua_tostring     fn_tostring     = nullptr;
static pfn_lua_type         fn_type         = nullptr;
static pfn_lua_newtable     fn_newtable     = nullptr;
static pfn_lua_settable     fn_settable     = nullptr;
static pfn_lua_gettable     fn_gettable     = nullptr;
static pfn_lua_rawseti      fn_rawseti      = nullptr;
static pfn_lua_pushnumber   fn_pushnumber   = nullptr;
static pfn_lua_pushboolean  fn_pushboolean  = nullptr;
static pfn_lua_pushnil      fn_pushnil      = nullptr;
static pfn_lua_gettop       fn_gettop       = nullptr;
static pfn_lua_pcall        fn_pcall        = nullptr;
static pfn_lua_load         fn_load         = nullptr;

static void WireFakes() {
    fn_pushstring   = reinterpret_cast<pfn_lua_pushstring>(&fake_pushstring);
    fn_pushcclosure = reinterpret_cast<pfn_lua_pushcclosure>(&fake_pushcclosure);
    fn_settop       = reinterpret_cast<pfn_lua_settop>(&fake_settop);
    fn_tonumber     = reinterpret_cast<pfn_lua_tonumber>(&fake_tonumber);
    fn_tostring     = reinterpret_cast<pfn_lua_tostring>(&fake_tostring);
    fn_type         = reinterpret_cast<pfn_lua_type>(&fake_type);
    fn_newtable     = reinterpret_cast<pfn_lua_newtable>(&fake_newtable);
    fn_settable     = reinterpret_cast<pfn_lua_settable>(&fake_settable);
    fn_gettable     = reinterpret_cast<pfn_lua_gettable>(&fake_gettable);
    fn_rawseti      = reinterpret_cast<pfn_lua_rawseti>(&fake_rawseti);
    fn_pushnumber   = reinterpret_cast<pfn_lua_pushnumber>(&fake_pushnumber);
    fn_pushboolean  = reinterpret_cast<pfn_lua_pushboolean>(&fake_pushboolean);
    fn_pushnil      = reinterpret_cast<pfn_lua_pushnil>(&fake_pushnil);
    fn_gettop       = reinterpret_cast<pfn_lua_gettop>(&fake_gettop);
    fn_pcall        = reinterpret_cast<pfn_lua_pcall>(&fake_pcall);
    fn_load         = reinterpret_cast<pfn_lua_load>(&fake_load);
}

// Cast helper matching the test harness pattern.
#define LS(fakePtr) reinterpret_cast<lua_State*>(fakePtr)
#define FS(luaPtr)  reinterpret_cast<FakeLuaState*>(luaPtr)

// ======================================================================
// SWFOC_* helpers, rewritten to read from ReplayState instead of engine RAM
// ======================================================================

static const ReplayPlayer* GetLocalReplayPlayer() {
    if (g_replay.players.empty()) return nullptr;
    if (g_replay.local_slot < 0) return &g_replay.players.front();
    for (const auto& p : g_replay.players) {
        if (static_cast<int>(p.slot) == g_replay.local_slot) return &p;
    }
    return &g_replay.players.front();
}

static int Lua_GetVersion(lua_State* L) {
    fn_pushstring(L, "SWFOC Lua Bridge v1.0 (replay)");
    return 1;
}

static int Lua_GetLocalPlayer(lua_State* L) {
    const ReplayPlayer* p = GetLocalReplayPlayer();
    if (!p) {
        fn_pushnumber(L, -1.0);
        fn_pushstring(L, "none");
        return 2;
    }
    fn_pushnumber(L, static_cast<double>(p->slot));
    fn_pushstring(L, p->faction_name.empty() ? "?" : p->faction_name.c_str());
    return 2;
}

static int Lua_GetCredits(lua_State* L) {
    const ReplayPlayer* p = GetLocalReplayPlayer();
    if (!p) { fn_pushnumber(L, 0); return 1; }
    fn_pushnumber(L, p->credits);
    return 1;
}

static int Lua_GetLocalFaction(lua_State* L) {
    const ReplayPlayer* p = GetLocalReplayPlayer();
    fn_pushstring(L, (p && !p->faction_name.empty()) ? p->faction_name.c_str() : "?");
    return 1;
}

// SetCredits / SetTechLevel are accepted but only mutate the in-memory
// replay state -- they never touch any real game process.
static int Lua_SetCredits(lua_State* L) {
    if (g_replay.players.empty()) { fn_pushnumber(L, 0); return 1; }
    double amount = fn_tonumber(L, 1);
    const ReplayPlayer* p = GetLocalReplayPlayer();
    if (!p) { fn_pushnumber(L, 0); return 1; }
    for (auto& mp : g_replay.players) {
        if (mp.slot == p->slot) { mp.credits = amount; break; }
    }
    fn_pushnumber(L, 1);
    return 1;
}

static int Lua_SetTechLevel(lua_State* L) {
    if (g_replay.players.empty()) { fn_pushnumber(L, 0); return 1; }
    int level = static_cast<int>(fn_tonumber(L, 1));
    const ReplayPlayer* p = GetLocalReplayPlayer();
    if (!p) { fn_pushnumber(L, 0); return 1; }
    for (auto& mp : g_replay.players) {
        if (mp.slot == p->slot) { mp.tech_level = level; break; }
    }
    fn_pushnumber(L, 1);
    return 1;
}

static int Lua_UncapCredits(lua_State* L) {
    // No-op in replay -- there is no max-credits field in ReplayState.
    fn_pushnumber(L, 1);
    return 1;
}

static int Lua_HeroInstantRespawn(lua_State* L) {
    // No-op in replay; the replay snapshot does not capture this value.
    fn_pushnumber(L, 1);
    return 1;
}

static int Lua_ListFactions(lua_State* L) {
    fn_newtable(L);
    int idx = 1;
    for (const auto& p : g_replay.players) {
        fn_newtable(L);

        fn_pushstring(L, "slot");
        fn_pushnumber(L, static_cast<double>(p.slot));
        fn_settable(L, -3);

        fn_pushstring(L, "name");
        fn_pushstring(L, p.faction_name.empty() ? "?" : p.faction_name.c_str());
        fn_settable(L, -3);

        fn_pushstring(L, "credits");
        fn_pushnumber(L, p.credits);
        fn_settable(L, -3);

        bool isLocal = (g_replay.local_slot >= 0 &&
                        static_cast<int>(p.slot) == g_replay.local_slot);
        fn_pushstring(L, "is_local");
        fn_pushnumber(L, isLocal ? 1.0 : 0.0);
        fn_settable(L, -3);

        fn_rawseti(L, -2, idx++);
    }
    return 1;
}

static int Lua_Log(lua_State* L) {
    const char* msg = fn_tostring(L, 1);
    if (msg) LogErr("[Replay][Lua] %s\n", msg);
    return 0;
}

static int Lua_StateInfo(lua_State* L) {
    char buf[256];
    snprintf(buf, sizeof(buf),
             "Replay states (from snapshot): %zu lua_state pointers captured",
             g_replay.lua_state_ptrs.size());
    fn_pushstring(L, buf);
    return 1;
}

static int Lua_EventControl(lua_State* L) {
    // No event ring in the replay harness.
    fn_pushnumber(L, 0);
    return 1;
}

// SWFOC_ReplayObjectCount(type) -> number of instances from the snapshot.
// Replay-only helper: lets editor tests verify the replay is mocking counts
// correctly without reaching for real game globals.
static int Lua_ReplayObjectCount(lua_State* L) {
    const char* name = fn_tostring(L, 1);
    if (!name) { fn_pushnumber(L, 0); return 1; }
    auto it = g_replay.objects.find(name);
    fn_pushnumber(L, it != g_replay.objects.end() ? static_cast<double>(it->second) : 0.0);
    return 1;
}

// SWFOC_ReplayMetadata(key) -> value string or "" if unknown.
static int Lua_ReplayMetadata(lua_State* L) {
    const char* key = fn_tostring(L, 1);
    if (!key) { fn_pushstring(L, ""); return 1; }
    auto it = g_replay.metadata.find(key);
    fn_pushstring(L, it != g_replay.metadata.end() ? it->second.c_str() : "");
    return 1;
}

// SWFOC_ReplayPlayerCount() -> number of players in the snapshot.
static int Lua_ReplayPlayerCount(lua_State* L) {
    fn_pushnumber(L, static_cast<double>(g_replay.players.size()));
    return 1;
}

// ----------------------------------------------------------------------
// New v5 service observer + mutation helpers (added 2026-04-08).
//
// These mirror entries in knowledge-base/replay_stub_gaps.md and let
// editor tests verify post-call state from the in-memory ReplayState
// without spinning up a real SWFOC process.
//
// All observers are read-only and return a sentinel (0 / -1 / "") for
// missing data so a Lua test can branch on absence cleanly.
// All mutation seams return 1 on success, 0 on rejection (e.g. unknown
// faction). Mutation seams never throw -- they are designed to be
// driven from a synthetic Lua test.
// ----------------------------------------------------------------------

// SWFOC_ReplayPlayerCredits(faction) -> number
// Returns the credits of the named faction, or -1 if no such faction.
static int Lua_ReplayPlayerCredits(lua_State* L) {
    const char* faction = fn_tostring(L, 1);
    if (!faction) { fn_pushnumber(L, -1.0); return 1; }
    std::string needle = ToUpperAscii(faction);
    for (const auto& p : g_replay.players) {
        if (ToUpperAscii(p.faction_name) == needle) {
            fn_pushnumber(L, p.credits);
            return 1;
        }
    }
    fn_pushnumber(L, -1.0);
    return 1;
}

// SWFOC_ReplayPlayerTechLevel(faction) -> number
// Returns the tech_level of the named faction, or -1 if no such faction.
static int Lua_ReplayPlayerTechLevel(lua_State* L) {
    const char* faction = fn_tostring(L, 1);
    if (!faction) { fn_pushnumber(L, -1.0); return 1; }
    std::string needle = ToUpperAscii(faction);
    for (const auto& p : g_replay.players) {
        if (ToUpperAscii(p.faction_name) == needle) {
            fn_pushnumber(L, static_cast<double>(p.tech_level));
            return 1;
        }
    }
    fn_pushnumber(L, -1.0);
    return 1;
}

// SWFOC_ReplayLastStoryEvent() -> string ("" if nothing pushed yet)
static int Lua_ReplayLastStoryEvent(lua_State* L) {
    fn_pushstring(L, g_replay.last_story_event.c_str());
    return 1;
}

// SWFOC_ReplayPushStoryEvent(event) -> 1 on success
// Mutation seam: records the event id so SWFOC_ReplayLastStoryEvent can
// observe it later.
static int Lua_ReplayPushStoryEvent(lua_State* L) {
    const char* event = fn_tostring(L, 1);
    if (!event) { fn_pushnumber(L, 0); return 1; }
    g_replay.last_story_event = event;
    fn_pushnumber(L, 1);
    return 1;
}

// SWFOC_ReplayDiplomaticState(a, b) -> "allied" / "hostile" / "neutral"
// Defaults to "hostile" for any pair that has not been explicitly set.
static int Lua_ReplayDiplomaticState(lua_State* L) {
    const char* a = fn_tostring(L, 1);
    const char* b = fn_tostring(L, 2);
    if (!a || !b) { fn_pushstring(L, "hostile"); return 1; }
    auto key = MakeDiplomacyKey(a, b);
    auto it = g_replay.diplomacy.find(key);
    if (it == g_replay.diplomacy.end()) {
        fn_pushstring(L, "hostile");
        return 1;
    }
    fn_pushstring(L, it->second.c_str());
    return 1;
}

// SWFOC_ReplaySetDiplomacy(a, b, state) -> 1 on success, 0 on bad input
// Mutation seam paired with SWFOC_ReplayDiplomaticState.
static int Lua_ReplaySetDiplomacy(lua_State* L) {
    const char* a = fn_tostring(L, 1);
    const char* b = fn_tostring(L, 2);
    const char* st = fn_tostring(L, 3);
    if (!a || !b || !st) { fn_pushnumber(L, 0); return 1; }
    g_replay.diplomacy[MakeDiplomacyKey(a, b)] = st;
    fn_pushnumber(L, 1);
    return 1;
}

// SWFOC_ReplayPlanetCorruption(planet) -> float in [0, 1] or -1 if unknown.
static int Lua_ReplayPlanetCorruption(lua_State* L) {
    const char* planet = fn_tostring(L, 1);
    if (!planet) { fn_pushnumber(L, -1.0); return 1; }
    auto it = g_replay.planets.find(ToUpperAscii(planet));
    if (it == g_replay.planets.end()) {
        fn_pushnumber(L, -1.0);
        return 1;
    }
    fn_pushnumber(L, static_cast<double>(it->second.corruption));
    return 1;
}

// SWFOC_ReplaySetPlanetCorruption(planet, value) -> 1 on success
// Creates the planet record if it does not yet exist.
static int Lua_ReplaySetPlanetCorruption(lua_State* L) {
    const char* planet = fn_tostring(L, 1);
    if (!planet) { fn_pushnumber(L, 0); return 1; }
    double value = fn_tonumber(L, 2);
    std::string key = ToUpperAscii(planet);
    auto& info = g_replay.planets[key];
    if (info.name.empty()) info.name = planet;
    info.corruption = static_cast<float>(value);
    fn_pushnumber(L, 1);
    return 1;
}

// SWFOC_ReplayUnitOwner(type, index) -> slot index, or -1 if out of range.
static int Lua_ReplayUnitOwner(lua_State* L) {
    const char* type_name = fn_tostring(L, 1);
    if (!type_name) { fn_pushnumber(L, -1.0); return 1; }
    int index = static_cast<int>(fn_tonumber(L, 2));
    if (index < 0) { fn_pushnumber(L, -1.0); return 1; }
    auto it = g_replay.object_owners.find(ToUpperAscii(type_name));
    if (it == g_replay.object_owners.end() ||
        index >= static_cast<int>(it->second.size())) {
        fn_pushnumber(L, -1.0);
        return 1;
    }
    fn_pushnumber(L, static_cast<double>(it->second[index]));
    return 1;
}

// SWFOC_ReplaySpawnUnit(faction, type, count) -> 1 on success
// Mutation seam: appends `count` instances of `type` owned by the player
// whose faction matches `faction`. Falls back to slot -1 (unowned) if no
// matching faction is registered. Mirrors the change in `objects` so the
// existing SWFOC_ReplayObjectCount helper still reports the correct total.
static int Lua_ReplaySpawnUnit(lua_State* L) {
    const char* faction = fn_tostring(L, 1);
    const char* type_name = fn_tostring(L, 2);
    if (!faction || !type_name) { fn_pushnumber(L, 0); return 1; }
    int count = static_cast<int>(fn_tonumber(L, 3));
    if (count <= 0) { fn_pushnumber(L, 0); return 1; }

    int32_t owner_slot = -1;
    std::string needle = ToUpperAscii(faction);
    for (const auto& p : g_replay.players) {
        if (ToUpperAscii(p.faction_name) == needle) {
            owner_slot = static_cast<int32_t>(p.slot);
            break;
        }
    }

    std::string type_key = ToUpperAscii(type_name);
    auto& owners = g_replay.object_owners[type_key];
    for (int i = 0; i < count; i++) owners.push_back(owner_slot);

    // Keep the section-3 catalog in sync so existing observers still work.
    g_replay.objects[type_name] += static_cast<uint32_t>(count);

    fn_pushnumber(L, 1);
    return 1;
}

// SWFOC_ReplayCooldownState(unit_type, ability_index) -> float, or -1 if missing.
static int Lua_ReplayCooldownState(lua_State* L) {
    const char* unit_type = fn_tostring(L, 1);
    if (!unit_type) { fn_pushnumber(L, -1.0); return 1; }
    int ability_idx = static_cast<int>(fn_tonumber(L, 2));
    if (ability_idx < 0) { fn_pushnumber(L, -1.0); return 1; }
    auto it = g_replay.cooldowns.find(unit_type);
    if (it == g_replay.cooldowns.end() ||
        ability_idx >= static_cast<int>(it->second.size())) {
        fn_pushnumber(L, -1.0);
        return 1;
    }
    fn_pushnumber(L, static_cast<double>(it->second[ability_idx]));
    return 1;
}

// SWFOC_ReplaySetCooldown(unit_type, ability_idx, value) -> 1 on success
// Mutation seam: writes (and grows the slot vector if needed) the cooldown
// value for the named unit type's ability slot.
static int Lua_ReplaySetCooldown(lua_State* L) {
    const char* unit_type = fn_tostring(L, 1);
    if (!unit_type) { fn_pushnumber(L, 0); return 1; }
    int ability_idx = static_cast<int>(fn_tonumber(L, 2));
    double value = fn_tonumber(L, 3);
    if (ability_idx < 0 || ability_idx > 256) { fn_pushnumber(L, 0); return 1; }
    auto& slots = g_replay.cooldowns[unit_type];
    if (static_cast<int>(slots.size()) <= ability_idx) {
        slots.resize(static_cast<size_t>(ability_idx) + 1, 0.0f);
    }
    slots[static_cast<size_t>(ability_idx)] = static_cast<float>(value);
    fn_pushnumber(L, 1);
    return 1;
}

// SWFOC_ReplayTaskForceCount(slot) -> number of task forces owned by slot.
static int Lua_ReplayTaskForceCount(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    int count = 0;
    for (const auto& tf : g_replay.task_forces) {
        if (tf.owner_slot == slot) count++;
    }
    fn_pushnumber(L, static_cast<double>(count));
    return 1;
}

// SWFOC_ReplayAddTaskForce(slot, name) -> 1 on success
// Mutation seam paired with SWFOC_ReplayTaskForceCount.
static int Lua_ReplayAddTaskForce(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    const char* name = fn_tostring(L, 2);
    if (!name) { fn_pushnumber(L, 0); return 1; }
    ReplayTaskForceRecord rec;
    rec.owner_slot = slot;
    rec.name = name;
    g_replay.task_forces.push_back(std::move(rec));
    fn_pushnumber(L, 1);
    return 1;
}

// SWFOC_ReplayHumanPlayerSlot() -> current local player slot, -1 if unset.
static int Lua_ReplayHumanPlayerSlot(lua_State* L) {
    fn_pushnumber(L, static_cast<double>(g_replay.local_slot));
    return 1;
}

// SWFOC_ReplaySwitchLocalPlayer(slot) -> 1 on success, 0 on bad slot
// Mutation seam: updates g_replay.local_slot at runtime so subsequent
// SWFOC_GetLocalPlayer / SWFOC_GetCredits calls reflect the new slot.
static int Lua_ReplaySwitchLocalPlayer(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    // -1 explicitly resets to "no local player".
    if (slot == -1) {
        g_replay.local_slot = -1;
        fn_pushnumber(L, 1);
        return 1;
    }
    for (const auto& p : g_replay.players) {
        if (static_cast<int>(p.slot) == slot) {
            g_replay.local_slot = slot;
            fn_pushnumber(L, 1);
            return 1;
        }
    }
    fn_pushnumber(L, 0);
    return 1;
}

// ----------------------------------------------------------------------
// Unit / hardpoint / behavior helpers (Task 101 — added 2026-04-23).
//
// These back the autonomous-iteration surface for Tasks 99 and 100: Lua
// tests can mock a unit, attach hardpoint behaviors, apply damage ticks,
// and assert which write path actually confers immunity. The helpers
// delegate to ReplayMut*/ReplayObs* pure-state functions in replay_state.h
// so the test harness can exercise the same semantics without going
// through the fake Lua stack.
// ----------------------------------------------------------------------

// SWFOC_ReplayMockUnit(obj_addr, type_name, owner_slot, hull, max_hull, hardpoint_count) -> 1
// Constructs (or upserts) a synthetic unit record. Hardpoints start with
// no attached behaviors.
static int Lua_ReplayMockUnit(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    const char* type_name = fn_tostring(L, 2);
    int owner_slot = static_cast<int>(fn_tonumber(L, 3));
    double hull = fn_tonumber(L, 4);
    double max_hull = fn_tonumber(L, 5);
    int hp_count = static_cast<int>(fn_tonumber(L, 6));
    if (obj_addr == 0 || hp_count < 0 || hp_count > 256) {
        fn_pushnumber(L, 0);
        return 1;
    }
    ReplayMutMockUnit(
        g_replay, obj_addr,
        type_name ? type_name : "",
        static_cast<int32_t>(owner_slot),
        static_cast<float>(hull),
        static_cast<float>(max_hull),
        static_cast<uint32_t>(hp_count));
    fn_pushnumber(L, 1);
    return 1;
}

// SWFOC_ReplayUnitHull(obj_addr) -> current hull, or -1 if unknown unit.
static int Lua_ReplayUnitHull(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    const auto* u = ReplayFindUnit(g_replay, obj_addr);
    fn_pushnumber(L, u ? static_cast<double>(u->hull) : -1.0);
    return 1;
}

// SWFOC_ReplayUnitMaxHull(obj_addr) -> max_hull, or -1 if unknown unit.
static int Lua_ReplayUnitMaxHull(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    const auto* u = ReplayFindUnit(g_replay, obj_addr);
    fn_pushnumber(L, u ? static_cast<double>(u->max_hull) : -1.0);
    return 1;
}

// SWFOC_ReplayUnitOwnerSlot(obj_addr) -> slot index, or -1 if unknown unit.
static int Lua_ReplayUnitOwnerSlot(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    const auto* u = ReplayFindUnit(g_replay, obj_addr);
    fn_pushnumber(L, u ? static_cast<double>(u->owner_slot) : -1.0);
    return 1;
}

// SWFOC_ReplayUnitInvulnFlag(obj_addr) -> display flag (0/1), -1 if unknown.
// Matches the current Lua_SetUnitInvuln byte at GameObj+0x3A7 — useful for
// regression tests that assert "flag flipped in memory but damage still
// applied" (i.e. the byte write is a no-op for gameplay).
static int Lua_ReplayUnitInvulnFlag(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    const auto* u = ReplayFindUnit(g_replay, obj_addr);
    fn_pushnumber(L, u ? static_cast<double>(u->invuln_flag) : -1.0);
    return 1;
}

// SWFOC_ReplayUnitPreventDeath(obj_addr) -> bit-0x80 of +0x3A1 (0/1), -1 if unknown.
static int Lua_ReplayUnitPreventDeath(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    const auto* u = ReplayFindUnit(g_replay, obj_addr);
    if (!u) { fn_pushnumber(L, -1.0); return 1; }
    fn_pushnumber(L, (u->prevent_death & 0x80) ? 1.0 : 0.0);
    return 1;
}

// SWFOC_ReplayHardpointCount(obj_addr) -> hardpoint count, -1 if unknown unit.
static int Lua_ReplayHardpointCount(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    const auto* u = ReplayFindUnit(g_replay, obj_addr);
    fn_pushnumber(L, u ? static_cast<double>(u->hardpoints.size()) : -1.0);
    return 1;
}

// SWFOC_ReplayHardpointHasBehavior(obj_addr, hp_index, behavior) -> 1 if attached, 0 if not, -1 on bad input.
static int Lua_ReplayHardpointHasBehavior(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    int hp_index = static_cast<int>(fn_tonumber(L, 2));
    const char* behavior = fn_tostring(L, 3);
    const auto* u = ReplayFindUnit(g_replay, obj_addr);
    if (!u || !behavior) { fn_pushnumber(L, -1.0); return 1; }
    if (hp_index < 0 || hp_index >= static_cast<int>(u->hardpoints.size())) {
        fn_pushnumber(L, -1.0);
        return 1;
    }
    bool has = ReplayHardpointHasBehavior(u->hardpoints[static_cast<size_t>(hp_index)], behavior);
    fn_pushnumber(L, has ? 1.0 : 0.0);
    return 1;
}

// SWFOC_ReplayUnitIsInvulnerable(obj_addr) -> 1 if any hardpoint has "INVULNERABLE", 0 otherwise, -1 on bad input.
// This is the single predicate that distinguishes the flag-flip (no effect)
// from the hardpoint-behavior path (real immunity) — Tasks 99/100 use it
// as the gating assertion.
static int Lua_ReplayUnitIsInvulnerable(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    const auto* u = ReplayFindUnit(g_replay, obj_addr);
    if (!u) { fn_pushnumber(L, -1.0); return 1; }
    bool inv = ReplayUnitAnyHardpointHasBehavior(*u, "INVULNERABLE");
    fn_pushnumber(L, inv ? 1.0 : 0.0);
    return 1;
}

// SWFOC_ReplayAttachBehavior(obj_addr, hp_index, behavior) -> 1 on success.
// Low-level primitive; most tests should prefer SWFOC_ReplayMakeInvulnerable.
static int Lua_ReplayAttachBehavior(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    int hp_index = static_cast<int>(fn_tonumber(L, 2));
    const char* behavior = fn_tostring(L, 3);
    if (!behavior) { fn_pushnumber(L, 0); return 1; }
    int rc = ReplayMutAttachBehavior(g_replay, obj_addr, hp_index, behavior);
    fn_pushnumber(L, rc);
    return 1;
}

// SWFOC_ReplayDetachBehavior(obj_addr, hp_index, behavior) -> 1 if detached, 0 if not present.
static int Lua_ReplayDetachBehavior(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    int hp_index = static_cast<int>(fn_tonumber(L, 2));
    const char* behavior = fn_tostring(L, 3);
    if (!behavior) { fn_pushnumber(L, 0); return 1; }
    int rc = ReplayMutDetachBehavior(g_replay, obj_addr, hp_index, behavior);
    fn_pushnumber(L, rc);
    return 1;
}

// SWFOC_ReplayMakeInvulnerable(obj_addr, flag) -> 1 on success, 0 on unknown unit.
// Mirrors the engine's Make_Invulnerable Lua wrapper: iterates every
// hardpoint and attaches/detaches the INVULNERABLE behavior. This is the
// CORRECT path for Task 99 — the mutation results in
// ReplayUnitIsInvulnerable returning 1 and ReplayApplyDamage being a no-op.
static int Lua_ReplayMakeInvulnerable(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    double raw_flag = fn_tonumber(L, 2);
    int rc = ReplayMutMakeInvulnerable(g_replay, obj_addr, raw_flag != 0.0);
    fn_pushnumber(L, rc);
    return 1;
}

// SWFOC_ReplaySetUnitInvulnFlag(obj_addr, flag) -> 1 on success, 0 on unknown unit.
// Mirrors the CURRENT Lua_SetUnitInvuln byte-poke at GameObj+0x3A7. Damage
// simulation intentionally does NOT honour this flag — tests can use this
// to assert "flag flipped, byte landed in memory, but damage still
// applied" (the 2026-04-23 live observation).
static int Lua_ReplaySetUnitInvulnFlag(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    uint8_t flag = (fn_tonumber(L, 2) != 0.0) ? 1 : 0;
    int rc = ReplayMutSetUnitInvulnFlag(g_replay, obj_addr, flag);
    fn_pushnumber(L, rc);
    return 1;
}

// SWFOC_ReplaySetPreventDeathBit(obj_addr, flag) -> 1 on success.
// Bit 0x80 of GameObj+0x3A1 — same display-only nature as InvulnFlag.
static int Lua_ReplaySetPreventDeathBit(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    bool set = fn_tonumber(L, 2) != 0.0;
    int rc = ReplayMutSetPreventDeathBit(g_replay, obj_addr, set);
    fn_pushnumber(L, rc);
    return 1;
}

// SWFOC_ReplaySetUnitHull(obj_addr, value) -> 1 on success, 0 if unknown unit.
// Clamps to [0, max_hull]; mirrors the live 99999 -> max_hull behaviour.
static int Lua_ReplaySetUnitHull(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    double value = fn_tonumber(L, 2);
    int rc = ReplayMutSetUnitHull(g_replay, obj_addr, static_cast<float>(value));
    fn_pushnumber(L, rc);
    return 1;
}

// SWFOC_ReplayApplyDamage(obj_addr, amount) -> new hull, -1 if unknown unit.
// Honours hardpoint INVULNERABLE behaviors (immune units do not lose hull);
// ignores the display-only invuln_flag / prevent_death bits. Simulates one
// damage tick.
static int Lua_ReplayApplyDamage(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    double amount = fn_tonumber(L, 2);
    float new_hull = ReplayMutApplyDamage(g_replay, obj_addr, static_cast<float>(amount));
    fn_pushnumber(L, static_cast<double>(new_hull));
    return 1;
}

// SWFOC_ReplaySetSelected(obj_addr) -> 1. Replaces selection with a single unit.
static int Lua_ReplaySetSelected(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    int rc = ReplayMutSetSelected(g_replay, obj_addr);
    fn_pushnumber(L, rc);
    return 1;
}

// SWFOC_ReplayAppendSelected(obj_addr) -> 1 on success. Adds to the selection
// (idempotent if the obj_addr is already present).
static int Lua_ReplayAppendSelected(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    int rc = ReplayMutAppendSelected(g_replay, obj_addr);
    fn_pushnumber(L, rc);
    return 1;
}

// SWFOC_ReplayClearSelected() -> 1
static int Lua_ReplayClearSelected(lua_State* L) {
    (void)L;
    ReplayMutClearSelected(g_replay);
    fn_pushnumber(L, 1);
    return 1;
}

// SWFOC_ReplayGetSelectedUnit() -> first selected obj_addr, 0 if empty.
// Mirrors the live SWFOC_GetSelectedUnit helper in lua_bridge.cpp.
static int Lua_ReplayGetSelectedUnit(lua_State* L) {
    uint64_t obj = ReplayObsGetSelectedUnit(g_replay);
    fn_pushnumber(L, static_cast<double>(obj));
    return 1;
}

// SWFOC_ReplaySelectedCount() -> number of currently selected units.
static int Lua_ReplaySelectedCount(lua_State* L) {
    fn_pushnumber(L, static_cast<double>(ReplayObsSelectedCount(g_replay)));
    return 1;
}

// SWFOC_ReplayUnitCount() -> number of mocked units currently in the state.
static int Lua_ReplayUnitCount(lua_State* L) {
    fn_pushnumber(L, static_cast<double>(g_replay.units.size()));
    return 1;
}

// SWFOC_ReplaySetPlanetTech / SetPlanetBuildings / SetPlanetCapital / GetPlanetTech
// / GetPlanetBuildings / GetPlanetTechAndBuildings  (Task 143).
static int Lua_ReplaySetPlanetTech(lua_State* L) {
    const char* raw = fn_tostring(L, 1);
    int tech = static_cast<int>(fn_tonumber(L, 2));
    std::string name = raw ? std::string(raw) : std::string();
    fn_pushnumber(L, static_cast<double>(
        ReplayMutSetPlanetTech(g_replay, name, static_cast<int32_t>(tech))));
    return 1;
}

static int Lua_ReplaySetPlanetBuildings(lua_State* L) {
    const char* raw = fn_tostring(L, 1);
    int count = static_cast<int>(fn_tonumber(L, 2));
    std::string name = raw ? std::string(raw) : std::string();
    fn_pushnumber(L, static_cast<double>(
        ReplayMutSetPlanetBuildings(g_replay, name, static_cast<int32_t>(count))));
    return 1;
}

static int Lua_ReplaySetPlanetCapital(lua_State* L) {
    const char* raw = fn_tostring(L, 1);
    int flag = static_cast<int>(fn_tonumber(L, 2));
    std::string name = raw ? std::string(raw) : std::string();
    fn_pushnumber(L, static_cast<double>(
        ReplayMutSetPlanetCapital(g_replay, name, flag != 0)));
    return 1;
}

static int Lua_ReplayPlanetTech(lua_State* L) {
    const char* raw = fn_tostring(L, 1);
    std::string name = raw ? std::string(raw) : std::string();
    fn_pushnumber(L, static_cast<double>(ReplayObsGetPlanetTech(g_replay, name)));
    return 1;
}

static int Lua_ReplayPlanetBuildings(lua_State* L) {
    const char* raw = fn_tostring(L, 1);
    std::string name = raw ? std::string(raw) : std::string();
    fn_pushnumber(L, static_cast<double>(ReplayObsGetPlanetBuildings(g_replay, name)));
    return 1;
}

static int Lua_ReplayPlanetTechAndBuildings(lua_State* L) {
    const char* raw = fn_tostring(L, 1);
    std::string name = raw ? std::string(raw) : std::string();
    std::string row = ReplayObsGetPlanetTechAndBuildings(g_replay, name);
    fn_pushstring(L, row.c_str());
    return 1;
}

// SWFOC_ReplaySetIncomeMultiplier / GetIncomeMultiplier / SetGameSpeed /
// GetGameSpeed / SetFreezeCredits / IsFreezeCredits / GetFreezeCreditsTarget /
// TickIncome  (Tasks 122/123/127).
static int Lua_ReplaySetBuildSpeed(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    double mult = fn_tonumber(L, 2);
    fn_pushnumber(L, static_cast<double>(ReplayMutSetBuildSpeed(
        g_replay, static_cast<int32_t>(slot), static_cast<float>(mult))));
    return 1;
}
static int Lua_ReplayGetBuildSpeed(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetBuildSpeed(
        g_replay, static_cast<int32_t>(slot))));
    return 1;
}
static int Lua_ReplaySetFactionSpeedMult(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    double mult = fn_tonumber(L, 2);
    fn_pushnumber(L, static_cast<double>(ReplayMutSetFactionSpeedMult(
        g_replay, static_cast<int32_t>(slot), static_cast<float>(mult))));
    return 1;
}
static int Lua_ReplayGetFactionSpeedMult(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetFactionSpeedMult(
        g_replay, static_cast<int32_t>(slot))));
    return 1;
}

static int Lua_ReplaySetIncomeMultiplier(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    double mult = fn_tonumber(L, 2);
    fn_pushnumber(L, static_cast<double>(ReplayMutSetIncomeMultiplier(
        g_replay, static_cast<int32_t>(slot), static_cast<float>(mult))));
    return 1;
}

static int Lua_ReplayGetIncomeMultiplier(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetIncomeMultiplier(
        g_replay, static_cast<int32_t>(slot))));
    return 1;
}

static int Lua_ReplaySetGameSpeed(lua_State* L) {
    double speed = fn_tonumber(L, 1);
    fn_pushnumber(L, static_cast<double>(ReplayMutSetGameSpeed(
        g_replay, static_cast<float>(speed))));
    return 1;
}

static int Lua_ReplayGetGameSpeed(lua_State* L) {
    (void)L;
    fn_pushnumber(L, static_cast<double>(ReplayObsGetGameSpeed(g_replay)));
    return 1;
}

static int Lua_ReplaySetFreezeCredits(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    int enable = static_cast<int>(fn_tonumber(L, 2));
    double target = fn_tonumber(L, 3);
    fn_pushnumber(L, static_cast<double>(ReplayMutSetFreezeCredits(
        g_replay, static_cast<int32_t>(slot), enable != 0, target)));
    return 1;
}

static int Lua_ReplayIsFreezeCredits(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsIsFreezeCredits(
        g_replay, static_cast<int32_t>(slot))));
    return 1;
}

static int Lua_ReplayFreezeCreditsTarget(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, ReplayObsGetFreezeCreditsTarget(
        g_replay, static_cast<int32_t>(slot)));
    return 1;
}

static int Lua_ReplayTickIncome(lua_State* L) {
    double base = fn_tonumber(L, 1);
    fn_pushnumber(L, static_cast<double>(ReplayMutTickIncome(g_replay, base)));
    return 1;
}

// SWFOC_ReplaySetFireRate / GetFireRate / ApplyFireRate (Task 131).
// SWFOC_ReplaySetAreaDamage / IsAreaDamage / ApplyAreaSplash (Task 132).
// SWFOC_ReplaySetTargetFilter / GetTargetFilter / IsTargetAllowed (Task 133).
static int Lua_ReplaySetFireRate(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    float mult = static_cast<float>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetFireRate(g_replay, slot, mult)));
    return 1;
}

static int Lua_ReplayGetFireRate(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetFireRate(g_replay, slot)));
    return 1;
}

static int Lua_ReplayApplyFireRate(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    float base = static_cast<float>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutApplyFireRate(g_replay, slot, base)));
    return 1;
}

static int Lua_ReplaySetAreaDamage(lua_State* L) {
    int enabled = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetAreaDamageEnabled(g_replay, enabled != 0)));
    return 1;
}

static int Lua_ReplayIsAreaDamage(lua_State* L) {
    fn_pushnumber(L, ReplayObsIsAreaDamageEnabled(g_replay) ? 1.0 : 0.0);
    return 1;
}

static int Lua_ReplayApplyAreaSplash(lua_State* L) {
    uint64_t primary = static_cast<uint64_t>(fn_tonumber(L, 1));
    float amount = static_cast<float>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutApplyAreaSplash(g_replay, primary, amount)));
    return 1;
}

static int Lua_ReplaySetTargetFilter(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    uint32_t bitmask = static_cast<uint32_t>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetTargetFilter(g_replay, slot, bitmask)));
    return 1;
}

static int Lua_ReplayGetTargetFilter(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetTargetFilter(g_replay, slot)));
    return 1;
}

static int Lua_ReplayIsTargetAllowed(lua_State* L) {
    int attacker_slot = static_cast<int>(fn_tonumber(L, 1));
    uint32_t target_kind = static_cast<uint32_t>(fn_tonumber(L, 2));
    fn_pushnumber(L, ReplayObsIsTargetAllowed(g_replay, attacker_slot, target_kind) ? 1.0 : 0.0);
    return 1;
}

// SWFOC_ReplaySetInstantBuild / IsInstantBuildEnabled / ShouldBuildComplete (Task 161).
// SWFOC_ReplaySetFreeBuild / IsFreeBuildEnabled / ComputeBuildCost (Task 162).
static int Lua_ReplaySetInstantBuild(lua_State* L) {
    int e = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetInstantBuild(g_replay, e != 0)));
    return 1;
}
static int Lua_ReplayIsInstantBuild(lua_State* L) {
    fn_pushnumber(L, ReplayObsIsInstantBuildEnabled(g_replay) ? 1.0 : 0.0);
    return 1;
}
static int Lua_ReplayShouldBuildComplete(lua_State* L) {
    int q = static_cast<int>(fn_tonumber(L, 1));
    int e = static_cast<int>(fn_tonumber(L, 2));
    fn_pushnumber(L, ReplayObsShouldBuildComplete(g_replay, q, e) ? 1.0 : 0.0);
    return 1;
}
static int Lua_ReplaySetFreeBuild(lua_State* L) {
    int e = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetFreeBuild(g_replay, e != 0)));
    return 1;
}
static int Lua_ReplayIsFreeBuild(lua_State* L) {
    fn_pushnumber(L, ReplayObsIsFreeBuildEnabled(g_replay) ? 1.0 : 0.0);
    return 1;
}
static int Lua_ReplayComputeBuildCost(lua_State* L) {
    int   slot = static_cast<int>(fn_tonumber(L, 1));
    float base = static_cast<float>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayObsComputeBuildCost(g_replay, slot, base)));
    return 1;
}

// SWFOC_ReplaySetUnitField / GetUnitField (Task 157).
static int Lua_ReplaySetUnitField(lua_State* L) {
    uint64_t    addr  = static_cast<uint64_t>(fn_tonumber(L, 1));
    const char* raw_f = fn_tostring(L, 2);
    float       val   = static_cast<float>(fn_tonumber(L, 3));
    std::string field = raw_f ? std::string(raw_f) : std::string();
    fn_pushnumber(L, static_cast<double>(ReplayMutSetUnitField(
        g_replay, addr, field, val)));
    return 1;
}

static int Lua_ReplayGetUnitField(lua_State* L) {
    uint64_t    addr  = static_cast<uint64_t>(fn_tonumber(L, 1));
    const char* raw_f = fn_tostring(L, 2);
    std::string field = raw_f ? std::string(raw_f) : std::string();
    float v = ReplayObsGetUnitField(g_replay, addr, field);
    // NaN transport over the pipe is fragile; emit a distinct sentinel
    // (-1234567.5f) when NaN is returned so pipe consumers can still
    // distinguish unknown from legitimate values.
    if (v != v) v = -1234567.5f;
    fn_pushnumber(L, static_cast<double>(v));
    return 1;
}

// SWFOC_ReplaySpawnUnits (Task 159).
// SWFOC_ReplaySetBuildCost / GetBuildCost (Task 160).
// SWFOC_ReplaySetUnitCapOverride / ClearUnitCapOverride / GetUnitCapOverride (Task 163).
static int Lua_ReplaySpawnUnits(lua_State* L) {
    const char* raw_type = fn_tostring(L, 1);
    int slot  = static_cast<int>(fn_tonumber(L, 2));
    int count = static_cast<int>(fn_tonumber(L, 3));
    std::string type_name = raw_type ? std::string(raw_type) : std::string();
    fn_pushnumber(L, static_cast<double>(ReplayMutSpawnUnits(
        g_replay, type_name, static_cast<int32_t>(slot), static_cast<int32_t>(count))));
    return 1;
}
static int Lua_ReplaySetBuildCost(lua_State* L) {
    int   slot = static_cast<int>(fn_tonumber(L, 1));
    float mult = static_cast<float>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetBuildCost(
        g_replay, static_cast<int32_t>(slot), mult)));
    return 1;
}
static int Lua_ReplayGetBuildCost(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetBuildCost(
        g_replay, static_cast<int32_t>(slot))));
    return 1;
}
static int Lua_ReplaySetUnitCapOverride(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    int cap  = static_cast<int>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetUnitCapOverride(
        g_replay, static_cast<int32_t>(slot), static_cast<int32_t>(cap))));
    return 1;
}
static int Lua_ReplayClearUnitCapOverride(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayMutClearUnitCapOverride(
        g_replay, static_cast<int32_t>(slot))));
    return 1;
}
static int Lua_ReplayGetUnitCapOverride(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetUnitCapOverride(
        g_replay, static_cast<int32_t>(slot))));
    return 1;
}

// SWFOC_ReplaySetAiFrozen / IsAiFrozen / FrozenAiCount (Task 114).
static int Lua_ReplaySetAiFrozen(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    int frozen = static_cast<int>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetAiFrozen(
        g_replay, static_cast<int32_t>(slot), frozen != 0)));
    return 1;
}
static int Lua_ReplayIsAiFrozen(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, ReplayObsIsAiFrozen(g_replay, static_cast<int32_t>(slot)) ? 1.0 : 0.0);
    return 1;
}
static int Lua_ReplayFrozenAiCount(lua_State* L) {
    fn_pushnumber(L, static_cast<double>(ReplayObsFrozenAiCount(g_replay)));
    return 1;
}

// SWFOC_ReplaySetCameraUnlocked / IsCameraUnlocked / SetCameraPos /
// GetCameraX/Y/Z / SetCameraRot / GetCameraRot / SetCameraZoom /
// GetCameraZoom (Task 115).
static int Lua_ReplaySetCameraUnlocked(lua_State* L) {
    int u = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetCameraUnlocked(g_replay, u != 0)));
    return 1;
}
static int Lua_ReplayIsCameraUnlocked(lua_State* L) {
    fn_pushnumber(L, ReplayObsIsCameraUnlocked(g_replay) ? 1.0 : 0.0);
    return 1;
}
static int Lua_ReplaySetCameraPos(lua_State* L) {
    float x = static_cast<float>(fn_tonumber(L, 1));
    float y = static_cast<float>(fn_tonumber(L, 2));
    float z = static_cast<float>(fn_tonumber(L, 3));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetCameraPos(g_replay, x, y, z)));
    return 1;
}
static int Lua_ReplayGetCameraX(lua_State* L) {
    fn_pushnumber(L, static_cast<double>(ReplayObsGetCameraX(g_replay)));
    return 1;
}
static int Lua_ReplayGetCameraY(lua_State* L) {
    fn_pushnumber(L, static_cast<double>(ReplayObsGetCameraY(g_replay)));
    return 1;
}
static int Lua_ReplayGetCameraZ(lua_State* L) {
    fn_pushnumber(L, static_cast<double>(ReplayObsGetCameraZ(g_replay)));
    return 1;
}
static int Lua_ReplaySetCameraRot(lua_State* L) {
    float r = static_cast<float>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetCameraRot(g_replay, r)));
    return 1;
}
static int Lua_ReplayGetCameraRot(lua_State* L) {
    fn_pushnumber(L, static_cast<double>(ReplayObsGetCameraRot(g_replay)));
    return 1;
}
static int Lua_ReplaySetCameraZoom(lua_State* L) {
    float z = static_cast<float>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetCameraZoom(g_replay, z)));
    return 1;
}
static int Lua_ReplayGetCameraZoom(lua_State* L) {
    fn_pushnumber(L, static_cast<double>(ReplayObsGetCameraZoom(g_replay)));
    return 1;
}

// SWFOC_ReplaySetOHK / IsOHK / GetAttackPower (Task 105).
static int Lua_ReplaySetOHK(lua_State* L) {
    int enable = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetOHK(g_replay, enable != 0)));
    return 1;
}

static int Lua_ReplayIsOHK(lua_State* L) {
    fn_pushnumber(L, ReplayObsIsOHK(g_replay) ? 1.0 : 0.0);
    return 1;
}

static int Lua_ReplayGetAttackPower(lua_State* L) {
    uint64_t addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetAttackPower(g_replay, addr)));
    return 1;
}

// SWFOC_ReplayAddUnitAbility / ListAbilities / TriggerAbility / TickAbilityCooldown /
// AbilityCooldown (Tasks 139/140).
static int Lua_ReplayAddUnitAbility(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    int index = static_cast<int>(fn_tonumber(L, 2));
    const char* raw_name = fn_tostring(L, 3);
    int cooldown = static_cast<int>(fn_tonumber(L, 4));
    int usable = static_cast<int>(fn_tonumber(L, 5));
    std::string name = raw_name ? std::string(raw_name) : std::string();
    fn_pushnumber(L, static_cast<double>(ReplayMutAddUnitAbility(
        g_replay, obj_addr, static_cast<int32_t>(index), name,
        static_cast<int32_t>(cooldown), usable != 0)));
    return 1;
}

static int Lua_ReplayListAbilities(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    std::string out = ReplayObsListAbilities(g_replay, obj_addr);
    fn_pushstring(L, out.c_str());
    return 1;
}

static int Lua_ReplayTriggerAbility(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    int index = static_cast<int>(fn_tonumber(L, 2));
    int cooldown = static_cast<int>(fn_tonumber(L, 3));
    fn_pushnumber(L, static_cast<double>(ReplayMutTriggerAbility(
        g_replay, obj_addr, static_cast<int32_t>(index), static_cast<int32_t>(cooldown))));
    return 1;
}

static int Lua_ReplayTickAbilityCooldown(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    int delta = static_cast<int>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutTickAbilityCooldown(
        g_replay, obj_addr, static_cast<int32_t>(delta))));
    return 1;
}

static int Lua_ReplayAbilityCooldown(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    int index = static_cast<int>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayObsAbilityCooldown(
        g_replay, obj_addr, static_cast<int32_t>(index))));
    return 1;
}

// SWFOC_ReplayListPlanets() -> CSV. Task 141.
static int Lua_ReplayListPlanets(lua_State* L) {
    std::string out = ReplayObsListPlanets(g_replay);
    fn_pushstring(L, out.c_str());
    return 1;
}

// SWFOC_ReplayChangePlanetOwner("name", slot) -> 1 on success. Task 142.
static int Lua_ReplayChangePlanetOwner(lua_State* L) {
    const char* raw = fn_tostring(L, 1);
    int slot = static_cast<int>(fn_tonumber(L, 2));
    std::string name = raw ? std::string(raw) : std::string();
    fn_pushnumber(L, static_cast<double>(
        ReplayMutChangePlanetOwner(g_replay, name, static_cast<int32_t>(slot))));
    return 1;
}

// SWFOC_ReplayPlanetOwner("name") -> slot or -1. Task 141/142 observer.
static int Lua_ReplayPlanetOwner(lua_State* L) {
    const char* raw = fn_tostring(L, 1);
    std::string name = raw ? std::string(raw) : std::string();
    fn_pushnumber(L, static_cast<double>(
        ReplayObsGetPlanetOwner(g_replay, name)));
    return 1;
}

// SWFOC_ReplayHeroStatEdit(obj, "field", value) -> 1 on success. Task 138.
static int Lua_ReplayHeroStatEdit(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    const char* raw = fn_tostring(L, 2);
    float value = static_cast<float>(fn_tonumber(L, 3));
    std::string field = raw ? std::string(raw) : std::string();
    fn_pushnumber(L, static_cast<double>(ReplayMutHeroStatEdit(g_replay, obj_addr, field, value)));
    return 1;
}

// SWFOC_ReplaySetUnitIsHero / ListHeroes / SetHeroRespawnTimer / SetPermadeath
// (Tasks 134, 135, 136).
static int Lua_ReplaySetUnitIsHero(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    int flag = static_cast<int>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetUnitIsHero(g_replay, obj_addr, flag != 0)));
    return 1;
}

static int Lua_ReplayIsUnitHero(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsIsUnitHero(g_replay, obj_addr)));
    return 1;
}

static int Lua_ReplayListHeroes(lua_State* L) {
    std::string out = ReplayObsListHeroes(g_replay);
    fn_pushstring(L, out.c_str());
    return 1;
}

static int Lua_ReplaySetHeroRespawnTimer(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    int ms = static_cast<int>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetHeroRespawnTimer(g_replay, obj_addr, ms)));
    return 1;
}

static int Lua_ReplayHeroRespawnTimer(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetHeroRespawnTimer(g_replay, obj_addr)));
    return 1;
}

static int Lua_ReplaySetPermadeath(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    int flag = static_cast<int>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetPermadeath(g_replay, obj_addr, flag != 0)));
    return 1;
}

static int Lua_ReplayIsPermadeath(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsIsPermadeath(g_replay, obj_addr)));
    return 1;
}

// SWFOC_ReplaySetUnitSpeed / SetUnitMaxSpeed / UnitSpeed / UnitMaxSpeed (Task 125).
static int Lua_ReplaySetUnitSpeed(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    float value = static_cast<float>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetUnitSpeed(g_replay, obj_addr, value)));
    return 1;
}

static int Lua_ReplaySetUnitMaxSpeed(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    float value = static_cast<float>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetUnitMaxSpeed(g_replay, obj_addr, value)));
    return 1;
}

static int Lua_ReplayUnitSpeed(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetUnitSpeed(g_replay, obj_addr)));
    return 1;
}

static int Lua_ReplayUnitMaxSpeed(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetUnitMaxSpeed(g_replay, obj_addr)));
    return 1;
}

// SWFOC_ReplaySetUnitShield(obj_addr, value) -> 1 on success. Task 130.
static int Lua_ReplaySetUnitShield(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    float value = static_cast<float>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetUnitShield(g_replay, obj_addr, value)));
    return 1;
}

// SWFOC_ReplaySetUnitMaxShield(obj_addr, value) -> 1 on success. Task 130.
static int Lua_ReplaySetUnitMaxShield(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    float value = static_cast<float>(fn_tonumber(L, 2));
    fn_pushnumber(L, static_cast<double>(ReplayMutSetUnitMaxShield(g_replay, obj_addr, value)));
    return 1;
}

// SWFOC_ReplayUnitShield(obj_addr) -> float, -1 if unknown. Task 130.
static int Lua_ReplayUnitShield(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetUnitShield(g_replay, obj_addr)));
    return 1;
}

// SWFOC_ReplayUnitMaxShield(obj_addr) -> float, -1 if unknown. Task 130.
static int Lua_ReplayUnitMaxShield(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetUnitMaxShield(g_replay, obj_addr)));
    return 1;
}

// SWFOC_ReplayKillUnit(obj_addr) -> 1 on success, 0 on no-op/unknown. Task 137.
static int Lua_ReplayKillUnit(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayMutKillUnit(g_replay, obj_addr)));
    return 1;
}

// SWFOC_ReplayReviveUnit(obj_addr) -> 1 on success, 0 on no-op/unknown. Task 137.
static int Lua_ReplayReviveUnit(lua_State* L) {
    uint64_t obj_addr = static_cast<uint64_t>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayMutReviveUnit(g_replay, obj_addr)));
    return 1;
}

// SWFOC_ReplaySetDamageMultiplier(slot, mult) -> 1 on success, 0 on bad input.
static int Lua_ReplaySetDamageMultiplier(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    double mult = fn_tonumber(L, 2);
    int rc = ReplayMutSetDamageMultiplier(
        g_replay, slot, static_cast<float>(mult));
    fn_pushnumber(L, rc);
    return 1;
}

// SWFOC_ReplayGetDamageMultiplier(slot) -> float (effective multiplier).
static int Lua_ReplayGetDamageMultiplier(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsGetDamageMultiplier(g_replay, slot)));
    return 1;
}

// SWFOC_ReplayHealAllLocal() -> count healed. Task 98 mutation seam.
static int Lua_ReplayHealAllLocal(lua_State* L) {
    (void)L;
    fn_pushnumber(L, static_cast<double>(ReplayMutHealAllLocal(g_replay)));
    return 1;
}

// SWFOC_ReplayEnumerateUnits(slot) -> CSV. Task 158 observer.
static int Lua_ReplayEnumerateUnits(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    std::string out = ReplayObsEnumerateUnitsForSlot(g_replay, slot);
    fn_pushstring(L, out.c_str());
    return 1;
}

// SWFOC_ReplayEventStreamDrain() -> CSV. Task 112 observer/drain.
static int Lua_ReplayEventStreamDrain(lua_State* L) {
    std::string out = ReplayObsEventStreamDrain(g_replay);
    fn_pushstring(L, out.c_str());
    return 1;
}

// SWFOC_ReplayEventLogCount() -> number of queued events.
static int Lua_ReplayEventLogCount(lua_State* L) {
    (void)L;
    fn_pushnumber(L, static_cast<double>(ReplayObsEventLogCount(g_replay)));
    return 1;
}

// SWFOC_ReplayGetAllPlayers() -> CSV roster. Task 111 observer.
static int Lua_ReplayGetAllPlayers(lua_State* L) {
    std::string out = ReplayObsListAllPlayers(g_replay);
    fn_pushstring(L, out.c_str());
    return 1;
}

// SWFOC_ReplayGameMode() -> 0/1/2. Task 108 observer, mirrors the live
// SWFOC_DumpState probe so replayed snapshots expose the same field.
static int Lua_ReplayGameMode(lua_State* L) {
    (void)L;
    fn_pushnumber(L, static_cast<double>(g_replay.game_mode));
    return 1;
}

// SWFOC_ReplayRevealAll(slot, enable) -> 1. Task 113 seam.
static int Lua_ReplayRevealAll(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    int enable = static_cast<int>(fn_tonumber(L, 2));
    int rc = ReplayMutRevealAll(g_replay, slot, enable != 0);
    fn_pushnumber(L, static_cast<double>(rc));
    return 1;
}

// SWFOC_ReplayIsRevealed(slot) -> 0/1. Task 113 observer.
static int Lua_ReplayIsRevealed(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    fn_pushnumber(L, static_cast<double>(ReplayObsIsRevealed(g_replay, slot)));
    return 1;
}

// SWFOC_ReplaySweepGodMode(enable) -> integer flipped count. Task 106 seam.
static int Lua_ReplaySweepGodMode(lua_State* L) {
    int enable = static_cast<int>(fn_tonumber(L, 1));
    int flipped = ReplayMutSweepGodMode(g_replay, enable != 0);
    fn_pushnumber(L, static_cast<double>(flipped));
    return 1;
}

// SWFOC_ReplayGodModeFullyActive() -> 1 if every local unit has INVULNERABLE
// attached to every hardpoint; 0 otherwise. Task 106 observer.
static int Lua_ReplayGodModeFullyActive(lua_State* L) {
    (void)L;
    fn_pushnumber(L, static_cast<double>(ReplayObsGodModeFullyActive(g_replay)));
    return 1;
}

// SWFOC_ReplayListTacticalUnits() -> CSV of per-unit rows (Task 104).
// Mirrors Lua_ListTacticalUnits in lua_bridge.cpp so the V2 editor can hit the
// same shape via the replay pipe. Pure-state observer lives in replay_state.h.
static int Lua_ReplayListTacticalUnits(lua_State* L) {
    std::string out = ReplayObsListTacticalUnits(g_replay);
    fn_pushstring(L, out.c_str());
    return 1;
}

// ======================================================================
// DoString wrapper (identical shape to lua_bridge.cpp)
// ======================================================================

struct StringReaderData { const char* str; size_t len; bool done; };

static const char* StringReader(lua_State* L, void* ud, size_t* sz) {
    (void)L;
    auto* rd = static_cast<StringReaderData*>(ud);
    if (rd->done) { *sz = 0; return nullptr; }
    rd->done = true;
    *sz = rd->len;
    return rd->str;
}

static int DoString(lua_State* L, const char* code, const char* chunkname) {
    if (!fn_load || !fn_pcall) return -1;
    StringReaderData rd{ code, strlen(code), false };
    int loadErr = fn_load(L, reinterpret_cast<lua_Chunkreader>(StringReader), &rd, chunkname);
    if (loadErr != 0) return loadErr;
    return fn_pcall(L, 0, 1, 0);
}

// SWFOC_DoString(code) -> success, errmsg (same ABI as the live bridge).
static int Lua_DoString_Fn(lua_State* L) {
    const char* code = fn_tostring(L, 1);
    if (!code) {
        fn_pushnumber(L, 0);
        fn_pushstring(L, "SWFOC_DoString: expected string argument");
        return 2;
    }
    int topBefore = fn_gettop(L);
    int err = DoString(L, code, "=SWFOC_DoString");
    if (err == 0) {
        fn_settop(L, topBefore);
        fn_pushnumber(L, 1);
        return 1;
    }
    const char* errMsg = fn_tostring(L, -1);
    fn_settop(L, topBefore);
    fn_pushnumber(L, 0);
    fn_pushstring(L, errMsg ? errMsg : "unknown error");
    return 2;
}

// ======================================================================
// Replay-aware fake_load shim
// ======================================================================
//
// The fake_load implementation in fake_lua.cpp does NOT actually compile the
// supplied Lua source -- it just pushes a "compiled chunk" placeholder and
// returns success. To make the pipe round-trip useful for editor tests we
// intercept a small catalog of commands here so the replay can answer a real
// client without needing a full Lua interpreter. Anything we don't recognize
// falls through to the plain fake stub.
//
// The flow is: PipeListenerThread -> DoString -> fn_load (intercepted) ->
// fn_pcall (plain stub). Our intercept pre-seeds the stack with a string that
// the pipe thread can read back out via fn_tostring(L, -1).

namespace {

// Trim leading/trailing whitespace and semicolons.
std::string Trim(const std::string& s) {
    size_t a = 0, b = s.size();
    while (a < b && (unsigned char)s[a] <= ' ') a++;
    while (b > a && ((unsigned char)s[b - 1] <= ' ' || s[b - 1] == ';')) b--;
    return s.substr(a, b - a);
}

// Try to match "return <expr>" and return the trimmed expression.
bool StripReturn(const std::string& s, std::string* out) {
    std::string t = Trim(s);
    if (t.size() >= 7 && t.compare(0, 7, "return ") == 0) {
        *out = Trim(t.substr(7));
        return true;
    }
    if (t.size() >= 7 && t.compare(0, 6, "return") == 0 && t[6] == '\t') {
        *out = Trim(t.substr(7));
        return true;
    }
    return false;
}

// Extract a single string argument from "foo(\"bar\")".
bool ExtractStringArg(const std::string& expr, std::string* out) {
    auto openParen = expr.find('(');
    auto closeParen = expr.rfind(')');
    if (openParen == std::string::npos || closeParen == std::string::npos || closeParen < openParen)
        return false;
    std::string inside = Trim(expr.substr(openParen + 1, closeParen - openParen - 1));
    if (inside.size() >= 2 && inside.front() == '"' && inside.back() == '"') {
        *out = inside.substr(1, inside.size() - 2);
        return true;
    }
    return false;
}

// Split a comma-separated argument list into raw token strings, respecting
// double-quoted spans so that `"a,b","c"` becomes [`"a,b"`, `"c"`]. The
// returned tokens still contain their surrounding quotes (if any) and have
// been Trim()ed of whitespace.
std::vector<std::string> SplitArgs(const std::string& inside) {
    std::vector<std::string> out;
    std::string cur;
    bool in_string = false;
    for (size_t i = 0; i < inside.size(); i++) {
        char c = inside[i];
        if (c == '"') {
            in_string = !in_string;
            cur.push_back(c);
        } else if (c == ',' && !in_string) {
            out.push_back(Trim(cur));
            cur.clear();
        } else {
            cur.push_back(c);
        }
    }
    if (!cur.empty() || !out.empty()) out.push_back(Trim(cur));
    return out;
}

// Pull the raw argument list out of "foo(arg1, arg2, ...)" and return the
// trimmed inside-the-parens substring. Returns false if the call shape is
// malformed or there are no parens.
bool ExtractArgs(const std::string& expr, std::vector<std::string>* out) {
    auto openParen = expr.find('(');
    auto closeParen = expr.rfind(')');
    if (openParen == std::string::npos || closeParen == std::string::npos || closeParen < openParen)
        return false;
    std::string inside = Trim(expr.substr(openParen + 1, closeParen - openParen - 1));
    *out = inside.empty() ? std::vector<std::string>{} : SplitArgs(inside);
    return true;
}

// Strip surrounding double quotes from a token (if present). Returns false
// when the token is not a simple double-quoted string literal.
bool UnquoteString(const std::string& token, std::string* out) {
    if (token.size() >= 2 && token.front() == '"' && token.back() == '"') {
        *out = token.substr(1, token.size() - 2);
        return true;
    }
    return false;
}

// Parse a numeric token (integer or float) into a double. Returns false on
// any non-numeric input.
bool ParseNumber(const std::string& token, double* out) {
    if (token.empty()) return false;
    char* end = nullptr;
    double v = strtod(token.c_str(), &end);
    if (end == token.c_str()) return false;
    while (end && *end != '\0' && (unsigned char)*end <= ' ') end++;
    if (end && *end != '\0') return false;
    *out = v;
    return true;
}

} // namespace

// Our replay intercept: pretends to compile the code by pushing a canned
// result and returning success. Any unknown code path delegates to the stock
// fake_load so the generic self-test / SWFOC_DoString suite still works.
static int ReplayLoad(lua_State* L, lua_Chunkreader reader, void* data, const char* chunkname) {
    FakeLuaState* fake = FS(L);

    // Drain the reader to reconstruct the full source string.
    std::string src;
    size_t sz = 0;
    const char* chunk = nullptr;
    while ((chunk = reader(L, data, &sz)) != nullptr && sz > 0) {
        src.append(chunk, sz);
    }

    // Push a callable placeholder so fake_pcall can execute it. We store the
    // pre-computed "return value" string on the TFUNCTION entry's strval so
    // we can retrieve it inside a custom pcall-like path below. Instead of
    // introducing a second stub we simply push the answer directly now:
    // fake_pcall will replace the function with nil results anyway, so we
    // take a simpler shortcut -- push a marker function, then after pcall the
    // pipe thread reads index -1 which will be nil. To avoid that, we instead
    // emulate the full execution by pushing the result here AND pre-populating
    // the call_log so the downstream pcall call becomes a no-op.

    // Simplest strategy: handle interesting commands directly by pushing a
    // string/number result onto the stack and returning a fake "function" the
    // pcall stub will pop and replace with a single nil. But the pipe thread
    // reads fn_tostring(L, -1) AFTER pcall, so a nil there is useless.
    //
    // Solution: we push a custom "function" whose type is TFUNCTION so
    // load() succeeds, then monkey-patch fake_pcall by pre-seeding the stack
    // so pcall's nresults-nil-pushes yield a string instead of nil. The easy
    // way is to have pcall return our value directly -- but we can't change
    // fake_pcall without modifying fake_lua.cpp (out of scope).
    //
    // Final approach: bypass pcall entirely. We push the result string as a
    // TSTRING (not TFUNCTION) and return a special sentinel error code that
    // DoString recognizes as success. Unfortunately DoString already treats
    // loadErr != 0 as failure.
    //
    // Simplest workable path: push a TFUNCTION whose subsequent pcall will
    // pop the function (nargs=0) and we manually push the result AFTER pcall.
    // But DrainReplayCommand (below) calls DoString then reads the top; if
    // pcall popped the function and pushed a nil result, the top is nil.
    //
    // So here is the actually-simple path we'll take: short-circuit at the
    // ReplayLoad level by running the command NOW, pushing the result now,
    // and returning a sentinel non-zero load error. Then DrainReplayCommand
    // special-cases the sentinel as success and uses the top-of-stack string.

    // Dispatch table:
    std::string expr;
    if (StripReturn(src, &expr)) {
        // return SWFOC_GetVersion()
        if (expr == "SWFOC_GetVersion()") {
            StackEntry e; e.type = LUA_TSTRING; e.strval = "SWFOC Lua Bridge v1.0 (replay)";
            fake->stack.push_back(e);
            return 9999;
        }
        if (expr == "SWFOC_GetCredits()") {
            const ReplayPlayer* p = GetLocalReplayPlayer();
            StackEntry e; e.type = LUA_TNUMBER; e.numval = p ? p->credits : 0.0;
            fake->stack.push_back(e);
            return 9999;
        }
        if (expr == "SWFOC_GetLocalPlayer()") {
            const ReplayPlayer* p = GetLocalReplayPlayer();
            char buf[160];
            if (p) {
                snprintf(buf, sizeof(buf), "slot=%u faction=%s",
                         (unsigned)p->slot,
                         p->faction_name.empty() ? "?" : p->faction_name.c_str());
            } else {
                snprintf(buf, sizeof(buf), "slot=-1 faction=none");
            }
            StackEntry e; e.type = LUA_TSTRING; e.strval = buf;
            fake->stack.push_back(e);
            return 9999;
        }
        if (expr == "SWFOC_ReplayPlayerCount()") {
            StackEntry e; e.type = LUA_TNUMBER; e.numval = (double)g_replay.players.size();
            fake->stack.push_back(e);
            return 9999;
        }
        {
            std::string arg;
            if (expr.rfind("SWFOC_ReplayObjectCount(", 0) == 0 && ExtractStringArg(expr, &arg)) {
                auto it = g_replay.objects.find(arg);
                StackEntry e; e.type = LUA_TNUMBER;
                e.numval = it != g_replay.objects.end() ? (double)it->second : 0.0;
                fake->stack.push_back(e);
                return 9999;
            }
            if (expr.rfind("SWFOC_ReplayMetadata(", 0) == 0 && ExtractStringArg(expr, &arg)) {
                auto it = g_replay.metadata.find(arg);
                StackEntry e; e.type = LUA_TSTRING;
                e.strval = it != g_replay.metadata.end() ? it->second : "";
                fake->stack.push_back(e);
                return 9999;
            }
        }
        // ----------------------------------------------------------------
        // New v5 service observer + mutation dispatchers (added 2026-04-08)
        // ----------------------------------------------------------------
        if (expr == "SWFOC_ReplayHumanPlayerSlot()") {
            StackEntry e; e.type = LUA_TNUMBER;
            e.numval = static_cast<double>(g_replay.local_slot);
            fake->stack.push_back(e);
            return 9999;
        }
        if (expr == "SWFOC_ReplayLastStoryEvent()") {
            StackEntry e; e.type = LUA_TSTRING;
            e.strval = g_replay.last_story_event;
            fake->stack.push_back(e);
            return 9999;
        }
        // SWFOC_ReplayPlayerCredits("FACTION") -> number
        {
            std::string arg;
            if (expr.rfind("SWFOC_ReplayPlayerCredits(", 0) == 0 && ExtractStringArg(expr, &arg)) {
                std::string needle = ToUpperAscii(arg);
                StackEntry e; e.type = LUA_TNUMBER; e.numval = -1.0;
                for (const auto& p : g_replay.players) {
                    if (ToUpperAscii(p.faction_name) == needle) {
                        e.numval = p.credits;
                        break;
                    }
                }
                fake->stack.push_back(e);
                return 9999;
            }
            if (expr.rfind("SWFOC_ReplayPlayerTechLevel(", 0) == 0 && ExtractStringArg(expr, &arg)) {
                std::string needle = ToUpperAscii(arg);
                StackEntry e; e.type = LUA_TNUMBER; e.numval = -1.0;
                for (const auto& p : g_replay.players) {
                    if (ToUpperAscii(p.faction_name) == needle) {
                        e.numval = static_cast<double>(p.tech_level);
                        break;
                    }
                }
                fake->stack.push_back(e);
                return 9999;
            }
            if (expr.rfind("SWFOC_ReplayPlanetCorruption(", 0) == 0 && ExtractStringArg(expr, &arg)) {
                StackEntry e; e.type = LUA_TNUMBER; e.numval = -1.0;
                auto it = g_replay.planets.find(ToUpperAscii(arg));
                if (it != g_replay.planets.end()) {
                    e.numval = static_cast<double>(it->second.corruption);
                }
                fake->stack.push_back(e);
                return 9999;
            }
            if (expr.rfind("SWFOC_ReplayPushStoryEvent(", 0) == 0 && ExtractStringArg(expr, &arg)) {
                g_replay.last_story_event = arg;
                StackEntry e; e.type = LUA_TNUMBER; e.numval = 1.0;
                fake->stack.push_back(e);
                return 9999;
            }
        }
        // Multi-arg observers/mutators -- use ExtractArgs.
        {
            std::vector<std::string> args;
            if (expr.rfind("SWFOC_ReplayDiplomaticState(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 2) {
                std::string a, b;
                if (UnquoteString(args[0], &a) && UnquoteString(args[1], &b)) {
                    auto key = MakeDiplomacyKey(a, b);
                    auto it = g_replay.diplomacy.find(key);
                    StackEntry e; e.type = LUA_TSTRING;
                    e.strval = (it == g_replay.diplomacy.end()) ? "hostile" : it->second;
                    fake->stack.push_back(e);
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetDiplomacy(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 3) {
                std::string a, b, st;
                if (UnquoteString(args[0], &a) && UnquoteString(args[1], &b) && UnquoteString(args[2], &st)) {
                    g_replay.diplomacy[MakeDiplomacyKey(a, b)] = st;
                    StackEntry e; e.type = LUA_TNUMBER; e.numval = 1.0;
                    fake->stack.push_back(e);
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetPlanetCorruption(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 2) {
                std::string planet;
                double value = 0.0;
                if (UnquoteString(args[0], &planet) && ParseNumber(args[1], &value)) {
                    std::string key = ToUpperAscii(planet);
                    auto& info = g_replay.planets[key];
                    if (info.name.empty()) info.name = planet;
                    info.corruption = static_cast<float>(value);
                    StackEntry e; e.type = LUA_TNUMBER; e.numval = 1.0;
                    fake->stack.push_back(e);
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayUnitOwner(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 2) {
                std::string type_name;
                double idx_d = 0.0;
                if (UnquoteString(args[0], &type_name) && ParseNumber(args[1], &idx_d)) {
                    int index = static_cast<int>(idx_d);
                    StackEntry e; e.type = LUA_TNUMBER; e.numval = -1.0;
                    auto it = g_replay.object_owners.find(ToUpperAscii(type_name));
                    if (index >= 0 && it != g_replay.object_owners.end() &&
                        index < static_cast<int>(it->second.size())) {
                        e.numval = static_cast<double>(it->second[index]);
                    }
                    fake->stack.push_back(e);
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySpawnUnit(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 3) {
                std::string faction, type_name;
                double count_d = 0.0;
                if (UnquoteString(args[0], &faction) && UnquoteString(args[1], &type_name) && ParseNumber(args[2], &count_d)) {
                    int count = static_cast<int>(count_d);
                    if (count > 0) {
                        int32_t owner_slot = -1;
                        std::string needle = ToUpperAscii(faction);
                        for (const auto& p : g_replay.players) {
                            if (ToUpperAscii(p.faction_name) == needle) {
                                owner_slot = static_cast<int32_t>(p.slot);
                                break;
                            }
                        }
                        std::string type_key = ToUpperAscii(type_name);
                        auto& owners = g_replay.object_owners[type_key];
                        for (int i = 0; i < count; i++) owners.push_back(owner_slot);
                        g_replay.objects[type_name] += static_cast<uint32_t>(count);
                        StackEntry e; e.type = LUA_TNUMBER; e.numval = 1.0;
                        fake->stack.push_back(e);
                        return 9999;
                    }
                    StackEntry e; e.type = LUA_TNUMBER; e.numval = 0.0;
                    fake->stack.push_back(e);
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayCooldownState(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 2) {
                std::string unit_type;
                double idx_d = 0.0;
                if (UnquoteString(args[0], &unit_type) && ParseNumber(args[1], &idx_d)) {
                    int idx = static_cast<int>(idx_d);
                    StackEntry e; e.type = LUA_TNUMBER; e.numval = -1.0;
                    auto it = g_replay.cooldowns.find(unit_type);
                    if (idx >= 0 && it != g_replay.cooldowns.end() &&
                        idx < static_cast<int>(it->second.size())) {
                        e.numval = static_cast<double>(it->second[idx]);
                    }
                    fake->stack.push_back(e);
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetCooldown(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 3) {
                std::string unit_type;
                double idx_d = 0.0;
                double value = 0.0;
                if (UnquoteString(args[0], &unit_type) && ParseNumber(args[1], &idx_d) && ParseNumber(args[2], &value)) {
                    int idx = static_cast<int>(idx_d);
                    if (idx >= 0 && idx <= 256) {
                        auto& slots = g_replay.cooldowns[unit_type];
                        if (static_cast<int>(slots.size()) <= idx) {
                            slots.resize(static_cast<size_t>(idx) + 1, 0.0f);
                        }
                        slots[static_cast<size_t>(idx)] = static_cast<float>(value);
                        StackEntry e; e.type = LUA_TNUMBER; e.numval = 1.0;
                        fake->stack.push_back(e);
                        return 9999;
                    }
                    StackEntry e; e.type = LUA_TNUMBER; e.numval = 0.0;
                    fake->stack.push_back(e);
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayTaskForceCount(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 1) {
                double slot_d = 0.0;
                if (ParseNumber(args[0], &slot_d)) {
                    int slot = static_cast<int>(slot_d);
                    int count = 0;
                    for (const auto& tf : g_replay.task_forces) {
                        if (tf.owner_slot == slot) count++;
                    }
                    StackEntry e; e.type = LUA_TNUMBER; e.numval = static_cast<double>(count);
                    fake->stack.push_back(e);
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayAddTaskForce(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 2) {
                double slot_d = 0.0;
                std::string name;
                if (ParseNumber(args[0], &slot_d) && UnquoteString(args[1], &name)) {
                    ReplayTaskForceRecord rec;
                    rec.owner_slot = static_cast<int32_t>(slot_d);
                    rec.name = name;
                    g_replay.task_forces.push_back(std::move(rec));
                    StackEntry e; e.type = LUA_TNUMBER; e.numval = 1.0;
                    fake->stack.push_back(e);
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySwitchLocalPlayer(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 1) {
                double slot_d = 0.0;
                if (ParseNumber(args[0], &slot_d)) {
                    int slot = static_cast<int>(slot_d);
                    StackEntry e; e.type = LUA_TNUMBER;
                    if (slot == -1) {
                        g_replay.local_slot = -1;
                        e.numval = 1.0;
                    } else {
                        bool found = false;
                        for (const auto& p : g_replay.players) {
                            if (static_cast<int>(p.slot) == slot) { found = true; break; }
                        }
                        if (found) {
                            g_replay.local_slot = slot;
                            e.numval = 1.0;
                        } else {
                            e.numval = 0.0;
                        }
                    }
                    fake->stack.push_back(e);
                    return 9999;
                }
            }
        }
        // ----------------------------------------------------------------
        // Unit / hardpoint / behavior dispatchers (Task 101 — 2026-04-23).
        //
        // Each helper is also registered as a real lua_CFunction, but the
        // replay pattern-matcher runs first, so we pre-answer common call
        // shapes for Tasks 99/100 autonomous iteration. Delegation goes
        // straight to the pure-state ReplayMut*/ReplayObs* functions.
        // ----------------------------------------------------------------
        auto push_num = [&](double v) {
            StackEntry e; e.type = LUA_TNUMBER; e.numval = v;
            fake->stack.push_back(e);
        };
        // Zero-arg observers / mutations.
        if (expr == "SWFOC_ReplayUnitCount()") {
            push_num(static_cast<double>(g_replay.units.size()));
            return 9999;
        }
        if (expr == "SWFOC_ReplayClearSelected()") {
            ReplayMutClearSelected(g_replay);
            push_num(1.0);
            return 9999;
        }
        if (expr == "SWFOC_ReplayGetSelectedUnit()") {
            push_num(static_cast<double>(ReplayObsGetSelectedUnit(g_replay)));
            return 9999;
        }
        if (expr == "SWFOC_ReplaySelectedCount()") {
            push_num(static_cast<double>(ReplayObsSelectedCount(g_replay)));
            return 9999;
        }
        if (expr == "SWFOC_ReplayListTacticalUnits()") {
            StackEntry e; e.type = LUA_TSTRING;
            e.strval = ReplayObsListTacticalUnits(g_replay);
            fake->stack.push_back(e);
            return 9999;
        }
        if (expr == "SWFOC_ReplayGodModeFullyActive()") {
            push_num(static_cast<double>(ReplayObsGodModeFullyActive(g_replay)));
            return 9999;
        }
        if (expr == "SWFOC_ReplayGameMode()") {
            push_num(static_cast<double>(g_replay.game_mode));
            return 9999;
        }
        if (expr == "SWFOC_ReplayGetAllPlayers()") {
            StackEntry e; e.type = LUA_TSTRING;
            e.strval = ReplayObsListAllPlayers(g_replay);
            fake->stack.push_back(e);
            return 9999;
        }
        if (expr == "SWFOC_ReplayEventStreamDrain()") {
            StackEntry e; e.type = LUA_TSTRING;
            e.strval = ReplayObsEventStreamDrain(g_replay);
            fake->stack.push_back(e);
            return 9999;
        }
        if (expr == "SWFOC_ReplayEventLogCount()") {
            push_num(static_cast<double>(ReplayObsEventLogCount(g_replay)));
            return 9999;
        }
        if (expr == "SWFOC_ReplayHealAllLocal()") {
            push_num(static_cast<double>(ReplayMutHealAllLocal(g_replay)));
            return 9999;
        }
        {
            std::vector<std::string> args;
            if (expr.rfind("SWFOC_ReplayEnumerateUnits(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double slot_d = 0.0;
                if (ParseNumber(args[0], &slot_d)) {
                    StackEntry e; e.type = LUA_TSTRING;
                    e.strval = ReplayObsEnumerateUnitsForSlot(
                        g_replay, static_cast<int32_t>(slot_d));
                    fake->stack.push_back(e);
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayHeroStatEdit(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 3) {
                double addr_d = 0.0, val_d = 0.0;
                std::string field;
                if (ParseNumber(args[0], &addr_d)
                    && UnquoteString(args[1], &field)
                    && ParseNumber(args[2], &val_d)) {
                    push_num(static_cast<double>(ReplayMutHeroStatEdit(
                        g_replay, static_cast<uint64_t>(addr_d), field, static_cast<float>(val_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetBuildSpeed(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double s_d = 0.0, m_d = 0.0;
                if (ParseNumber(args[0], &s_d) && ParseNumber(args[1], &m_d)) {
                    push_num(static_cast<double>(ReplayMutSetBuildSpeed(
                        g_replay, static_cast<int32_t>(s_d), static_cast<float>(m_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayGetBuildSpeed(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double s_d = 0.0;
                if (ParseNumber(args[0], &s_d)) {
                    push_num(static_cast<double>(ReplayObsGetBuildSpeed(
                        g_replay, static_cast<int32_t>(s_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetFactionSpeedMult(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double s_d = 0.0, m_d = 0.0;
                if (ParseNumber(args[0], &s_d) && ParseNumber(args[1], &m_d)) {
                    push_num(static_cast<double>(ReplayMutSetFactionSpeedMult(
                        g_replay, static_cast<int32_t>(s_d), static_cast<float>(m_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayGetFactionSpeedMult(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double s_d = 0.0;
                if (ParseNumber(args[0], &s_d)) {
                    push_num(static_cast<double>(ReplayObsGetFactionSpeedMult(
                        g_replay, static_cast<int32_t>(s_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetFireRate(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double s_d = 0.0, m_d = 0.0;
                if (ParseNumber(args[0], &s_d) && ParseNumber(args[1], &m_d)) {
                    push_num(static_cast<double>(ReplayMutSetFireRate(
                        g_replay, static_cast<int32_t>(s_d), static_cast<float>(m_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayGetFireRate(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double s_d = 0.0;
                if (ParseNumber(args[0], &s_d)) {
                    push_num(static_cast<double>(ReplayObsGetFireRate(
                        g_replay, static_cast<int32_t>(s_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayApplyFireRate(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double s_d = 0.0, base_d = 0.0;
                if (ParseNumber(args[0], &s_d) && ParseNumber(args[1], &base_d)) {
                    push_num(static_cast<double>(ReplayMutApplyFireRate(
                        g_replay, static_cast<int32_t>(s_d), static_cast<float>(base_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetAreaDamage(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double e_d = 0.0;
                if (ParseNumber(args[0], &e_d)) {
                    push_num(static_cast<double>(ReplayMutSetAreaDamageEnabled(
                        g_replay, e_d != 0.0)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayIsAreaDamage(", 0) == 0) {
                push_num(ReplayObsIsAreaDamageEnabled(g_replay) ? 1.0 : 0.0);
                return 9999;
            }
            if (expr.rfind("SWFOC_ReplayApplyAreaSplash(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, amt_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &amt_d)) {
                    push_num(static_cast<double>(ReplayMutApplyAreaSplash(
                        g_replay, static_cast<uint64_t>(addr_d), static_cast<float>(amt_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetTargetFilter(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double s_d = 0.0, b_d = 0.0;
                if (ParseNumber(args[0], &s_d) && ParseNumber(args[1], &b_d)) {
                    push_num(static_cast<double>(ReplayMutSetTargetFilter(
                        g_replay, static_cast<int32_t>(s_d), static_cast<uint32_t>(b_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayGetTargetFilter(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double s_d = 0.0;
                if (ParseNumber(args[0], &s_d)) {
                    push_num(static_cast<double>(ReplayObsGetTargetFilter(
                        g_replay, static_cast<int32_t>(s_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayIsTargetAllowed(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double s_d = 0.0, k_d = 0.0;
                if (ParseNumber(args[0], &s_d) && ParseNumber(args[1], &k_d)) {
                    push_num(ReplayObsIsTargetAllowed(
                        g_replay, static_cast<int32_t>(s_d), static_cast<uint32_t>(k_d)) ? 1.0 : 0.0);
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetOHK(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double e_d = 0.0;
                if (ParseNumber(args[0], &e_d)) {
                    push_num(static_cast<double>(ReplayMutSetOHK(g_replay, e_d != 0.0)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayIsOHK(", 0) == 0) {
                push_num(ReplayObsIsOHK(g_replay) ? 1.0 : 0.0);
                return 9999;
            }
            if (expr.rfind("SWFOC_ReplayGetAttackPower(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double a_d = 0.0;
                if (ParseNumber(args[0], &a_d)) {
                    push_num(static_cast<double>(ReplayObsGetAttackPower(
                        g_replay, static_cast<uint64_t>(a_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetIncomeMultiplier(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double slot_d = 0.0, mult_d = 0.0;
                if (ParseNumber(args[0], &slot_d) && ParseNumber(args[1], &mult_d)) {
                    push_num(static_cast<double>(ReplayMutSetIncomeMultiplier(
                        g_replay, static_cast<int32_t>(slot_d), static_cast<float>(mult_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayGetIncomeMultiplier(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double slot_d = 0.0;
                if (ParseNumber(args[0], &slot_d)) {
                    push_num(static_cast<double>(ReplayObsGetIncomeMultiplier(
                        g_replay, static_cast<int32_t>(slot_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetGameSpeed(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double speed_d = 0.0;
                if (ParseNumber(args[0], &speed_d)) {
                    push_num(static_cast<double>(ReplayMutSetGameSpeed(
                        g_replay, static_cast<float>(speed_d))));
                    return 9999;
                }
            }
            if (expr == "SWFOC_ReplayGetGameSpeed()") {
                push_num(static_cast<double>(ReplayObsGetGameSpeed(g_replay)));
                return 9999;
            }
            if (expr.rfind("SWFOC_ReplaySetFreezeCredits(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 3) {
                double slot_d = 0.0, enable_d = 0.0, target_d = 0.0;
                if (ParseNumber(args[0], &slot_d) && ParseNumber(args[1], &enable_d)
                    && ParseNumber(args[2], &target_d)) {
                    push_num(static_cast<double>(ReplayMutSetFreezeCredits(
                        g_replay, static_cast<int32_t>(slot_d), enable_d != 0.0, target_d)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayIsFreezeCredits(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double slot_d = 0.0;
                if (ParseNumber(args[0], &slot_d)) {
                    push_num(static_cast<double>(ReplayObsIsFreezeCredits(
                        g_replay, static_cast<int32_t>(slot_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayFreezeCreditsTarget(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double slot_d = 0.0;
                if (ParseNumber(args[0], &slot_d)) {
                    push_num(ReplayObsGetFreezeCreditsTarget(
                        g_replay, static_cast<int32_t>(slot_d)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayTickIncome(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double base_d = 0.0;
                if (ParseNumber(args[0], &base_d)) {
                    push_num(static_cast<double>(ReplayMutTickIncome(g_replay, base_d)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayListAbilities(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double addr_d = 0.0;
                if (ParseNumber(args[0], &addr_d)) {
                    StackEntry e; e.type = LUA_TSTRING;
                    e.strval = ReplayObsListAbilities(
                        g_replay, static_cast<uint64_t>(addr_d));
                    fake->stack.push_back(e);
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayAbilityCooldown(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, idx_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &idx_d)) {
                    push_num(static_cast<double>(ReplayObsAbilityCooldown(
                        g_replay, static_cast<uint64_t>(addr_d),
                        static_cast<int32_t>(idx_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayAddUnitAbility(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 5) {
                double addr_d = 0.0, idx_d = 0.0, cd_d = 0.0, use_d = 0.0;
                std::string name;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &idx_d)
                    && UnquoteString(args[2], &name)
                    && ParseNumber(args[3], &cd_d) && ParseNumber(args[4], &use_d)) {
                    push_num(static_cast<double>(ReplayMutAddUnitAbility(
                        g_replay, static_cast<uint64_t>(addr_d),
                        static_cast<int32_t>(idx_d), name,
                        static_cast<int32_t>(cd_d), use_d != 0.0)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayTriggerAbility(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 3) {
                double addr_d = 0.0, idx_d = 0.0, cd_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &idx_d)
                    && ParseNumber(args[2], &cd_d)) {
                    push_num(static_cast<double>(ReplayMutTriggerAbility(
                        g_replay, static_cast<uint64_t>(addr_d),
                        static_cast<int32_t>(idx_d), static_cast<int32_t>(cd_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayTickAbilityCooldown(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, delta_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &delta_d)) {
                    push_num(static_cast<double>(ReplayMutTickAbilityCooldown(
                        g_replay, static_cast<uint64_t>(addr_d),
                        static_cast<int32_t>(delta_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetPlanetTech(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                std::string name; double tech_d = 0.0;
                if (UnquoteString(args[0], &name) && ParseNumber(args[1], &tech_d)) {
                    push_num(static_cast<double>(ReplayMutSetPlanetTech(
                        g_replay, name, static_cast<int32_t>(tech_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetPlanetBuildings(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                std::string name; double cnt_d = 0.0;
                if (UnquoteString(args[0], &name) && ParseNumber(args[1], &cnt_d)) {
                    push_num(static_cast<double>(ReplayMutSetPlanetBuildings(
                        g_replay, name, static_cast<int32_t>(cnt_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetPlanetCapital(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                std::string name; double flag_d = 0.0;
                if (UnquoteString(args[0], &name) && ParseNumber(args[1], &flag_d)) {
                    push_num(static_cast<double>(ReplayMutSetPlanetCapital(
                        g_replay, name, flag_d != 0.0)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayPlanetTech(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                std::string name;
                if (UnquoteString(args[0], &name)) {
                    push_num(static_cast<double>(ReplayObsGetPlanetTech(g_replay, name)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayPlanetBuildings(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                std::string name;
                if (UnquoteString(args[0], &name)) {
                    push_num(static_cast<double>(ReplayObsGetPlanetBuildings(g_replay, name)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayPlanetTechAndBuildings(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                std::string name;
                if (UnquoteString(args[0], &name)) {
                    StackEntry e; e.type = LUA_TSTRING;
                    e.strval = ReplayObsGetPlanetTechAndBuildings(g_replay, name);
                    fake->stack.push_back(e);
                    return 9999;
                }
            }
            if (expr == "SWFOC_ReplayListPlanets()") {
                StackEntry e; e.type = LUA_TSTRING;
                e.strval = ReplayObsListPlanets(g_replay);
                fake->stack.push_back(e);
                return 9999;
            }
            if (expr.rfind("SWFOC_ReplayChangePlanetOwner(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                std::string name;
                double slot_d = 0.0;
                if (UnquoteString(args[0], &name) && ParseNumber(args[1], &slot_d)) {
                    push_num(static_cast<double>(ReplayMutChangePlanetOwner(
                        g_replay, name, static_cast<int32_t>(slot_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayPlanetOwner(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                std::string name;
                if (UnquoteString(args[0], &name)) {
                    push_num(static_cast<double>(ReplayObsGetPlanetOwner(g_replay, name)));
                    return 9999;
                }
            }
            if (expr == "SWFOC_ReplayListHeroes()") {
                StackEntry e; e.type = LUA_TSTRING;
                e.strval = ReplayObsListHeroes(g_replay);
                fake->stack.push_back(e);
                return 9999;
            }
            if (expr.rfind("SWFOC_ReplaySetUnitIsHero(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, flag_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &flag_d)) {
                    push_num(static_cast<double>(ReplayMutSetUnitIsHero(
                        g_replay, static_cast<uint64_t>(addr_d), flag_d != 0.0)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayIsUnitHero(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double addr_d = 0.0;
                if (ParseNumber(args[0], &addr_d)) {
                    push_num(static_cast<double>(ReplayObsIsUnitHero(
                        g_replay, static_cast<uint64_t>(addr_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetHeroRespawnTimer(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, ms_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &ms_d)) {
                    push_num(static_cast<double>(ReplayMutSetHeroRespawnTimer(
                        g_replay, static_cast<uint64_t>(addr_d), static_cast<int32_t>(ms_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayHeroRespawnTimer(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double addr_d = 0.0;
                if (ParseNumber(args[0], &addr_d)) {
                    push_num(static_cast<double>(ReplayObsGetHeroRespawnTimer(
                        g_replay, static_cast<uint64_t>(addr_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetPermadeath(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, flag_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &flag_d)) {
                    push_num(static_cast<double>(ReplayMutSetPermadeath(
                        g_replay, static_cast<uint64_t>(addr_d), flag_d != 0.0)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayIsPermadeath(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double addr_d = 0.0;
                if (ParseNumber(args[0], &addr_d)) {
                    push_num(static_cast<double>(ReplayObsIsPermadeath(
                        g_replay, static_cast<uint64_t>(addr_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetUnitSpeed(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, val_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &val_d)) {
                    push_num(static_cast<double>(ReplayMutSetUnitSpeed(
                        g_replay, static_cast<uint64_t>(addr_d), static_cast<float>(val_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetUnitMaxSpeed(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, val_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &val_d)) {
                    push_num(static_cast<double>(ReplayMutSetUnitMaxSpeed(
                        g_replay, static_cast<uint64_t>(addr_d), static_cast<float>(val_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayUnitSpeed(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double addr_d = 0.0;
                if (ParseNumber(args[0], &addr_d)) {
                    push_num(static_cast<double>(ReplayObsGetUnitSpeed(
                        g_replay, static_cast<uint64_t>(addr_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayUnitMaxSpeed(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double addr_d = 0.0;
                if (ParseNumber(args[0], &addr_d)) {
                    push_num(static_cast<double>(ReplayObsGetUnitMaxSpeed(
                        g_replay, static_cast<uint64_t>(addr_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetUnitShield(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, val_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &val_d)) {
                    push_num(static_cast<double>(ReplayMutSetUnitShield(
                        g_replay, static_cast<uint64_t>(addr_d), static_cast<float>(val_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetUnitMaxShield(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, val_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &val_d)) {
                    push_num(static_cast<double>(ReplayMutSetUnitMaxShield(
                        g_replay, static_cast<uint64_t>(addr_d), static_cast<float>(val_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayUnitShield(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double addr_d = 0.0;
                if (ParseNumber(args[0], &addr_d)) {
                    push_num(static_cast<double>(ReplayObsGetUnitShield(
                        g_replay, static_cast<uint64_t>(addr_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayUnitMaxShield(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double addr_d = 0.0;
                if (ParseNumber(args[0], &addr_d)) {
                    push_num(static_cast<double>(ReplayObsGetUnitMaxShield(
                        g_replay, static_cast<uint64_t>(addr_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayKillUnit(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double addr_d = 0.0;
                if (ParseNumber(args[0], &addr_d)) {
                    push_num(static_cast<double>(ReplayMutKillUnit(
                        g_replay, static_cast<uint64_t>(addr_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayReviveUnit(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double addr_d = 0.0;
                if (ParseNumber(args[0], &addr_d)) {
                    push_num(static_cast<double>(ReplayMutReviveUnit(
                        g_replay, static_cast<uint64_t>(addr_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetDamageMultiplier(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double slot_d = 0.0, mult_d = 0.0;
                if (ParseNumber(args[0], &slot_d) && ParseNumber(args[1], &mult_d)) {
                    push_num(static_cast<double>(ReplayMutSetDamageMultiplier(
                        g_replay, static_cast<int32_t>(slot_d),
                        static_cast<float>(mult_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayGetDamageMultiplier(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double slot_d = 0.0;
                if (ParseNumber(args[0], &slot_d)) {
                    push_num(static_cast<double>(ReplayObsGetDamageMultiplier(
                        g_replay, static_cast<int32_t>(slot_d))));
                    return 9999;
                }
            }
        }
        {
            std::vector<std::string> args;
            if (expr.rfind("SWFOC_ReplayRevealAll(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 2) {
                double slot_d = 0.0, flag_d = 0.0;
                if (ParseNumber(args[0], &slot_d) && ParseNumber(args[1], &flag_d)) {
                    push_num(static_cast<double>(ReplayMutRevealAll(
                        g_replay, static_cast<int32_t>(slot_d), flag_d != 0.0)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayIsRevealed(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double slot_d = 0.0;
                if (ParseNumber(args[0], &slot_d)) {
                    push_num(static_cast<double>(ReplayObsIsRevealed(
                        g_replay, static_cast<int32_t>(slot_d))));
                    return 9999;
                }
            }
        }
        {
            std::vector<std::string> args;
            if (expr.rfind("SWFOC_ReplaySweepGodMode(", 0) == 0
                && ExtractArgs(expr, &args) && args.size() == 1) {
                double flag_d = 0.0;
                if (ParseNumber(args[0], &flag_d)) {
                    push_num(static_cast<double>(
                        ReplayMutSweepGodMode(g_replay, flag_d != 0.0)));
                    return 9999;
                }
            }
        }
        // One-arg (obj_addr) observers / mutators.
        {
            struct OneArgObserver {
                const char*                             prefix;
                std::function<double(uint64_t)>          fn;
            };
            OneArgObserver one_arg[] = {
                {"SWFOC_ReplayUnitHull(",         [](uint64_t a) { auto* u = ReplayFindUnit(g_replay, a); return u ? static_cast<double>(u->hull)        : -1.0; }},
                {"SWFOC_ReplayUnitMaxHull(",      [](uint64_t a) { auto* u = ReplayFindUnit(g_replay, a); return u ? static_cast<double>(u->max_hull)    : -1.0; }},
                {"SWFOC_ReplayUnitOwnerSlot(",    [](uint64_t a) { auto* u = ReplayFindUnit(g_replay, a); return u ? static_cast<double>(u->owner_slot)  : -1.0; }},
                {"SWFOC_ReplayUnitInvulnFlag(",   [](uint64_t a) { auto* u = ReplayFindUnit(g_replay, a); return u ? static_cast<double>(u->invuln_flag) : -1.0; }},
                {"SWFOC_ReplayUnitPreventDeath(", [](uint64_t a) { auto* u = ReplayFindUnit(g_replay, a); if (!u) return -1.0; return (u->prevent_death & 0x80) ? 1.0 : 0.0; }},
                {"SWFOC_ReplayHardpointCount(",   [](uint64_t a) { auto* u = ReplayFindUnit(g_replay, a); return u ? static_cast<double>(u->hardpoints.size()) : -1.0; }},
                {"SWFOC_ReplayUnitIsInvulnerable(", [](uint64_t a) { auto* u = ReplayFindUnit(g_replay, a); if (!u) return -1.0; return ReplayUnitAnyHardpointHasBehavior(*u, "INVULNERABLE") ? 1.0 : 0.0; }},
                {"SWFOC_ReplaySetSelected(",      [](uint64_t a) { return static_cast<double>(ReplayMutSetSelected(g_replay, a)); }},
                {"SWFOC_ReplayAppendSelected(",   [](uint64_t a) { return static_cast<double>(ReplayMutAppendSelected(g_replay, a)); }},
            };
            for (const auto& oa : one_arg) {
                if (expr.rfind(oa.prefix, 0) == 0) {
                    std::vector<std::string> args;
                    if (ExtractArgs(expr, &args) && args.size() == 1) {
                        double n = 0.0;
                        if (ParseNumber(args[0], &n)) {
                            push_num(oa.fn(static_cast<uint64_t>(n)));
                            return 9999;
                        }
                    }
                }
            }
        }
        // Two-arg (obj_addr, value) mutators and damage tick.
        {
            std::vector<std::string> args;
            if (expr.rfind("SWFOC_ReplayMakeInvulnerable(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, flag_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &flag_d)) {
                    push_num(static_cast<double>(ReplayMutMakeInvulnerable(g_replay, static_cast<uint64_t>(addr_d), flag_d != 0.0)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetUnitInvulnFlag(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, flag_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &flag_d)) {
                    push_num(static_cast<double>(ReplayMutSetUnitInvulnFlag(g_replay, static_cast<uint64_t>(addr_d), flag_d != 0.0 ? 1 : 0)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetPreventDeathBit(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, flag_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &flag_d)) {
                    push_num(static_cast<double>(ReplayMutSetPreventDeathBit(g_replay, static_cast<uint64_t>(addr_d), flag_d != 0.0)));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplaySetUnitHull(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, val_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &val_d)) {
                    push_num(static_cast<double>(ReplayMutSetUnitHull(g_replay, static_cast<uint64_t>(addr_d), static_cast<float>(val_d))));
                    return 9999;
                }
            }
            if (expr.rfind("SWFOC_ReplayApplyDamage(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 2) {
                double addr_d = 0.0, dmg_d = 0.0;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &dmg_d)) {
                    float new_hull = ReplayMutApplyDamage(g_replay, static_cast<uint64_t>(addr_d), static_cast<float>(dmg_d));
                    push_num(static_cast<double>(new_hull));
                    return 9999;
                }
            }
        }
        // Three-arg (obj_addr, hp_index, behavior) primitives.
        {
            std::vector<std::string> args;
            auto dispatch_three = [&](const char* prefix, std::function<int(uint64_t, int, const std::string&)> fn) -> bool {
                if (expr.rfind(prefix, 0) != 0) return false;
                if (!ExtractArgs(expr, &args) || args.size() != 3) return false;
                double addr_d = 0.0, hp_d = 0.0;
                std::string behavior;
                if (!ParseNumber(args[0], &addr_d)) return false;
                if (!ParseNumber(args[1], &hp_d)) return false;
                if (!UnquoteString(args[2], &behavior)) return false;
                push_num(static_cast<double>(fn(static_cast<uint64_t>(addr_d), static_cast<int>(hp_d), behavior)));
                return true;
            };
            if (dispatch_three("SWFOC_ReplayAttachBehavior(",
                               [](uint64_t a, int hp, const std::string& b) { return ReplayMutAttachBehavior(g_replay, a, hp, b); })) return 9999;
            if (dispatch_three("SWFOC_ReplayDetachBehavior(",
                               [](uint64_t a, int hp, const std::string& b) { return ReplayMutDetachBehavior(g_replay, a, hp, b); })) return 9999;
            if (expr.rfind("SWFOC_ReplayHardpointHasBehavior(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 3) {
                double addr_d = 0.0, hp_d = 0.0;
                std::string behavior;
                if (ParseNumber(args[0], &addr_d) && ParseNumber(args[1], &hp_d) && UnquoteString(args[2], &behavior)) {
                    auto* u = ReplayFindUnit(g_replay, static_cast<uint64_t>(addr_d));
                    int hp_index = static_cast<int>(hp_d);
                    if (!u || hp_index < 0 || hp_index >= static_cast<int>(u->hardpoints.size())) {
                        push_num(-1.0);
                    } else {
                        push_num(ReplayHardpointHasBehavior(u->hardpoints[static_cast<size_t>(hp_index)], behavior) ? 1.0 : 0.0);
                    }
                    return 9999;
                }
            }
        }
        // Six-arg: SWFOC_ReplayMockUnit(addr, "type", owner, hull, max_hull, hp_count).
        {
            std::vector<std::string> args;
            if (expr.rfind("SWFOC_ReplayMockUnit(", 0) == 0 && ExtractArgs(expr, &args) && args.size() == 6) {
                double addr_d = 0.0, owner_d = 0.0, hull_d = 0.0, max_d = 0.0, hp_d = 0.0;
                std::string type_name;
                if (ParseNumber(args[0], &addr_d) &&
                    UnquoteString(args[1], &type_name) &&
                    ParseNumber(args[2], &owner_d) &&
                    ParseNumber(args[3], &hull_d) &&
                    ParseNumber(args[4], &max_d) &&
                    ParseNumber(args[5], &hp_d)) {
                    int hp_count = static_cast<int>(hp_d);
                    if (addr_d > 0 && hp_count >= 0 && hp_count <= 256) {
                        ReplayMutMockUnit(
                            g_replay,
                            static_cast<uint64_t>(addr_d),
                            type_name,
                            static_cast<int32_t>(owner_d),
                            static_cast<float>(hull_d),
                            static_cast<float>(max_d),
                            static_cast<uint32_t>(hp_count));
                        push_num(1.0);
                    } else {
                        push_num(0.0);
                    }
                    return 9999;
                }
            }
        }
    }

    // Unknown command -- fall through to the plain fake stub so SWFOC_Log,
    // no-op scripts, etc. still "run". Replay the stream through fake_load.
    StringReaderData rd{ src.c_str(), src.size(), false };
    return fake_load(fake,
                     reinterpret_cast<void*>(+[](FakeLuaState*, void* ud, size_t* sz) -> const char* {
                         auto* r = static_cast<StringReaderData*>(ud);
                         if (r->done) { *sz = 0; return nullptr; }
                         r->done = true;
                         *sz = r->len;
                         return r->str;
                     }),
                     &rd,
                     chunkname ? chunkname : "=replay");
}

// ======================================================================
// Register all SWFOC_* helpers against a FakeLuaState
// ======================================================================

static void RegisterAll(lua_State* L) {
    struct { const char* name; lua_CFunction func; } funcs[] = {
        {"SWFOC_GetVersion",         Lua_GetVersion},
        {"SWFOC_GetLocalPlayer",     Lua_GetLocalPlayer},
        {"SWFOC_GetCredits",         Lua_GetCredits},
        {"SWFOC_SetCredits",         Lua_SetCredits},
        {"SWFOC_SetTechLevel",       Lua_SetTechLevel},
        {"SWFOC_UncapCredits",       Lua_UncapCredits},
        {"SWFOC_HeroInstantRespawn", Lua_HeroInstantRespawn},
        {"SWFOC_ListFactions",       Lua_ListFactions},
        {"SWFOC_Log",                Lua_Log},
        {"SWFOC_DoString",           Lua_DoString_Fn},
        {"SWFOC_StateInfo",          Lua_StateInfo},
        {"SWFOC_EventControl",       Lua_EventControl},
        // Replay-only helpers
        {"SWFOC_GetLocalFaction",    Lua_GetLocalFaction},
        {"SWFOC_ReplayObjectCount",  Lua_ReplayObjectCount},
        {"SWFOC_ReplayMetadata",     Lua_ReplayMetadata},
        {"SWFOC_ReplayPlayerCount",  Lua_ReplayPlayerCount},
        // v5 service observers + mutation seams (added 2026-04-08)
        {"SWFOC_ReplayPlayerCredits",       Lua_ReplayPlayerCredits},
        {"SWFOC_ReplayPlayerTechLevel",     Lua_ReplayPlayerTechLevel},
        {"SWFOC_ReplayLastStoryEvent",      Lua_ReplayLastStoryEvent},
        {"SWFOC_ReplayPushStoryEvent",      Lua_ReplayPushStoryEvent},
        {"SWFOC_ReplayDiplomaticState",     Lua_ReplayDiplomaticState},
        {"SWFOC_ReplaySetDiplomacy",        Lua_ReplaySetDiplomacy},
        {"SWFOC_ReplayPlanetCorruption",    Lua_ReplayPlanetCorruption},
        {"SWFOC_ReplaySetPlanetCorruption", Lua_ReplaySetPlanetCorruption},
        {"SWFOC_ReplayUnitOwner",           Lua_ReplayUnitOwner},
        {"SWFOC_ReplaySpawnUnit",           Lua_ReplaySpawnUnit},
        {"SWFOC_ReplayCooldownState",       Lua_ReplayCooldownState},
        {"SWFOC_ReplaySetCooldown",         Lua_ReplaySetCooldown},
        {"SWFOC_ReplayTaskForceCount",      Lua_ReplayTaskForceCount},
        {"SWFOC_ReplayAddTaskForce",        Lua_ReplayAddTaskForce},
        {"SWFOC_ReplayHumanPlayerSlot",     Lua_ReplayHumanPlayerSlot},
        {"SWFOC_ReplaySwitchLocalPlayer",   Lua_ReplaySwitchLocalPlayer},
        // Unit / hardpoint / behavior helpers (added 2026-04-23 for Task 101)
        {"SWFOC_ReplayMockUnit",             Lua_ReplayMockUnit},
        {"SWFOC_ReplayUnitCount",            Lua_ReplayUnitCount},
        {"SWFOC_ReplayUnitHull",             Lua_ReplayUnitHull},
        {"SWFOC_ReplayUnitMaxHull",          Lua_ReplayUnitMaxHull},
        {"SWFOC_ReplayUnitOwnerSlot",        Lua_ReplayUnitOwnerSlot},
        {"SWFOC_ReplayUnitInvulnFlag",       Lua_ReplayUnitInvulnFlag},
        {"SWFOC_ReplayUnitPreventDeath",     Lua_ReplayUnitPreventDeath},
        {"SWFOC_ReplayHardpointCount",       Lua_ReplayHardpointCount},
        {"SWFOC_ReplayHardpointHasBehavior", Lua_ReplayHardpointHasBehavior},
        {"SWFOC_ReplayUnitIsInvulnerable",   Lua_ReplayUnitIsInvulnerable},
        {"SWFOC_ReplayAttachBehavior",       Lua_ReplayAttachBehavior},
        {"SWFOC_ReplayDetachBehavior",       Lua_ReplayDetachBehavior},
        {"SWFOC_ReplayMakeInvulnerable",     Lua_ReplayMakeInvulnerable},
        {"SWFOC_ReplaySetUnitInvulnFlag",    Lua_ReplaySetUnitInvulnFlag},
        {"SWFOC_ReplaySetPreventDeathBit",   Lua_ReplaySetPreventDeathBit},
        {"SWFOC_ReplaySetUnitHull",          Lua_ReplaySetUnitHull},
        {"SWFOC_ReplayApplyDamage",          Lua_ReplayApplyDamage},
        {"SWFOC_ReplaySetSelected",          Lua_ReplaySetSelected},
        {"SWFOC_ReplayAppendSelected",       Lua_ReplayAppendSelected},
        {"SWFOC_ReplayClearSelected",        Lua_ReplayClearSelected},
        {"SWFOC_ReplayGetSelectedUnit",      Lua_ReplayGetSelectedUnit},
        {"SWFOC_ReplaySelectedCount",        Lua_ReplaySelectedCount},
        {"SWFOC_ReplayListTacticalUnits",    Lua_ReplayListTacticalUnits},
        {"SWFOC_ReplaySweepGodMode",         Lua_ReplaySweepGodMode},
        {"SWFOC_ReplayGodModeFullyActive",   Lua_ReplayGodModeFullyActive},
        {"SWFOC_ReplayRevealAll",            Lua_ReplayRevealAll},
        {"SWFOC_ReplayIsRevealed",           Lua_ReplayIsRevealed},
        {"SWFOC_ReplayGameMode",             Lua_ReplayGameMode},
        {"SWFOC_ReplayGetAllPlayers",        Lua_ReplayGetAllPlayers},
        {"SWFOC_ReplayEventStreamDrain",     Lua_ReplayEventStreamDrain},
        {"SWFOC_ReplayEventLogCount",        Lua_ReplayEventLogCount},
        {"SWFOC_ReplayEnumerateUnits",       Lua_ReplayEnumerateUnits},
        {"SWFOC_ReplayHealAllLocal",         Lua_ReplayHealAllLocal},
        {"SWFOC_ReplaySetDamageMultiplier",  Lua_ReplaySetDamageMultiplier},
        {"SWFOC_ReplayGetDamageMultiplier",  Lua_ReplayGetDamageMultiplier},
        {"SWFOC_ReplayKillUnit",             Lua_ReplayKillUnit},
        {"SWFOC_ReplayReviveUnit",           Lua_ReplayReviveUnit},
        {"SWFOC_ReplaySetUnitShield",        Lua_ReplaySetUnitShield},
        {"SWFOC_ReplaySetUnitMaxShield",     Lua_ReplaySetUnitMaxShield},
        {"SWFOC_ReplayUnitShield",           Lua_ReplayUnitShield},
        {"SWFOC_ReplayUnitMaxShield",        Lua_ReplayUnitMaxShield},
        {"SWFOC_ReplaySetUnitSpeed",         Lua_ReplaySetUnitSpeed},
        {"SWFOC_ReplaySetUnitMaxSpeed",      Lua_ReplaySetUnitMaxSpeed},
        {"SWFOC_ReplayUnitSpeed",            Lua_ReplayUnitSpeed},
        {"SWFOC_ReplayUnitMaxSpeed",         Lua_ReplayUnitMaxSpeed},
        {"SWFOC_ReplaySetUnitIsHero",        Lua_ReplaySetUnitIsHero},
        {"SWFOC_ReplayIsUnitHero",           Lua_ReplayIsUnitHero},
        {"SWFOC_ReplayListHeroes",           Lua_ReplayListHeroes},
        {"SWFOC_ReplaySetHeroRespawnTimer",  Lua_ReplaySetHeroRespawnTimer},
        {"SWFOC_ReplayHeroRespawnTimer",     Lua_ReplayHeroRespawnTimer},
        {"SWFOC_ReplaySetPermadeath",        Lua_ReplaySetPermadeath},
        {"SWFOC_ReplayIsPermadeath",         Lua_ReplayIsPermadeath},
        {"SWFOC_ReplayHeroStatEdit",         Lua_ReplayHeroStatEdit},
        {"SWFOC_ReplayListPlanets",          Lua_ReplayListPlanets},
        {"SWFOC_ReplayChangePlanetOwner",    Lua_ReplayChangePlanetOwner},
        {"SWFOC_ReplayPlanetOwner",          Lua_ReplayPlanetOwner},
        {"SWFOC_ReplaySetPlanetTech",        Lua_ReplaySetPlanetTech},
        {"SWFOC_ReplaySetPlanetBuildings",   Lua_ReplaySetPlanetBuildings},
        {"SWFOC_ReplaySetPlanetCapital",     Lua_ReplaySetPlanetCapital},
        {"SWFOC_ReplayPlanetTech",           Lua_ReplayPlanetTech},
        {"SWFOC_ReplayPlanetBuildings",      Lua_ReplayPlanetBuildings},
        {"SWFOC_ReplayPlanetTechAndBuildings", Lua_ReplayPlanetTechAndBuildings},
        {"SWFOC_ReplayAddUnitAbility",       Lua_ReplayAddUnitAbility},
        {"SWFOC_ReplayListAbilities",        Lua_ReplayListAbilities},
        {"SWFOC_ReplayTriggerAbility",       Lua_ReplayTriggerAbility},
        {"SWFOC_ReplayTickAbilityCooldown",  Lua_ReplayTickAbilityCooldown},
        {"SWFOC_ReplayAbilityCooldown",      Lua_ReplayAbilityCooldown},
        {"SWFOC_ReplaySetIncomeMultiplier",  Lua_ReplaySetIncomeMultiplier},
        {"SWFOC_ReplayGetIncomeMultiplier",  Lua_ReplayGetIncomeMultiplier},
        {"SWFOC_ReplaySetGameSpeed",         Lua_ReplaySetGameSpeed},
        {"SWFOC_ReplayGetGameSpeed",         Lua_ReplayGetGameSpeed},
        {"SWFOC_ReplaySetFreezeCredits",     Lua_ReplaySetFreezeCredits},
        {"SWFOC_ReplayIsFreezeCredits",      Lua_ReplayIsFreezeCredits},
        {"SWFOC_ReplayFreezeCreditsTarget",  Lua_ReplayFreezeCreditsTarget},
        {"SWFOC_ReplayTickIncome",           Lua_ReplayTickIncome},
        {"SWFOC_ReplaySetBuildSpeed",        Lua_ReplaySetBuildSpeed},
        {"SWFOC_ReplayGetBuildSpeed",        Lua_ReplayGetBuildSpeed},
        {"SWFOC_ReplaySetFactionSpeedMult",  Lua_ReplaySetFactionSpeedMult},
        {"SWFOC_ReplayGetFactionSpeedMult",  Lua_ReplayGetFactionSpeedMult},
        {"SWFOC_ReplaySetFireRate",          Lua_ReplaySetFireRate},
        {"SWFOC_ReplayGetFireRate",          Lua_ReplayGetFireRate},
        {"SWFOC_ReplayApplyFireRate",        Lua_ReplayApplyFireRate},
        {"SWFOC_ReplaySetAreaDamage",        Lua_ReplaySetAreaDamage},
        {"SWFOC_ReplayIsAreaDamage",         Lua_ReplayIsAreaDamage},
        {"SWFOC_ReplayApplyAreaSplash",      Lua_ReplayApplyAreaSplash},
        {"SWFOC_ReplaySetTargetFilter",      Lua_ReplaySetTargetFilter},
        {"SWFOC_ReplayGetTargetFilter",      Lua_ReplayGetTargetFilter},
        {"SWFOC_ReplayIsTargetAllowed",      Lua_ReplayIsTargetAllowed},
        {"SWFOC_ReplaySetOHK",               Lua_ReplaySetOHK},
        {"SWFOC_ReplayIsOHK",                Lua_ReplayIsOHK},
        {"SWFOC_ReplayGetAttackPower",       Lua_ReplayGetAttackPower},
        {"SWFOC_ReplaySetAiFrozen",          Lua_ReplaySetAiFrozen},
        {"SWFOC_ReplayIsAiFrozen",           Lua_ReplayIsAiFrozen},
        {"SWFOC_ReplayFrozenAiCount",        Lua_ReplayFrozenAiCount},
        {"SWFOC_ReplaySetCameraUnlocked",    Lua_ReplaySetCameraUnlocked},
        {"SWFOC_ReplayIsCameraUnlocked",     Lua_ReplayIsCameraUnlocked},
        {"SWFOC_ReplaySetCameraPos",         Lua_ReplaySetCameraPos},
        {"SWFOC_ReplayGetCameraX",           Lua_ReplayGetCameraX},
        {"SWFOC_ReplayGetCameraY",           Lua_ReplayGetCameraY},
        {"SWFOC_ReplayGetCameraZ",           Lua_ReplayGetCameraZ},
        {"SWFOC_ReplaySetCameraRot",         Lua_ReplaySetCameraRot},
        {"SWFOC_ReplayGetCameraRot",         Lua_ReplayGetCameraRot},
        {"SWFOC_ReplaySetCameraZoom",        Lua_ReplaySetCameraZoom},
        {"SWFOC_ReplayGetCameraZoom",        Lua_ReplayGetCameraZoom},
        {"SWFOC_ReplaySpawnUnits",           Lua_ReplaySpawnUnits},
        {"SWFOC_ReplaySetBuildCost",         Lua_ReplaySetBuildCost},
        {"SWFOC_ReplayGetBuildCost",         Lua_ReplayGetBuildCost},
        {"SWFOC_ReplaySetUnitCapOverride",   Lua_ReplaySetUnitCapOverride},
        {"SWFOC_ReplayClearUnitCapOverride", Lua_ReplayClearUnitCapOverride},
        {"SWFOC_ReplayGetUnitCapOverride",   Lua_ReplayGetUnitCapOverride},
        {"SWFOC_ReplaySetUnitField",         Lua_ReplaySetUnitField},
        {"SWFOC_ReplayGetUnitField",         Lua_ReplayGetUnitField},
        {"SWFOC_ReplaySetInstantBuild",      Lua_ReplaySetInstantBuild},
        {"SWFOC_ReplayIsInstantBuild",       Lua_ReplayIsInstantBuild},
        {"SWFOC_ReplayShouldBuildComplete",  Lua_ReplayShouldBuildComplete},
        {"SWFOC_ReplaySetFreeBuild",         Lua_ReplaySetFreeBuild},
        {"SWFOC_ReplayIsFreeBuild",          Lua_ReplayIsFreeBuild},
        {"SWFOC_ReplayComputeBuildCost",     Lua_ReplayComputeBuildCost},
    };
    for (auto& f : funcs) {
        fn_pushstring(L, f.name);
        fn_pushcclosure(L, f.func, 0);
        fn_settable(L, LUA_GLOBALSINDEX);
    }
}

// ======================================================================
// Pipe listener -- COPY of lua_bridge.cpp's PipeThreadProc/DrainPipeCommand,
// adapted to use the replay pipe name and to drive DoString directly on the
// embedded FakeLuaState (no main-thread marshaling, no luaD_call hook).
// ======================================================================

static CRITICAL_SECTION g_replayLock;
static FakeLuaState     g_replayLua;          // The one-and-only replay VM
static volatile bool    g_pipeShutdown = false;

// Execute one command string against the replay VM and emit a response.
static void ReplayExecute(const char* cmd, std::string* response) {
    EnterCriticalSection(&g_replayLock);

    LogErr("[Replay] Executing: %.120s%s\n", cmd,
           strlen(cmd) > 120 ? "..." : "");

    int savedTop = fn_gettop(LS(&g_replayLua));
    int err = DoString(LS(&g_replayLua), cmd, "=replay_pipe");

    if (err == 0 || err == 9999) {
        // Success: read the value at the top of the stack.
        const char* retVal = fn_tostring(LS(&g_replayLua), -1);
        if (retVal && retVal[0]) {
            *response = std::string(retVal) + "\n";
        } else {
            *response = "OK\n";
        }
    } else {
        const char* errMsg = fn_tostring(LS(&g_replayLua), -1);
        if (!errMsg) errMsg = "unknown error";
        *response = std::string("ERR: ") + errMsg + "\n";
    }
    fn_settop(LS(&g_replayLua), savedTop);

    LeaveCriticalSection(&g_replayLock);
}

static DWORD WINAPI ReplayPipeThreadProc(LPVOID) {
    LogErr("[Replay] Pipe listener thread started on %s\n", REPLAY_PIPE_NAME);

    while (!g_pipeShutdown) {
        HANDLE hPipe = CreateNamedPipeA(
            REPLAY_PIPE_NAME,
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            1,             // max instances
            4096,          // out buffer
            PIPE_CMD_MAX,  // in buffer
            1000,          // default timeout ms
            nullptr);

        if (hPipe == INVALID_HANDLE_VALUE) {
            LogErr("[Replay] CreateNamedPipe failed: %lu\n", GetLastError());
            Sleep(1000);
            continue;
        }

        BOOL connected = ConnectNamedPipe(hPipe, nullptr)
            ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED);

        if (!connected || g_pipeShutdown) {
            CloseHandle(hPipe);
            continue;
        }

        LogErr("[Replay] Client connected\n");

        char buf[PIPE_CMD_MAX];
        DWORD totalRead = 0;
        BOOL readOk = ReadFile(hPipe, buf, PIPE_CMD_MAX - 1, &totalRead, nullptr);
        if (!readOk || totalRead == 0) {
            LogErr("[Replay] Read failed or empty\n");
            DisconnectNamedPipe(hPipe);
            CloseHandle(hPipe);
            continue;
        }
        buf[totalRead] = '\0';

        LogErr("[Replay] Received %lu bytes\n", (unsigned long)totalRead);

        std::string response;
        ReplayExecute(buf, &response);

        DWORD written = 0;
        WriteFile(hPipe, response.c_str(),
                  static_cast<DWORD>(response.size()),
                  &written, nullptr);
        FlushFileBuffers(hPipe);
        DisconnectNamedPipe(hPipe);
        CloseHandle(hPipe);
        LogErr("[Replay] Client disconnected\n");
    }

    LogErr("[Replay] Pipe listener thread exiting\n");
    return 0;
}

// ======================================================================
// Ctrl-C shutdown
// ======================================================================

static BOOL WINAPI CtrlHandler(DWORD ctrl) {
    if (ctrl == CTRL_C_EVENT || ctrl == CTRL_BREAK_EVENT ||
        ctrl == CTRL_CLOSE_EVENT) {
        LogErr("\n[Replay] Shutdown signal received\n");
        g_pipeShutdown = true;
        // Kick the listener out of its blocking ConnectNamedPipe by opening a
        // throwaway handle to our own pipe.
        HANDLE h = CreateFileA(REPLAY_PIPE_NAME, GENERIC_READ | GENERIC_WRITE,
                               0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (h != INVALID_HANDLE_VALUE) CloseHandle(h);
        return TRUE;
    }
    return FALSE;
}

// ======================================================================
// main
// ======================================================================

static int RunExecScript(const std::vector<std::string>& scripts) {
    // Execute one or more Lua snippets directly against the already-loaded
    // replay VM and print each result on its own line. No pipe. This is the
    // autonomous-iteration hook for Tasks 99/100: a CI loop or agent can
    // run the binary with `--exec "return SWFOC_ReplayUnitHull(...)"` and
    // read the answer on stdout without setting up a named-pipe client.
    int failures = 0;
    for (const auto& code : scripts) {
        int savedTop = fn_gettop(LS(&g_replayLua));
        int err = DoString(LS(&g_replayLua), code.c_str(), "=replay_exec");
        if (err == 0 || err == 9999) {
            const char* ret = fn_tostring(LS(&g_replayLua), -1);
            if (ret && ret[0]) {
                printf("%s\n", ret);
            } else {
                printf("OK\n");
            }
        } else {
            const char* errMsg = fn_tostring(LS(&g_replayLua), -1);
            fprintf(stderr, "ERR: %s\n", errMsg ? errMsg : "unknown error");
            failures++;
        }
        fn_settop(LS(&g_replayLua), savedTop);
    }
    return failures == 0 ? 0 : 5;
}

int main(int argc, char** argv) {
    // CLI:
    //   swfoc_replay.exe <snapshot>                     — host the pipe listener
    //   swfoc_replay.exe <snapshot> --exec "<lua>" [...] — run one or more Lua
    //                                                     snippets offline and exit
    //   swfoc_replay.exe <snapshot> --dump              — print summary and exit
    if (argc < 2) {
        fprintf(stderr,
            "Usage: %s <path-to-snapshot.swfocsnap> [--exec \"<lua>\" ...] [--dump]\n"
            "\n"
            "Default: load the snapshot and host the replay pipe at %s.\n"
            "--exec   Run the given Lua snippets, print each result on stdout, exit.\n"
            "         Use `return <expr>` so the snippet pushes a value.\n"
            "--dump   Print a one-line summary of the loaded state and exit.\n",
            argv[0] ? argv[0] : "swfoc_replay.exe",
            REPLAY_PIPE_NAME);
        return 2;
    }

    const char* snapPath = argv[1];
    bool dumpOnly = false;
    std::vector<std::string> execScripts;
    for (int i = 2; i < argc; ++i) {
        std::string arg = argv[i];
        if (arg == "--dump") {
            dumpOnly = true;
        } else if (arg == "--exec") {
            if (i + 1 >= argc) {
                fprintf(stderr, "--exec requires a Lua snippet\n");
                return 2;
            }
            execScripts.emplace_back(argv[++i]);
        } else {
            fprintf(stderr, "unknown flag: %s\n", arg.c_str());
            return 2;
        }
    }

    // --- 1. Load the snapshot ---
    auto r = LoadSnapshot(snapPath, g_replay);
    if (!r.ok) {
        LogErr("[Replay] Failed to load '%s': %s\n", snapPath, r.error.c_str());
        return 3;
    }

    LogOut("[Replay] Loaded %zu bytes, magic OK, version=%u\n",
           r.total_bytes, g_replay.format_version);
    LogOut("[Replay] %zu players, %zu object types, %zu globals, %zu metadata entries\n",
           g_replay.players.size(),
           g_replay.objects.size(),
           g_replay.globals.size(),
           g_replay.metadata.size());
    LogOut("[Replay] %zu units, %zu selected\n",
           g_replay.units.size(),
           g_replay.selected_units.size());

    if (dumpOnly && execScripts.empty()) {
        // Nothing else to do; the LogOut lines above are the summary.
        return 0;
    }

    // --- 2. Wire up fake Lua + register SWFOC_* helpers ---
    WireFakes();
    InitializeCriticalSection(&g_replayLock);
    fake_reset(&g_replayLua);
    RegisterAll(LS(&g_replayLua));

    // Override fn_load with the replay-aware intercept so simple pipe
    // commands return real values instead of "nil" stubs.
    fn_load = reinterpret_cast<pfn_lua_load>(&ReplayLoad);

    // --- 3. If --exec was supplied, run scripts and exit.
    if (!execScripts.empty()) {
        int rc = RunExecScript(execScripts);
        DeleteCriticalSection(&g_replayLock);
        return rc;
    }

    // --- 4. Otherwise, start the pipe listener.
    SetConsoleCtrlHandler(CtrlHandler, TRUE);
    HANDLE hThread = CreateThread(nullptr, 0, ReplayPipeThreadProc, nullptr, 0, nullptr);
    if (!hThread) {
        LogErr("[Replay] Failed to create pipe listener thread: %lu\n", GetLastError());
        DeleteCriticalSection(&g_replayLock);
        return 4;
    }

    LogOut("[Replay] Listening on %s\n", REPLAY_PIPE_NAME);
    LogOut("[Replay] Press Ctrl-C to exit\n");

    // Block until Ctrl-C or thread exits on its own.
    WaitForSingleObject(hThread, INFINITE);
    CloseHandle(hThread);
    DeleteCriticalSection(&g_replayLock);

    LogOut("[Replay] Bye\n");
    return 0;
}
