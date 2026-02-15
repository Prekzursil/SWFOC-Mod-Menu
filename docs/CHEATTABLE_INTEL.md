# Cheat Table Intelligence

Source: `StarWarsG.CT`

This is an extracted intelligence summary, not a direct CE table import.
It keeps AOB/patch techniques that are useful for trainer calibration and ignores unrelated table noise.

## Extracted Scripts

| Group | Entry | Technique | Trainer Mapping | Primary AOB |
|---|---|---|---|---|
| Empire At War | Infinite Credits | code_cave_override | set_credits (+ mirror sync) | `F3 0F 2C 50 70 89 57` |
| Empire At War | Maphack | branch_bypass_patch | toggle_fog_reveal (or code-patch fallback) | `80 3C 06 00 74 1A` |
| Empire At War | 1Sec/1Credit Build S | code_cave_override | set_instant_build_multiplier (or patch-mode feature) | `8B 83 04 09 00 00 48` |
| Empire At War | 1Sec/1 Cred Build C | code_cave_override | set_instant_build_multiplier (or patch-mode feature) | `83 BB 78 07 00 00 00` |
| Empire At War | Max Unit Cap 99k | branch_bypass_patch | future:set_unit_cap | `48 8B 74 24 68 8B C7` |
| Forces Of Corruption | Infinite Credits | code_cave_override | set_credits (+ mirror sync) | `F3 0F 2C 50 70 89 57` |
| Forces Of Corruption | Maphack | branch_bypass_patch | toggle_fog_reveal (or code-patch fallback) | `66 83 3C 70 00 74` |
| Forces Of Corruption | 1 Sec/1 Cred Build S | code_cave_override | set_instant_build_multiplier (or patch-mode feature) | `8B 83 FC 09 00 00 48` |
| Forces Of Corruption | 1Sec/1 Cred Build C | code_cave_override | set_instant_build_multiplier (or patch-mode feature) | `83 BB 6C 08 00 00 00 41` |
| Forces Of Corruption | Max Unit Cap 99k | code_cave_patch | future:set_unit_cap | `48 8B 74 24 68 8B C7` |

## Actionable Notes

1. `Infinite Credits` scripts confirm a dual-path flow (`float -> int convert`), matching the trainer's mirror-sync model.
2. `Maphack` scripts are branch-bypass patches, so they are an optional fallback path if symbol-based fog toggles regress.
3. `1 Sec/1 Cred Build` scripts are code-cave overrides with hardcoded values; useful as behavior anchors, not as final trainer behavior.
4. `Max Unit Cap` suggests a future patch-mode feature (`set_unit_cap`) if desired.

## Detailed Entries

### Empire At War / Infinite Credits
- Technique: `code_cave_override`
- Trainer mapping: `set_credits (+ mirror sync)`
- Injection points: `StarWarsG.exe+2FCFC`
- AOB scans:
  - `credits1` on `StarWarsG.exe` with pattern `F3 0F 2C 50 70 89 57`
- Constant writes:
  - `[rax+70] <- (float)1000000`
- Disable restore bytes:
  - `db F3 0F 2C 50 70`
- Notes:
  - Uses code-cave trampoline with immediate writes.

### Empire At War / Maphack
- Technique: `branch_bypass_patch`
- Trainer mapping: `toggle_fog_reveal (or code-patch fallback)`
- Injection points: `StarWarsG.exe+451974`
- AOB scans:
  - `maphack2` on `StarWarsG.exe` with pattern `80 3C 06 00 74 1A`
- Disable restore bytes:
  - `db 80 3C 06 00 74 1A`
- Notes:
  - Branch behavior is bypassed/patched.

### Empire At War / 1Sec/1Credit Build S
- Technique: `code_cave_override`
- Trainer mapping: `set_instant_build_multiplier (or patch-mode feature)`
- Injection points: `StarWarsG.exe+333E73`
- AOB scans:
  - `building1` on `StarWarsG.exe` with pattern `8B 83 04 09 00 00 48`
