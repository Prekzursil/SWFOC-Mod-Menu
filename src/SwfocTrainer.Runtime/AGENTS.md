# Runtime Agent Contract

## Purpose
Protect runtime attach/memory workflows with deterministic diagnostics and fail-safe behavior.

## Scope
Applies to files under `src/SwfocTrainer.Runtime/`.

## Required Evidence
1. Include tests for attach/mode/resolution behavior changes.
2. Include launch-context reason code impact in PR notes.
3. Include repro bundle evidence for live-only fixes.

## Runtime Safety Rules
1. Signature-first; never rely on blind fixed addresses.
2. If runtime confidence is uncertain, block mutating actions.
3. Record actionable reason codes in diagnostics.
4. When both `swfoc.exe` and `StarWarsG.exe` exist for FoC, attach host selection must be deterministic.
