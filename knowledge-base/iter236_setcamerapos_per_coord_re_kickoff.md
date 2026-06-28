# Iter 236 — A1.x SetCameraPos per-coord RE kickoff (multi-iter arc, iter 1 of ~5)

**Date:** 2026-05-06
**Status at end of iter 236:** RE design doc complete; design decision matrix selected; iter 237-240 implementation outline ready.
**Predecessor arcs:** iter 224-228 (A1.3 SetFireRate via WeaponTick MinHook detour), iter 230-234 (A1.x FreezeCredits via AddCredits MinHook detour). Both followed canonical 5-iter shape. **Iter 236 deliberately uses a DIFFERENT pattern** (direct call, not MinHook detour) to expand the arc-shape playbook.

---

## Headline finding

**`CameraClass::SetTransformMatrix @ RVA 0x261BD0` is the canonical 4x3 matrix setter, callable directly from C++.** No MinHook detour needed — bridge can construct a 4x3 matrix in C++, copy current rotation from `GetPosition`'s matrix source, set translation X/Y/Z to the new values, and call SetTransformMatrix. Pattern parallels **iter-100 SetSpeedOverride** (direct engine-function call, not detour) — proves the canonical 5-iter arc shape works for both detour-style and direct-call-style wires.

This is the **lowest-complexity remaining A1.x candidate** in the deferred set:
- Setter is 80 bytes, 4-tool VERIFIED (`rva_camera_set_transform_matrix`)
- Reader is 34 bytes, 4-tool VERIFIED (`rva_camera_get_position`)
- 16 callers of SetTransformMatrix (CameraClass animation pipeline) → low risk of breaking other systems
- Field offsets confirmed via decompile: positions at matrix[3] / [7] / [11] (translation column of 4x3 matrix)
- No engine-mode interactions to dissect (camera modes are orthogonal — see caveats below)

---

## Decompile bodies

### `CameraClass::GetPosition @ 0x261A40` (size 0x22, 34 bytes, 7 callers)

```c
// Prototype: _DWORD *__fastcall(__int64 camera, _DWORD *out_xyz)
// Reads X/Y/Z from the camera's transform-matrix pointer at CameraClass+0x40.
_DWORD *__fastcall sub_140261A40(__int64 a1, _DWORD *a2)
{
  _DWORD *result;
  int v3, v4;

  result = *(_DWORD **)(a1 + 64);   // a1+0x40 → matrix pointer
  v3      = result[7];               // Y = matrix[7]  (byte offset 0x1C)
  v4      = result[11];              // Z = matrix[11] (byte offset 0x2C)
  *a2     = result[3];               // X = matrix[3]  (byte offset 0x0C)
  a2[1]   = v3;                      // out[1] = Y
  a2[2]   = v4;                      // out[2] = Z
  return result;                     // return the matrix pointer (callers can introspect more)
}
```

**Field-offset finding**: CameraClass holds a pointer to a 4x4 (or 4x3) transform matrix at offset 0x40 (decimal 64). Translation X/Y/Z are at matrix indices [3], [7], [11] = byte offsets 0x0C, 0x1C, 0x2C — column 3 of rows 0-2 in row-major float layout. Standard 4x4 transform matrix pattern.

### `CameraClass::SetTransformMatrix @ 0x261BD0` (size 0x50, 80 bytes, 16 callers)

```c
// Prototype: __int64 __fastcall(__int64 camera, _DWORD *new_matrix)
// new_matrix is a 12-float (4x3 matrix) array; positions at indices [3], [7], [11].
__int64 __fastcall sub_140261BD0(__int64 a1, _DWORD *a2)
{
  // Copy 12 floats (4x3 matrix) into the camera's INLINE matrix at +0x10..+0x40.
  *(_DWORD *)(a1 + 16) = *a2;        // CameraClass+0x10 = matrix[0]   (row 0 col 0)
  *(_DWORD *)(a1 + 20) = a2[1];      // +0x14 = matrix[1]              (row 0 col 1)
  *(_DWORD *)(a1 + 24) = a2[2];      // +0x18 = matrix[2]              (row 0 col 2)
  *(_DWORD *)(a1 + 28) = a2[3];      // +0x1C = matrix[3]  ← X translation
  *(_DWORD *)(a1 + 32) = a2[4];      // +0x20 = matrix[4]              (row 1 col 0)
  *(_DWORD *)(a1 + 36) = a2[5];      // +0x24 = matrix[5]              (row 1 col 1)
  *(_DWORD *)(a1 + 40) = a2[6];      // +0x28 = matrix[6]              (row 1 col 2)
  *(_DWORD *)(a1 + 44) = a2[7];      // +0x2C = matrix[7]  ← Y translation
  *(_DWORD *)(a1 + 48) = a2[8];      // +0x30 = matrix[8]              (row 2 col 0)
  *(_DWORD *)(a1 + 52) = a2[9];      // +0x34 = matrix[9]              (row 2 col 1)
  *(_DWORD *)(a1 + 56) = a2[10];     // +0x38 = matrix[10]             (row 2 col 2)
  *(_DWORD *)(a1 + 60) = a2[11];     // +0x3C = matrix[11] ← Z translation

  // Propagate inline matrix to the pointer-target at +0x40 (called every frame too).
  return sub_140261C20(*(_QWORD *)(a1 + 64));
}
```

