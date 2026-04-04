# RE Integration Plan: swfoc_memory -> SWFOC Editor

**Date:** 2026-04-04
**Source:** Phase 1-2 RE knowledge base in `C:\Users\Prekzursil\Downloads\swfoc_memory`
**Target:** SWFOC Editor at `C:\Users\Prekzursil\Downloads\SWFOC editor` (Prekzursil/SWFOC-Mod-Menu)

---

## Editor Architecture Summary

The editor uses a **signature-first symbol resolution** pipeline:

1. **Profile JSON** (`profiles/default/profiles/base_swfoc.json`) defines:
   - `signatureSets` — AOB patterns with `addressMode` (HitPlusOffset, ReadRipRelative32AtOffset, ReadAbsolute32AtOffset)
   - `fallbackOffsets` — hardcoded RVAs when signatures fail
   - `actions` — named operations referencing resolved symbols

2. **SignatureResolver** scans the live process module for AOB matches, resolves addresses via RIP-relative or absolute addressing, and builds a `SymbolMap`.

3. **Ghidra Symbol Pack** hydration — `SignatureResolver.SymbolHydration.cs` loads JSON symbol packs from `profiles/default/sdk/ghidra/symbol-packs/`. Format:
   ```json
   {
     "SchemaVersion": "1.0",
     "BinaryFingerprint": { "FingerprintId": "<module>_<sha256>" },
     "BuildMetadata": { "GeneratedAtUtc": "..." },
     "Anchors": [
       { "Id": "<symbol_name>", "Address": <integer_rva>, "Confidence": 1.0 }
     ]
   }
   ```
   These bypass AOB scanning entirely — direct RVA-based resolution with binary fingerprint validation.

4. **Actions** execute via `executionKind`: `Memory` (direct read/write), `Sdk` (runtime hook), `Helper` (Lua/pipe bridge), `CodePatch` (byte patching), `Freeze` (continuous write), `Save` (save file edit).

---

## Feature-by-Feature Integration Analysis

### TIER 1: Now Unblockable (Phase 2 provides the missing data)

#### 1. `set_credits` — **MISMATCH DETECTED**

**Current state:** Profile has signature `credits` with AOB `8B 0D ?? ?? ?? ?? 41 B8 0C 00 00 00 48 8B 14 C8`, addressMode `ReadRipRelative32AtOffset`, valueType `Int32`, fallback offset `10882632` (0xA62148).

**RE finding:** Credits are at `PlayerObject + 0x70` as **float32**, not Int32. The current signature resolves to a *static global* that mirrors the selected player's credits for UI display — NOT the authoritative player object field.

**Mismatch:** The editor writes Int32 to a UI-facing mirror variable. This works for display but may not persist through game events. The authoritative path is:
- `PlayerArray (global 0xA16FF0)` -> index by player ID -> `PlayerObject + 0x70` (float32)
- Or call `AddCredits` at RVA `0x27F370`

**Recommendation:** **Keep the existing approach for now** (it works). Add a new `set_credits_authoritative` action that reads PlayerArray + writes float to player+0x70. This is a moderate change.

**Complexity:** Moderate (needs pointer chain traversal: global -> array -> player -> field)

#### 2. `set_selected_speed` — **MISMATCH DETECTED**

**Current state:** Signature `selected_speed` with AOB pointing to a UI-mirrored static variable, valueType `Float`.

**RE finding:** Speed override is at `GameObjectClass + 0xA8 -> locomotor + 0x29C` (flag) + `+0x2A0` (float value). The native `Override_Max_Speed` Lua function or `SetSpeedOverride` at `0x3A8C90` is the proper path.

**Mismatch:** Editor writes a UI-mirror float. RE shows the engine reads speed from the locomotor component, not a static. The UI mirror may work for display but the engine ignores it for actual movement.

**Recommendation:** Add new action `set_speed_override` using pointer chain: selected_object -> +0xA8 -> locomotor -> +0x29C (set 1) + +0x2A0 (write speed). Or hook `SetSpeedOverride` at `0x3A8C90`.

**Complexity:** Moderate (pointer chain through selected object)

#### 3. `set_hero_respawn_timer` — **VALIDATED**

**Current state:** Has signature `hero_respawn_timer` with AOB, fallback offset `1384560` (0x152070).

