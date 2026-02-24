# SWFOC Editor Program Plan (Ambitious, Safe-Method Track)

## Mission
Build a production-grade trainer/editor for Star Wars: Empire at War / Forces of Corruption that supports:

- Base game (`base_sweaw`, `base_swfoc`)
- AOTR (`aotr_1397421866_swfoc`)
- ROE submod (`roe_3447786229_swfoc`)
- New custom mods through a guided onboarding/calibration flow

The roadmap is intentionally ambitious, but constrained to safe existing runtime methods.

## Safety Boundary (Locked)

### Allowed in this track

- Signature + fallback memory actions already supported by runtime
- Existing code-patch mechanism already in architecture
- Helper-mod hooks declared per profile
- Save decoding/editing/diff/patch workflows
- Process/launch-context detection, diagnostics, and profile metadata evolution

### Disallowed in this track

- New invasive injection architectures beyond current model
- In-game overlay injection
- Kernel/driver anti-cheat bypass techniques
- Any destructive modification of original mod files in-place

## Program Milestones

## M0: Runtime Fidelity and Launch-Context Normalization (Reliability First)

Goal: attach/profile selection and dependency gating behave deterministically across `STEAMMOD`, `MODPATH`, `StarWarsG`, and command-line-inaccessible sessions.

Deliverables:

1. Shared launch-context model and resolver

- `LaunchKind`, `LaunchContext`, `ProfileRecommendation`
- Stable reason-code and confidence output

2. Runtime wiring

- `ProcessLocator` emits `LaunchContext` + legacy metadata bridge
- UI recommendation uses normalized recommendation contract first
- Attach diagnostics include launch kind, normalized modpath, recommendation reason/confidence

3. Dependency validator extraction and hardening

- Dedicated `IModDependencyValidator`
- Workshop roots + local `MODPATH` dependency resolution
- `Pass` / `SoftFail` / `HardFail` behavior with action gating

4. Launch-context detector tool

- `tools/detect-launch-context.py`
- Stable JSON contract for offline diagnostics and fixture testing

5. Metadata contract normalization

- `requiredWorkshopIds`, `requiredMarkerFile`, `dependencySensitiveActions`
- optional `localPathHints`, `localParentPathHints`, `profileAliases`

6. Test matrix

- deterministic parser/recommendation tests
- dependency validation tests
- script fixture smoke tests
- live tests using normalized recommendation semantics

Exit criteria:

- Runtime and script agree on recommendation for fixture cases
- attach succeeds or degrades gracefully with low-confidence fallback
- no attach hard-fail for unresolved dependencies unless metadata contract is unsafe/malformed

## M1: Live Action Command Surface (Gameplay Power Tools)

Goal: deliver robust high-value in-game controls for tactical/campaign workflows with mode-aware safety.

Deliverables:

1. Catalog-driven spawn suite

- preset packs by faction/era/role
- batch spawn operations (count, stagger, formation marker options)
- helper-backed execution where profile requires it

2. Selected-unit inspector + transaction model

- read live matrix (hp, shield, speed, damage multiplier, cooldown, veterancy, owner)
- apply/revert transaction stack
- conflict-safe write semantics + readback policies

3. Tactical bundle

- god/one-hit/fog/timer toggles with explicit tactical-mode gating
- preset “combat state” bundles for rapid QA scenarios

4. Campaign bundle

- credits/resources, planet owner, hero state, build-speed controls
- campaign guardrails by profile feature flags and symbol health

5. Action reliability scoring

- per-action status: stable / experimental / unavailable
- diagnostics exposed in UI and logs

Exit criteria:

- each supported profile has a validated “core command pack”
- unresolved symbols are clearly surfaced and action availability is deterministic

## M2: Save Lab (Full-Schema Editing + Repeatability)

Goal: make save editing reliable, inspectable, and repeatable for advanced workflows.

Deliverables:

1. Full-schema editor hardening

- typed tree/table editor
- schema validation and consistency checks before write

2. Diff/Patch pack system

- export patch packs from save diffs
- import/replay patch packs across compatible saves
- checksum-aware write flow with rollback snapshot

3. Save workflow ergonomics

- targeted search/filter by entity/faction/path
- patch preview and risk hints before apply

Exit criteria:

- round-trip validation on corpus samples
- patch packs apply deterministically across compatible schema targets

## M3: Mod Compatibility Studio (Custom-Mod Onboarding)

Goal: make onboarding a new mod/submod deliberate and fast without expanding invasive methods.

Deliverables:

1. Profile calibration wizard

- bootstrap from closest base profile (`base_swfoc` / `aotr` / `roe`)
- collect launch samples (`STEAMMOD`, `MODPATH`, process host reality)
- author initial metadata contract and aliases

2. Signature calibration flow

- guided scan candidates and confidence ranking
- fallback offset capture where signatures unresolved
- persist calibration notes + evidence bundle

3. Compatibility report card

- per-action support matrix for the new mod profile
- dependency checks and helper hook readiness
- known-risk list before profile promotion

4. Custom-mod profile pack output

- generated profile JSON + schemas/helpers references
- validation report linked to evidence artifacts

Exit criteria:

- new custom mod can be onboarded from bootstrap to first usable action pack via documented wizard flow
- profile evidence package exists for every promoted custom profile

## M4: Distribution and Operational Hardening

Goal: make profile evolution and support operations reliable post-release.

Deliverables:

1. Profile-pack update pipeline

- manifest integrity and rollback support
- version compatibility gates

2. Diagnostics and support bundle

- one-click export of logs, launch-context snapshot, profile info, symbol resolution summary

3. Telemetry-lite (local-first diagnostics)

- action success/failure counters by profile/version
- calibration drift indicators (signature miss/fallback usage trends)

4. Release hardening

- portable package validation
- regression matrix automation for shipped profiles

Exit criteria:

- profile updates are safe and reversible
- support incidents can be triaged from exported diagnostics without re-running user sessions

## Program-Level Test Strategy

1. Deterministic tests

- launch-context parser/recommendation matrix
- dependency validator matrix (workshop + local mod roots)
- profile inheritance and metadata contract tests

2. Live tests

- skip gracefully if target process is absent
- profile selection assertions based on reason code + recommendation output

3. Manual validation tracks

- attach/selection correctness on mixed launch contexts
- critical action smoke runs per shipped profile
- save load integrity after edits

## First-Class Profiles (Current)

- `base_sweaw`
- `base_swfoc`
- `aotr_1397421866_swfoc`
- `roe_3447786229_swfoc`

Custom-mod onboarding extends this set without blocking M0/M1 completion.