**Architectural finding**: CameraClass has TWO matrix representations:
1. **Inline matrix** at +0x10..+0x40 (12 floats, 4x3 layout) — written by SetTransformMatrix.
2. **Matrix pointer** at +0x40 — points to a richer per-frame structure (4x4 with view+projection?) — read by GetPosition.

`sub_140261C20` (the propagation callee) presumably copies the inline matrix into the pointer-target's translation column. This decoupling is why SetTransformMatrix is the **canonical** setter — it touches both halves consistently.

### Field offsets confirmed

| Offset | Type | Meaning | Source |
|---|---|---|---|
| `CameraClass+0x10` (`+16`) | `float[12]` | Inline 4x3 transform matrix (row-major) | SetTransformMatrix decompile |
| `CameraClass+0x10 + 0x0C` (`+0x1C`) | `float32` | X translation (inline matrix `[3]`) | SetTransformMatrix line 4 |
| `CameraClass+0x10 + 0x1C` (`+0x2C`) | `float32` | Y translation (inline matrix `[7]`) | SetTransformMatrix line 8 |
| `CameraClass+0x10 + 0x2C` (`+0x3C`) | `float32` | Z translation (inline matrix `[11]`) | SetTransformMatrix line 12 |
| `CameraClass+0x40` (`+64`) | `pointer → 4x4 matrix` | Per-frame view-matrix pointer | GetPosition decompile |
| `*(CameraClass+0x40)[3] / [7] / [11]` | `float32` | X/Y/Z translation in pointed-to matrix | GetPosition decompile |

### New findings for the ledger

Two new entries to append in iter 240 close-out:

1. **`struct_camera_inline_matrix`**: CameraClass+0x10..+0x40 = inline 4x3 transform matrix (12 floats, row-major). Translation column at indices [3], [7], [11].
2. **`struct_camera_matrix_pointer`**: CameraClass+0x40 = pointer to 4x4 view matrix (richer than inline). GetPosition reads X/Y/Z from this pointer at indices [3], [7], [11].

Both are 1-tool (IDA) findings; will use binary-fingerprint identity for 2nd/3rd tool consensus (matches `struct_player_credit_cap` iter-235 pattern).

---

## Design decision matrix

The wire can be one of three shapes:

| Shape | Implementation | Pros | Cons | Verdict |
|---|---|---|---|---|
| **MinHook detour at SetTransformMatrix** | Hook the function, intercept callers, modify translation | Operator can override translation independently of camera animation | Camera animation pipeline calls SetTransformMatrix every frame; intercepting causes camera jitter. Fights the animation system. | **REJECT** |
| **Direct call to SetTransformMatrix from bridge** | `Lua_SetCameraPos(x,y,z)` reads current matrix via GetPosition, copies rotation, sets new translation, calls SetTransformMatrix | Single-shot setter. No frame-by-frame fighting. Operator clicks → camera teleports. | One-shot effect — animation pipeline immediately overwrites unless operator pauses cinematic. Mitigation: pair with iter-145 cinematic camera "Set_Key" path or with iter-208 `Lock_Controls` to freeze input. | **CHOSEN** |
| **Lua DoString wrapping engine `Cam_To_Pos` Lua API** | If engine Lua API exists, wrap via DoString | Cleanest if it exists | Per iter-106/107 RE: engine has no `Free_Cam` Lua, only `Scroll_Camera_To` (which takes userdata). No raw-coords engine Lua API exists. | **N/A — no Lua API exists** |

**Why direct-call works here while iter-96/iter-225/iter-231 needed MinHook**:
- iter-96 (Damage), iter-225 (FireRate), iter-231 (Credits): operator wants the engine to scale these *every time the engine reads them*. MinHook detour is the right shape.
- iter-236 (CameraPos): operator wants a single one-shot teleport. Direct C++ call to SetTransformMatrix is the right shape.
- iter-100 SetSpeedOverride: same pattern as iter-236 (direct call, no detour). Iter-100 was the first proof this works for one-shot setters.