**RE finding:** `Default_Hero_Respawn_Time` global at RVA `0xB169F0` (11495920 decimal). The `ScheduleHeroRespawn` function reads this when delay<=0.

**Mismatch:** Fallback offset `0x152070` does NOT match RE finding `0xB169F0`. **The current offset appears wrong.** Needs validation.

**Recommendation:** Update fallback offset to `11495920` (0xB169F0). Verify AOB pattern still matches.

**Complexity:** Trivial (offset update in profile JSON)

#### 4. `toggle_tactical_god_mode` / `toggle_tactical_one_hit_mode` — **VALIDATED**

**Current state:** Working via AOB-resolved Bool writes. Already marked as `experimentalFeatures` in profile.

**RE finding:** The invulnerability flag is at `GameObjectClass + 0x3A7`, confirmed in Phase 1. The god mode AOB likely patches the `Take_Damage_Outer` check.

**Status:** Already working. Phase 2 confirms the mechanism. Can be promoted from experimental to stable.

**Complexity:** Trivial (remove from `experimentalFeatures` list)

### TIER 2: Quick Wins (Profile JSON changes only)

#### 5. `set_tech_level` — **NEW ACTION NEEDED**

**Current state:** No `set_tech_level` action exists in profile. Not in `signatureSets`.

**RE finding:** Tech level at `PlayerObject + 0x84` (int32). `SetTechLevel` at RVA `0x288980`. Lua `Set_Tech_Level` at `0x604480`.

**Recommendation:** Add new signature for `SetTechLevel` function prologue (AOB in `signatures_phase2.json`). Add action `set_tech_level` of kind `Memory` writing Int32 to resolved player+0x84. Or kind `Helper` calling Lua `Set_Tech_Level`.

**Complexity:** Moderate (new action + signature + pointer chain to player object)

#### 6. `income_multiplier` — **NEW ACTION NEEDED**

**RE finding:** Income multiplier at `FUN_1404B0500() + 0x20`. Applied by `AddCredits` for positive values only.

**Recommendation:** Add signature for the multiplier-source function, add action `set_income_multiplier` writing Float to resolved address + 0x20.

**Complexity:** Moderate

#### 7. `set_max_credits` — **NEW ACTION POSSIBLE**

**RE finding:** `PlayerObject + 0x74` (float32). Set to negative to disable cap.

**Complexity:** Same as credits — pointer chain through PlayerArray.

### TIER 3: Features Requiring New Capabilities

#### 8. Ability Triggering — **MAPPED BUT COMPLEX**

**RE finding:** 91 ability classes recovered. Each has `vfunction2` (Activate). Triggering requires: get unit -> get ability list -> find specific ability -> call vfunction2.

**Recommendation:** This needs a Helper execution path. Either inject via Lua (if ability has a Lua binding) or via native vtable call injection.

**Complexity:** Complex (needs ability enumeration + vtable call injection)

#### 9. `spawn_unit_helper` — **VALIDATED**

**Current state:** Action exists with `executionKind: Helper`. Uses `helperHookId`.

**RE finding:** `Spawn_Unit` Lua function at `0x898C28`, `Galactic_Spawn_Unit` implementation at `0x546C70`. 40 global Lua functions mapped.

**Status:** The helper bridge likely already calls Spawn_Unit via Lua pipe. RE confirms the backing implementation exists and is stable.

**Complexity:** Trivial (existing path validated)

#### 10. `place_planet_building` — **MAPPED**

**Current state:** Action exists with `executionKind: Helper`.

**RE finding:** Planet garrison list at `planet + 0x978`, tech requirement at `planet + 0x89C`. Building placement likely goes through `Galactic_Spawn_Unit` with building type + planet target.

**Status:** Helper path exists. RE data can improve error handling (check tech level before attempt).

**Complexity:** Low (existing path, RE improves validation)

---

## Mismatches & Corrections

