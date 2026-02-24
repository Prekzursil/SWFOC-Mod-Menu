# Tests Agent Contract

## Purpose

Ensure deterministic tests remain authoritative while live tests remain explicit and auditable.

## Scope

Applies to files under `tests/`.

## Required Evidence

1. Deterministic tests for new behavior.
2. Live tests must skip with explicit reason context when prerequisites are absent.
3. No pass-by-return in live tests.

## Test Rules

1. Keep deterministic CI filter valid.
2. Add fixture-based tests for tooling contracts and schemas.
3. Runtime reliability changes require both unit coverage and bundle-level diagnostics checks.
