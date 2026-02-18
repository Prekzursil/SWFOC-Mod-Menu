# Symbol Calibration TODO (x64 Steam)

This file tracks which runtime symbols are known-good on the 64-bit Steam build
of `swfoc.exe` and which still need calibration/verification.

Notes:
- "RVA" means `address - moduleBase` for `swfoc.exe` main module.
- We prioritize `Signature` resolution; fallback RVAs are only a safety net.
- Mods from Workshop do not change `swfoc.exe`, so symbol addresses should be
  identical across `base_swfoc`, `aotr_1397421866_swfoc`, and `roe_3447786229_swfoc`.
- `StarWarsG.CT` extraction outputs are tracked in `docs/CHEATTABLE_INTEL.md` and
  `artifacts/cheattable_intel/starwarsg.intel.json`.

## Base FoC / AOTR / ROE (x64 Steam `swfoc.exe`)

### Core Globals
- [x] `credits` (Int32): Calibrated fallback to `RVA 0xA60E48` (`addr 0x7FF76D700E48` in sample run)
  - Previous mapping (`RVA 0x176AE8`) was incorrect (`read_symbol` returned `1210066044` while in-game was `26677`).
  - Two-snapshot narrowing:
    - Snapshot A (`26677`): 29 writable candidates (1 module + 28 heap)
    - Snapshot B (`16227`): 11163 writable candidates (1 module + many heap)
    - Intersection: single address `0x7FF76D700E48` (`RVA 0xA60E48`)
  - Runtime write path for `set_credits` now uses a trampoline hook over the credits conversion site:
    - default payload: `{ "symbol": "credits", "intValue": 1000000, "lockCredits": false }`
    - one-shot mode (`lockCredits=false`) pulses authoritative float write and then releases lock
    - persistent mode (`lockCredits=true`) keeps real float credits locked at target value
    - guarded by unique-pattern resolution + original-byte validation; detach restores bytes and frees code cave
  - Remaining improvement: replace/repair `credits` signature so it resolves by AOB instead of fallback.
- [x] `game_timer_freeze` (Bool/Byte): Signature resolved (RVA `0x15A665`)
- [x] `fog_reveal` (Bool/Byte): Signature resolved (RVA `0x15A664`)
- [x] `ai_enabled` (Bool/Byte): Signature resolved (RVA `0x15A488`)

### Campaign / Economy
- [x] `instant_build` (Float): Signature resolved (RVA `0x104F6C`)
- [x] `planet_owner` (Int32): Signature resolved (RVA `0x173DD0`)
- [ ] `hero_respawn_timer` (Int32): Signature resolves + reads `15` (RVA `0x152070`)
  Next: verify in-game semantics (does changing it actually change hero wound/respawn time?)

### Tactical / Selected Unit Matrix
- [x] `selected_hp` (Float): Signature resolved (RVA `0x15B5B4`)
- [x] `selected_shield` (Float): Signature resolved (RVA `0x15B970`)
- [x] `selected_speed` (Float): Signature resolved (RVA `0x15B9B0`)
- [x] `selected_damage_multiplier` (Float): Signature resolved (RVA `0x15B9F0`)
- [x] `selected_cooldown_multiplier` (Float): Signature resolved (RVA `0x15F710`)
- [x] `selected_veterancy` (Int32): Signature resolved (RVA `0x15B9B8`)
- [x] `selected_owner_faction` (Int32): Signature resolved (RVA `0x15B9F8`)

### Tactical Toggles
- [ ] `tactical_god_mode` (Bool/Byte): Signature resolves (RVA `0x150678`)
  Next: verify in-game effect (invulnerability) in an actual tactical battle.
- [ ] `tactical_one_hit_mode` (Bool/Byte): Signature resolves (RVA `0x15A666`)
  Next: verify in-game effect (damage one-hit) in an actual tactical battle.

## Engineering TODOs (To Avoid Regressions)
- [x] Add fallback-offset range validation in `SignatureResolver` so offsets outside module bounds are ignored.
- [ ] In UI/action layer: hide/disable actions when required symbols are missing or unresolved.
- [ ] Add a "Calibrate symbol" debug panel that prints candidate RIP-relative patterns (based on the existing smoke-test scanners).
- [ ] Add optional patch-mode fallback action for fog/maphack using CT-derived branch-bypass AOBs when symbol toggles regress.
- [ ] Evaluate a patch-mode unit-cap feature (`future:set_unit_cap`) from CT patterns, while keeping profile-driven memory actions as primary path.

## Live Checklist Template (Issue #19)

Use this evidence template for each live run:

- Date:
- Build:
- Profile:
- Launch recommendation: `profileId`, `reasonCode`, `confidence`
- Attach summary: symbol healthy/degraded/unresolved counts
- Tactical toggles smoke:
  - `toggle_tactical_god_mode`
  - `toggle_tactical_one_hit_mode`
- Hero-state helper smoke:
  - AOTR: `set_hero_state_helper`
  - ROE: `toggle_roe_respawn_helper`
- Notes / regressions:

### Standardized Evidence Payload (Required)

When posting closure evidence for `#19`/`#34`, include:

- `runId`
- `classification` (`passed|skipped|failed|blocked_environment|blocked_profile_mismatch`)
- `profileId`
- launch recommendation (`reasonCode`, `confidence`)
- runtime mode (`hint`, `effective`, `reasonCode`)
- tactical toggle outcome (`pass|skip|fail` + reason)
- helper workflow outcome (`pass|skip|fail` + reason)
- artifact paths:
  - `TestResults/runs/<runId>/repro-bundle.json`
  - `TestResults/runs/<runId>/repro-bundle.md`
  - TRX files used for the run