| Item | Current Value | RE Finding | Impact |
|------|--------------|-----------|--------|
| Credits type | Int32 | **float32** (PlayerObject+0x70) | Write works but type is wrong — may cause truncation |
| Credits fallback | 0xA62148 | PlayerObject+0x70 via PlayerArray (0xA16FF0) | Current path writes a UI mirror, not the authoritative field |
| Hero respawn fallback | 0x152070 | **0xB169F0** | **Likely wrong offset — needs immediate correction** |
| Speed path | Static mirror | Locomotor component +0x29C/0x2A0 | Current write doesn't affect engine movement calculation |
| Set_Hull Lua path | Assumed available | **Does NOT exist** as Lua function | Helper layer cannot use Lua for HP writes |

---

## Quick-Win Ranking (effort-to-impact ratio)

| Rank | Change | Effort | Impact | What to Do |
|------|--------|--------|--------|------------|
| 1 | Fix hero_respawn_timer fallback | 5 min | High | Change `1384560` to `11495920` in profile JSON |
| 2 | Promote god_mode/one_hit from experimental | 5 min | Medium | Remove from `experimentalFeatures` in profile JSON |
| 3 | Add tech_level signature + fallback | 30 min | High | New signature entry + new action in profile JSON |
| 4 | Add credits_authoritative action | 1 hr | High | New action using PlayerArray pointer chain |
| 5 | Add speed_override action | 1 hr | High | New action using locomotor pointer chain |
| 6 | Add max_credits action | 30 min | Medium | PlayerObject+0x74 float write (same chain as credits) |
| 7 | Add income_multiplier action | 1 hr | Medium | New signature + float write |
| 8 | Generate Ghidra Symbol Pack | 30 min | High | Convert all known RVAs to GhidraAnchorDto format |

---

## Ghidra Symbol Pack Generation

The editor's `SignatureResolver.SymbolHydration` system can consume a JSON symbol pack that provides **direct RVA-based symbol resolution** — bypassing AOB scanning entirely. This is the ideal delivery format for RE-derived knowledge.

### Required Format

```json
{
  "SchemaVersion": "1.0",
  "BinaryFingerprint": {
    "FingerprintId": "starwarsg.exe_<sha256_of_current_binary>"
  },
  "BuildMetadata": {
    "GeneratedAtUtc": "2026-04-04T00:00:00Z"
  },
  "Anchors": [
    { "Id": "credits", "Address": <absolute_address_as_integer>, "Confidence": 1.0 },
    { "Id": "tech_level", "Address": <absolute_address_as_integer>, "Confidence": 1.0 },
    ...
  ]
}
```

### Gap

The current `GhidraAnchorDto` expects `Address` as an integer (absolute address at runtime). But our RE findings are **RVAs** (relative to image base). The adapter needs to add `image_base + RVA` to produce the absolute address. Since image base varies per launch (ASLR), the symbol pack should store the **module-relative offset** and the hydration code should add the runtime base address.

**Looking at the code:** `TryBuildAnchorSymbol` in `SignatureResolver.SymbolHydration.cs` — need to verify if Address is treated as absolute or module-relative. If absolute, the pack needs regeneration per launch (impractical). If module-relative, we can generate it once.

### Recommendation

Check how `GhidraAnchorDto.Address` is consumed. If it's added to module base, generate a static pack. If it's used as-is, we need the fallbackOffsets path instead (which already supports module-relative offsets).

---

## Still Blocked (Needs Phase 3 RE)

| Feature | What's Missing | Phase 3 Work |
|---------|---------------|--------------|
| Struct-aware object enumeration | Need PooledObjectClass allocator understanding | Decompile PooledObjectClass vtable |
| Per-ability cooldown manipulation | Need ability countdown data pack field offsets | Decompile AbilityCountdownDataPackClass |
| Damage scaling (not just on/off) | Need damage multiplier field in combat component | Decompile damage path from Take_Damage to final calculation |
| Planet building slot manipulation | Need building slot struct layout | Decompile planet building management functions |
| Faction diplomacy changes | Need alliance/enemy relationship storage | Decompile FactionClass and alliance system |
| Save file hero state mapping | Need exact save format field mapping per mod | Validate schema against RE-derived struct layouts |

---

## Files to Generate for Editor

1. **`re_integration_plan.md`** — this document
2. **`ghidra_symbol_pack.json`** — Ghidra Symbol Pack for current binary
3. **`signatures_phase2_editor_format.json`** — Phase 2 signatures in editor profile format
4. **Updated `base_swfoc.json`** — corrected fallbacks + new actions (NOT in this deliverable — planning only)
