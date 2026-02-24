# Tooling Agent Contract

## Purpose

Tooling must produce reproducible, machine-readable evidence for mod/runtime triage.

## Scope

Applies to files under `tools/`.

## Required Evidence

1. Every tooling script change must include a deterministic fixture or smoke command.
2. Script outputs must be stable and schema-backed where practical.
3. Failure conditions must use explicit classification codes.

## Reliability Rules

1. Per-run output isolation under `TestResults/runs/<runId>/`.
2. Missing artifacts are explicit failures, never implicit pass.
3. Keep Windows live target assumptions explicit in docs and script help.