**Pattern lesson**: choose detour vs direct-call based on whether the operator wants *every-frame override* (detour) or *one-shot mutation* (direct call).

---

## Implementation pseudocode

```cpp
// In lua_bridge.cpp
// Direct-call wrapper, NO MinHook hook needed — SetTransformMatrix is a clean
// vftable-stable engine function with 4-tool consensus.
typedef __int64 (__fastcall *pfn_SetTransformMatrix)(__int64, void*);
typedef void* (__fastcall *pfn_GetPosition)(__int64, float*);

static int Lua_SetCameraPos(lua_State* L) {
    double x = fn_tonumber(L, 1);
    double y = fn_tonumber(L, 2);
    double z = fn_tonumber(L, 3);

    // Locate active camera. In tactical mode, this is GameModeRoot.camera.
    // Need to walk: g_base + GameModeRoot_Global + 0x18 → GameModeClass*
    //              GameModeClass+<some offset> → CameraClass*
    // Iter 237 will pin the exact offset path via callgraph re-audit.
    __int64 camera = LookupActiveCamera();
    if (!camera) {
        fn_pushstring(L, "ERR: no active camera (not in tactical mode?)");
        return 1;
    }

    // Read current matrix (uses GetPosition's pointer chain to find matrix source).
    // We need the FULL 4x3 matrix to preserve rotation, not just X/Y/Z.
    // The inline matrix at CameraClass+0x10 is the source of truth.
    float matrix[12];
    memcpy(matrix, reinterpret_cast<float*>(camera + 0x10), 12 * sizeof(float));

    // Update translation column (indices [3], [7], [11]).
    matrix[3]  = static_cast<float>(x);
    matrix[7]  = static_cast<float>(y);
    matrix[11] = static_cast<float>(z);

    // Call SetTransformMatrix to write back + propagate to pointer-target.
    auto fn = reinterpret_cast<pfn_SetTransformMatrix>(g_base + RVA::CameraSetTransformMatrix);
    fn(camera, matrix);

    Log("[Bridge] SetCameraPos(%.2f, %.2f, %.2f) -- LIVE\n", x, y, z);
    fn_pushstring(L, "OK: camera teleported (LIVE -- SetTransformMatrix direct call)");
    return 1;
}

static int Lua_GetCameraPos(lua_State* L) {
    __int64 camera = LookupActiveCamera();
    if (!camera) {
        fn_pushnumber(L, 0); fn_pushnumber(L, 0); fn_pushnumber(L, 0);
        return 3;
    }
    float xyz[3];
    auto fn = reinterpret_cast<pfn_GetPosition>(g_base + RVA::CameraGetPosition);
    fn(camera, xyz);
    fn_pushnumber(L, xyz[0]);
    fn_pushnumber(L, xyz[1]);
    fn_pushnumber(L, xyz[2]);
    return 3;  // returns 3 values: x, y, z
}
```

**Note**: `Lua_GetCameraPos` already exists in the catalog (`SWFOC_GetCameraPos` is Phase2HookPending-mirror). Iter 237 will flip it to LIVE alongside the new SetCameraPos.

### Engine semantic caveats

| Caveat | Behavior |
|---|---|
| **One-shot effect** | Operator click teleports camera once. Animation pipeline (cinematic camera, follow-camera, scrolling) overwrites within 1 frame unless paused. |
| **Pair with cinematic mode** | For sustained position override, operator should: (1) iter-145 StartCinematicCamera, (2) iter-236 SetCameraPos(x,y,z), (3) iter-145 EndCinematicCamera. |
| **Pair with controls lock** | iter-208 SWFOC_LockControls pauses player input → SetCameraPos sticks for the lock duration. |
| **Galactic vs tactical** | LookupActiveCamera must walk the GameModeRoot chain to the *current* camera (galactic camera vs tactical camera have different addresses). Iter 237 pins the chain. |
| **Coordinate system** | Tactical: world coords (engine units, ~1 unit = 1 meter for typical AT-AT scale). Galactic: starmap coords. Operator must know which mode they're in — tooltip should warn. |
| **No clamp** | Camera can be moved arbitrarily far. Going below terrain or far above doesn't crash but renders nothing useful. Document in catalog rationale. |

---

## Implementation outline (iter 237-240)

### Iter 237 — Bridge LIVE wire shipped (~25 LoC)

