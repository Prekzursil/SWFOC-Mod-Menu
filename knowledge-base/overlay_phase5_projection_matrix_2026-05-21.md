# Overlay Interactive Phase 5 kickoff — projection-matrix RE pass

**Date:** 2026-05-21
**Spec:** `.ralph/specs/overlay-interactive.md` — Phase 5 (click-to-inspect + 3D cursor), spec iter-chain row **iter-297**.
**Hat:** `overlay-interactive`
**Loop iter:** 536 (spec iter-297).
**Predecessor:** `iter296_overlay_phase4_close.md` (Phase 4 close — drag-drop tactical spawning).
**Successor:** spec iter-298 — cursor → world-space conversion.

## Headline

**The projection-matrix RE pass SUCCEEDED — no blocked-items entry
needed.** The spec budgeted iter-297 as "the harder of the two Phase 5
paths" and provided a fallback (`SWFOC_GetUnitAtScreenCoords` new
bridge wire). The fallback is **not** required: a static cross-tool
pass over the existing decompile corpus pinned the engine's global D3D
projection matrix — and, as free evidence from the same function body,
the global view matrix and the view-projection product — all at
**3-tool consensus**. Phase 5's honest defer "Projection matrix RVA
NOT pinned" (spec line 71) is now **CLOSED**.

| What | RVA | Size | Ledger entry |
|---|---|---|---|
| Global D3D **projection** matrix | `0xA6EF24` | 64 B (D3DXMATRIX) | `fact_global_d3d_projection_matrix` |
| Global D3D **view** matrix | `0xA6EEE4` | 64 B (D3DXMATRIX) | `fact_global_d3d_view_matrix` |
| Global D3D **view*projection** matrix | `0xA6F49C` | 64 B (D3DXMATRIX) | `fact_global_d3d_view_projection_matrix` |
| Camera-matrix **build** routine | `0x17F1D0` | 0x32A | `rva_d3d_build_camera_matrices` |

All four entries are `VERIFIED`, `tools_consensus = [binary_ninja, ghidra, ida_pro]`.
Ledger lint after the add: **345 entries, 0 errors / 0 warnings.**

## How it was found

`sub_14017F1D0` (RVA `0x17F1D0`) is the per-camera-update routine that
rebuilds the engine's D3D transform globals. Its body was read
**directly in all three decompilers** — IDA Hex-Rays
(`decompile_corpus/ida_full/full_b36-37.json`), Ghidra
(`ghidra_full/by_addr/0x14017f1d0.json`), Binary Ninja
(`binja_full/by_addr/0x14017f1d0.json`) — and all three agree:

```
if (dword_140A6EBB0 == 0)        // projection-mode flag: 0 = perspective
    D3DXMatrixPerspectiveFovRH(&0xA6EF24, vFov, aspect, zn, zf);
else if (dword_140A6EBB0 == 1)   // 1 = ortho
    D3DXMatrixOrthoRH(&0xA6EF24, w, h, zn, zf);
```

Binary Ninja resolved all five `D3DXMatrixPerspectiveFovRH` arguments
(`&data_140a6ef24, zmm0_2, zmm7, arg4, var_78`); the IDA asm shows
`lea rcx, unk_140A6EF24` immediately before the call, i.e. `0xA6EF24`
is the `D3DXMATRIX* pOut` first argument. Both the perspective and the
ortho branch write the **same** address, confirming `0xA6EF24` is the
single projection-matrix slot.

The same function builds the **view** matrix at `0xA6EEE4` just before
the projection call: it copies the rotation/translation elements from
the camera basis block near `0xA6EBF0`, zeroes column-3 elements
`[3]/[7]/[11]`, and sets `[15] = 1.0` (`0x3F800000`). Element `[0]` is
at `0xA6EEE4`; element `[15]` at `0xA6EF20`; the projection matrix
begins immediately after at `0xA6EF24` — the two matrices are stored
**contiguously**.

Three small accessor functions confirm the identification independently:

| Accessor | RVA | Behaviour |
|---|---|---|
| projection getter | `0x17F7B0` | `return &0xA6EF24;` (8-byte function) |
| inverse-view getter | `0x17F7C0` | `D3DXMatrixInverse(&0xA6F31C, 0, &0xA6EEE4)` behind dirty flag `byte_140A6EB62` |
| view-projection getter | `0x17F810` | `D3DXMatrixMultiply(&0xA6F49C, &0xA6EEE4, &0xA6EF24)` behind dirty flag `byte_140A6EB68` |