- Constant writes:
  - `[rbx+00000904] <- (int)1`
  - `[rbx+00000908] <- (int)1`
- Disable restore bytes:
  - `db 8B 83 04 09 00 00`
- Notes:
  - Uses code-cave trampoline with immediate writes.

### Empire At War / 1Sec/1 Cred Build C
- Technique: `code_cave_override`
- Trainer mapping: `set_instant_build_multiplier (or patch-mode feature)`
- Injection points: `build5`
- AOB scans:
  - `building2` on `StarWarsG.exe` with pattern `83 BB 78 07 00 00 00`
- Constant writes:
  - `[rbx+778] <- (int)1`
  - `[rbx+794] <- (int)1`
- Disable restore bytes:
  - `db 83 BB 78 07 00 00 00`
- Notes:
  - Uses code-cave trampoline with immediate writes.

### Empire At War / Max Unit Cap 99k
- Technique: `branch_bypass_patch`
- Trainer mapping: `future:set_unit_cap`
- Injection points: `StarWarsG.exe+28DF6F`
- AOB scans:
  - `maxunit1` on `StarWarsG.exe` with pattern `48 8B 74 24 68 8B C7`
- Disable restore bytes:
  - `db 48 8B 74 24 68`
- Notes:
  - Branch behavior is bypassed/patched.

### Forces Of Corruption / Infinite Credits
- Technique: `code_cave_override`
- Trainer mapping: `set_credits (+ mirror sync)`
- Injection points: `StarWarsG.exe+2FCFC`
- AOB scans:
  - `credits1` on `StarWarsG.exe` with pattern `F3 0F 2C 50 70 89 57`
- Constant writes:
  - `[rax+70] <- (float)1000000`
- Disable restore bytes:
  - `db F3 0F 2C 50 70`
- Notes:
  - Uses code-cave trampoline with immediate writes.

### Forces Of Corruption / Maphack
- Technique: `branch_bypass_patch`
- Trainer mapping: `toggle_fog_reveal (or code-patch fallback)`
- Injection points: `StarWarsG.exe+4C1764`
- AOB scans:
  - `maphack1` on `StarWarsG.exe` with pattern `66 83 3C 70 00 74`
- Disable restore bytes:
  - `db 66 83 3C 70 00 74 1A`
- Notes:
  - Branch behavior is bypassed/patched.

### Forces Of Corruption / 1 Sec/1 Cred Build S
- Technique: `code_cave_override`
- Trainer mapping: `set_instant_build_multiplier (or patch-mode feature)`
- Injection points: `StarWarsG.exe+374413`
- AOB scans:
  - `building1` on `StarWarsG.exe` with pattern `8B 83 FC 09 00 00 48`
- Constant writes:
  - `[rbx+000009FC] <- (int)1`
  - `[rbx+00000A00] <- (int)1`
- Disable restore bytes:
  - `db 8B 83 FC 09 00 00`
- Notes:
  - Uses code-cave trampoline with immediate writes.

### Forces Of Corruption / 1Sec/1 Cred Build C
- Technique: `code_cave_override`
- Trainer mapping: `set_instant_build_multiplier (or patch-mode feature)`
- Injection points: `StarWarsG.exe+400427`
- AOB scans:
  - `build2` on `StarWarsG.exe` with pattern `83 BB 6C 08 00 00 00 41`
- Constant writes:
  - `[rbx+0000086C] <- (int)1`
  - `[rbx+00000890] <- (int)1`
- Disable restore bytes:
  - `db 83 BB 6C 08 00 00 00`
- Notes:
  - Uses code-cave trampoline with immediate writes.

### Forces Of Corruption / Max Unit Cap 99k
- Technique: `code_cave_patch`
- Trainer mapping: `future:set_unit_cap`
- Injection points: `StarWarsG.exe+2AC94F`
- AOB scans:
  - `maxunitcap1` on `StarWarsG.exe` with pattern `48 8B 74 24 68 8B C7`
- Disable restore bytes:
  - `db 48 8B 74 24 68`
- Notes:
  - Uses code-cave trampoline.