- `swfoc_lua_bridge/rvas.h`: add `RVA::CameraSetTransformMatrix = 0x261BD0` + `RVA::CameraGetPosition = 0x261A40` constants (rename existing if needed).
- `swfoc_lua_bridge/lua_bridge.cpp`: pseudocode block above.
  - **`LookupActiveCamera()` helper** is the gating dependency — needs to walk `GameModeRoot_Global → +0x18 (GameModeClass*) → +<offset> (CameraClass*)`. Find via decompiling GetPosition's callers (7 sites) — at least one is the per-frame engine update which dereferences the active camera through the same chain.
- 2 Lua functions: `Lua_SetCameraPos(x, y, z)` + flip existing `Lua_GetCameraPos` to LIVE-backed via SetTransformMatrix path.
- `CapabilityStatusCatalog`: 1 NEW Live entry (`SWFOC_SetCameraPos`); flip 1 existing entry from Phase2HookPending → Live (`SWFOC_GetCameraPos`).
- Bridge harness 1100/0 GREEN. DLL + replay rebuilt clean.
- **+1-2 LIVE flips. 147 → 148-149 LIVE wires.**

### Iter 238 — Simulator handlers + 4-6 pin tests

- `tests/SwfocTrainer.Tests/Simulator/SwfocSimulator.cs`: 2 `Reg()` handlers + Set/Get methods.
- `tests/SwfocTrainer.Tests/Simulator/FakeGameState.cs`: `float[3] CameraPosition { get; set; } = {0,0,0};` field.
- 4-6 test pin file `Iter238SetCameraPosSimulatorTests.cs` (catalog Live + iter-236 cross-references + FakeGameState reflection + simulator round-trip Set→Get + 3-coord independence test).

### Iter 239 — Camera & Debug tab native UX

- Camera & Debug tab already has buttons from iter 148-149. Add new "Set camera position (raw coords)" GroupBox with 3 TextBoxes (X, Y, Z) + Apply (LIVE) + Read (LIVE).
- Mirrors iter-227 / iter-233 native UX shape.
- ~6 test pin file.

### Iter 240 — Live verify + close

- Bridge harness + verifier lint clean.
- Append `struct_camera_inline_matrix` + `struct_camera_matrix_pointer` ledger entries.
- HISTORY.md update with the 5-iter arc summary.
- STATUS.md master-loop update.
- Iter 241 = operator changelog (mirrors iter 229 + iter 235).

---

## Risks + open questions

1. **`LookupActiveCamera` chain might differ between game modes**: tactical-mode CameraClass vs GalacticCameraClass (`rva_galactic_camera_ctor` exists at 0x4565). Iter 237 must handle both, OR scope the wire to tactical-only with explicit error in galactic mode.

2. **Animation pipeline overwrites**: documented above. Tooltip must warn operator. **Mitigation suggestion in iter 237**: pair the wire with an explicit "Pause camera animation" toggle (could be a future LIVE wire — requires another RE pass on the camera update loop).

3. **Coordinate system confusion**: tactical world coords are ~1 unit = 1 meter at typical scale; operator types `1000, 0, 100` and gets a useful map view. Galactic uses starmap coords (different scale). Tooltip should sample-document with: "Tactical example: (X=1000, Y=0, Z=100) for above-AT-AT view".

4. **Camera under terrain**: no engine clamp. Document in catalog rationale.

5. **Pattern parity check vs iter-100 SetSpeedOverride**:
   - iter-100: direct call to `SetSpeedOverride @ 0x3A8C90` taking `(unit, mult)` → writes a unit-state field.
   - iter-236: direct call to `SetTransformMatrix @ 0x261BD0` taking `(camera, matrix[12])` → writes inline matrix + propagates.
   - **Same shape**: bridge does NOT install MinHook. Bridge directly calls the engine function with marshaled args.

6. **Performance**: zero overhead. No detour, no hooks, no per-frame interception. The wire fires only when the operator clicks the button.

---

## Iter 236 close-out

- This document is the iter 236 deliverable.
- No bridge / dispatcher / VM / XAML / test changes. Pure RE + design doc.
- 109 → 109 buttons UNCHANGED. 115 → 115 native UX UNCHANGED.
- Verifier ledger lint untouched (no new entries this iter — they land in iter 240 close-out).
- Iter 237 task creation queued at end of iter 236 close-out.

**Pattern lesson reinforced (3rd canonical 5-iter arc this session)**: A1.x deferred sub-tasks all follow `~1 RE-design + ~3-4 implementation iters + ~1 verify+close` shape, regardless of whether the wire uses MinHook detour (iter 224-228, iter 230-234) or direct-call (iter 236-240). The shape is invariant to implementation strategy.