These back the engine's `EngineParam_Get_D3d_Projection_Transform` /
`EngineParam_Get_D3d_View_Projection_Transform` shader-parameter
classes (RTTI names observed at `sub_1401ADCF0` / `sub_1401ADDF0` /
`sub_1401ADFF0`).

## Memory map (StarWarsG.exe, image base 0x140000000)

```
0xA6EBB0   dword   projection-mode flag   (0 = perspective, 1 = ortho)
0xA6EBB4   float   candidate FOV input    (feeds tanf -> vertical FOV)
0xA6EBB8   float   candidate aspect input (perspective divide)
0xA6EEE4   D3DXMATRIX  global VIEW matrix             (64 B)
0xA6EF24   D3DXMATRIX  global PROJECTION matrix       (64 B)  <-- spec iter-297 target
0xA6F31C   D3DXMATRIX  cached INVERSE-VIEW matrix     (64 B, lazy)
0xA6F49C   D3DXMATRIX  cached VIEW*PROJECTION matrix  (64 B, lazy)
```

All RVAs are relative to image base `0x140000000`. At runtime add the
RVA to the live base of `StarWarsG.exe`.

## What this hands to iter-298 (cursor -> world-space conversion)

The Phase 5 honest defer is closed; iter-298 no longer needs a new
bridge wire for the projection side. The conversion recipe:

1. Read viewport `(vw, vh)` (overlay already has the host window size).
2. Cursor `(sx, sy)` -> NDC: `nx = 2*sx/vw - 1`, `ny = 1 - 2*sy/vh`
   (D3D NDC y is up).
3. Read the global **view** (`0xA6EEE4`) and **projection**
   (`0xA6EF24`) matrices — OR read the pre-multiplied **view*projection**
   (`0xA6F49C`) directly.
4. Invert `view*projection`; transform two clip-space points
   (`z_ndc = 0` near, `z_ndc = 1` far — D3D depth range) to world space;
   the pair defines the world-space pick ray.
5. **Handedness:** the main render path uses `D3DXMatrix*RH` — iter-298
   must use **right-handed** unprojection conventions. (The separate
   camera setup at `sub_1401AB760` uses `LookAtLH`; that is a different
   sub-camera, not the main render path — do not mix them.)

For the **2D Z=0 plane interim** (the spec's Phase 4/5 fallback if a
full 3D pick is deferred), intersect the world ray with the `z = 0`
ground plane — exactly the plane Phase 4 drag-drop spawning already
uses, so a cursor pick and a drag-drop drop resolve to the same world
coordinate system.

The remaining Phase 5 defer is unchanged: **cursor-hit-unit detection**
still has no exposed engine wire — Mitigation A (client-side raycast
over per-unit AABBs from the extended `HudSnapshot`, spec iter-302)
stands.

## Verification (guardrail 1002)

| Gate | Result |
|---|---|
| Ledger lint (`python -m verifier lint`) | **345 entries, 0 errors / 0 warnings** |
| `VERIFIED_RVAS.md` regenerated | 309 entries; the 4 new RVAs present |
| Bridge harness | n/a — no bridge surface touched; inherits **1100 / 0** |
| Editor test suite | n/a — no editor surface touched; inherits green |
| Overlay DLL | n/a — no `swfoc_overlay/**` source touched; DLL byte-identical to iter-535 (1,094,656 B) |

This iter is a **RE + ledger + docs** iter: it edits
`knowledge-base/verified_facts.json`, regenerates
`knowledge-base/VERIFIED_RVAS.md`, and writes this doc. No overlay
source, no bridge surface, no editor surface — the overlay DLL is
unchanged.

## What's next — Phase 5 continues

- **iter-298** — cursor → world-space conversion: implement the recipe
  above (cursor + viewport + view `0xA6EEE4` + projection `0xA6EF24`
  → world ray). Plumbing-only iter; no UI.
- **iter-299** — cursor-hit-unit detection (client-side raycast proxy).
- **iter-300** — click-to-inspect overlay panel (read-only).
- **iter-301** — inspector action buttons (all 5 wires already LIVE).
- **iter-302** — unit-AABB extension to `HudSnapshot` (append-only;
  also unblocks the live-engine minimap dots deferred at iter-293).
- **iter-303** — Phase 5 close-out.
