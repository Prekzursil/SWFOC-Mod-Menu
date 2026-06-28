# Overlay UX Research — 2026-05-08

Research dispatched during iter-284 by parallel sub-agent. Informs Phase 3-6 overlay roadmap.

## TL;DR

- **ImGui v1.91.5 drag-drop API is fully available** in vendored sources (`swfoc_overlay/imgui/imgui.h:877-888`).
- **CRITICAL GAP 1**: No "click-on-unit" / "cursor-hits-unit-at-screen-coords" bridge wire exists. Phase 5 (click-to-inspect) is BLOCKED until either a NEW bridge wire ships OR client-side raycasting proxy is built.
- **CRITICAL GAP 2**: Projection matrix RVA NOT pinned in `verified_facts.json`. Phase 4 (drag-drop spawning) needs cursor→world conversion. Interim mitigation: use 2D Z=0 plane assumption (sufficient for ground units in tactical mode); 3D raycasting deferred to Phase 5+ pending RE.
- **Top 3 quick wins** (high UX value × low effort):
  1. **Bookmarkable camera positions** (F6/F7/F8) — leverages iter-237 LIVE SetCameraPos/GetCameraPos. ~30 LoC.
  2. **Recent-actions toolbar** — re-execute last 5 SWFOC calls. No new bridge deps. ~50 LoC overlay.
  3. **Mini-map** — top-down 256×256 quad with unit dots. Doubles as Phase 4 drag-drop target. ~500 LoC.

## ImGui drag-drop API surface

**Source-side**:
```cpp
bool ImGui::BeginDragDropSource(ImGuiDragDropFlags flags = 0);
bool ImGui::SetDragDropPayload(const char* type, const void* data, size_t sz, ImGuiCond cond = 0);
void ImGui::EndDragDropSource();
```

**Target-side**:
```cpp
bool ImGui::BeginDragDropTarget();
const ImGuiPayload* ImGui::AcceptDragDropPayload(const char* type, ImGuiDragDropFlags flags = 0);
void ImGui::EndDragDropTarget();
```

**Payload struct** (`imgui.h:2485`): `void* Data`, `int DataSize`, `char DataType[33]`, `bool Preview`, `bool Delivery`, methods `IsDataType()` / `IsPreview()` / `IsDelivery()`.

**Flags** (`imgui.h:1357-1371`): `SourceNoPreviewTooltip`, `AcceptBeforeDelivery`, `AcceptNoDrawDefaultRect`.

**SWFOC shape**: source widget = unit-type list in overlay panel; payload type = `"unit_type_<NAME>"`; target = either fixed minimap drop zone OR full game world (needs cursor→world).

## Bridge wires usable from overlay

**Spawn**:
| Wire | Mode | Signature | LIVE iter |
|---|---|---|---|
| `SWFOC_SpawnUnitLua` | Tactical | `(player, type, pos)` | 109 |
| `SWFOC_GalacticSpawnUnit` | Galactic | `(player, type, planet)` | 152 |
| `SWFOC_ReinforceUnitLua` | Tactical (pool) | `(player, type, pos)` | 185 |
| `SWFOC_SpawnFromReinforcementPoolLua` | Tactical (alt) | `(player, type, pos)` | 185 |
| `SWFOC_CreateGenericObjectLua` | Tactical | `(type, pos, player)` ← param order differs | 185 |

**Movement**: `SWFOC_TeleportUnitLua(unit, pos)` (151), `SWFOC_SetCameraPos(x,y,z)` (237 detour), `SWFOC_GetCameraPos` returns "X,Y,Z" (237).

**Mutation**: `SWFOC_KillUnit` (detour), `SWFOC_HealUnitLua` (154), `SWFOC_ChangeUnitOwner` (108), `SWFOC_GodMode` (detour), `SWFOC_MakeInvulnerableLua` (110), `SWFOC_HideLua`/`SWFOC_PreventAiUsageLua`/`SWFOC_SetUnitSelectableLua` (111).

**Discovery**: `SWFOC_FindObjectTypeLua` (177), `SWFOC_FindFirstObjectLua` (177), `SWFOC_FindAllObjectsOfTypeLua` (179), `SWFOC_FindNearestLua` (186), `SWFOC_GetLocalPlayer`, `SWFOC_EnumerateUnits` (LIVE).

**Read-side stats**: `SWFOC_GetOwnerLua` (168), `SWFOC_GetPositionLua` (171), `SWFOC_GetHealthLua`/`SWFOC_GetHullLua`/`SWFOC_GetShieldLua` (167+).

**MISSING (gap noted)**: `SWFOC_GetUnitAtScreenCoords` — engine has internal selection system; needs new bridge wire to expose.

## Cursor→world-space conversion (Phase 4 blocker)

**What's pinned**:
- `CameraClass +0x40` view-matrix pointer (per-frame)
- `rva_camera_get_position` @ 0x261A40 (LIVE, iter-237)
- `rva_camera_set_transform_matrix` @ 0x261BD0 (LIVE, iter-237)

**What's missing**:
- Projection matrix RVA (or D3D9 viewport + FOV synthesis)
- Screen→NDC transformation (viewport divide)
- Camera-ray raycasting (ray through world units)

**Phase 4 MVP recommendation**: Lock to 2D Z=0 plane (ground-units only). Operator drags unit-type onto a minimap region in overlay; click maps to world X/Y; Z is 0 (ground level) or operator-tweakable slider. Defers full 3D raycasting to Phase 5+ pending dedicated RE iter.

## Brainstormed interactive features (12 candidates)

| # | Feature | Difficulty | Bridge deps | UX value |
|---|---|---|---|---|
| 1 | Unit Inspector (click in-game) | High | NEW: cursor-hit detection wire | HIGH |
| 2 | Faction-Switch Hotkey (F3) | Medium | iter-108 ChangeUnitOwner / engine Switch_Sides | MEDIUM |
| 3 | Pause/Resume Time (F4) | Low | iter-178 GetSecondsPerGameMinute / Phase 2 SetGameSpeed | MEDIUM |
| 4 | Quick-Save/Quick-Load (F5/F9) | High | NEW: state capture/restore | MEDIUM |
| 5 | **Bookmarkable Camera Positions (F6/F7/F8)** | **Low** | iter-237 SetCameraPos/GetCameraPos | **HIGH** |
| 6 | **Recent-Actions Toolbar** | **Medium** | None new | **MEDIUM** |
| 7 | Weather/Time-of-Day Cycler | Medium | NEW: Lua user-var or Story_Event | LOW |
| 8 | Cinematic Mode | High | iter-162/165 Fade + Letter_Box + SetGameSpeed + Suspend_AI | HIGH |
| 9 | In-Overlay Event Log Tail | Medium | NEW: bridge story-event tail | MEDIUM |
| 10 | **Mini-Map Overlay** | **High** | Engine heightmap + ortho cam | **HIGH** |
| 11 | Mod-Info HUD Extension | Low | iter-228 BatchTypeExists + mod metadata | MEDIUM |
| 12 | "Drop Here" Visual Ring | Medium | NEW: unit-type dimensions + D3D9 line draw | HIGH |

## Phased delivery plan

### Phase 3: Interactive widgets in overlay (iter 286-290, ~5 iters)

In-overlay buttons that call bridge wires. No drag-drop, no world interaction.
- ImGui::Combo (faction/type), ImGui::InputFloat3 (position), ImGui::Button.
- "God mode", "Heal all", "Pause", "Kill selected", "Teleport selected".
- Result feedback in footer or toast.

### Phase 4: Drag-drop tactical spawning (iter 291-295, ~5 iters)

Drag unit-type from overlay onto minimap or world.
- BeginDragDropSource (unit-type list) → SetDragDropPayload("unit_type", type_string).
- 2D Z=0 plane interim (Phase 5 promotes to 3D).
- D3D9 line/circle preview ring at drop point.
- SWFOC_SpawnUnitLua call with converted coords.

### Phase 5: Click-to-inspect + 3D cursor (iter 296-302, ~7 iters)

Click in-game unit → overlay inspector with unit metadata + action buttons.
- NEW bridge wire: cursor-hit-unit detection (or client-side raycasting proxy).
- Projection matrix RVA pinned (RE iter required).
- Inspector: GetOwner/GetHealth/GetHull/GetShield read-side; Kill/Heal/Teleport/SwapOwner write-side.

### Phase 6: Hotkey expansion + power-user features (iter 303+, ~4 iters)

- F2 spawn mode toggle (from Phase 4).
- F3 faction-switch (cycle).
- F4 pause/resume (with current-speed display).
- F5/F9 quick-save/load (deferred to Phase 7 if state-capture too complex).
- F6/F7/F8 bookmarkable camera positions.
- Numpad 1-4 faction-specific god-mode toggles.

## Open questions blocking Phase 4

1. **Projection Matrix RVA**: where in the engine is it stored? RE iter needed.
2. **Screen-click model**: 2D plane (simple) vs 3D raycast (durable)?
3. **Cursor-under-unit**: NEW bridge wire vs client-side raycast?
4. **Drag-drop payload format**: type-only vs full spawn args? Recommend type-only.
5. **Mod-aware unit lists**: refresh on mod change vs static-at-startup?
6. **Multi-player safety**: gate mutations by `SWFOC_GetLocalPlayer` check.

## Source files referenced

- `swfoc_overlay/imgui/imgui.h` (ImGui v1.91.5 API)
- `SWFOC editor/src/SwfocTrainer.Core/Diagnostics/CapabilityStatusCatalog.cs` (226 wire catalog)
- `knowledge-base/verified_facts.json` (camera RVAs)
- `knowledge-base/alamo_engine_reference.md` (engine pointers)
- `swfoc_overlay/overlay_design_2026-04-27.md` (architectural rationale)
