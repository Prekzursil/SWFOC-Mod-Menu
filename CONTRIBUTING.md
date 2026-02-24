# Contributing

Thanks for contributing to SWFOC-Mod-Menu.

## Ground Rules

- Runtime target is Windows.
- Keep changes profile-driven and evidence-backed.
- Do not commit large local mod mirrors or generated build artifacts.
- Prefer additive, reversible changes.

## Branch Naming

Use one of:

- `feature/<short-description>`
- `fix/<short-description>`
- `chore/<short-description>`
- `docs/<short-description>`

## Commit Style

Recommended:

- `feat: ...`
- `fix: ...`
- `chore: ...`
- `docs: ...`
- `test: ...`

## Development Commands

```powershell
dotnet restore SwfocTrainer.sln
dotnet build SwfocTrainer.sln -c Release
```

Deterministic tests:

```powershell
dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj `
  -c Release --no-build `
  --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"
```

Optional live tests require a running target process.

## Pull Request Requirements

Each PR should include:

1. Scope summary and rationale.
2. Risk notes (runtime safety / profile compatibility).
3. Evidence of validation:

- deterministic test output and/or
- live/manual notes with exact profile + build context.

4. Any metadata/profile schema changes called out explicitly.

## Runtime Reliability Changes

If touching signatures, fallbacks, or runtime attach logic:

- include before/after behavior
- include launch-context evidence (`reasonCode`, `confidence`, `launchKind`)
- record whether change impacts base, AOTR, ROE, or custom mods
- include `runId` and repro bundle evidence (`repro-bundle.json`, `repro-bundle.md`) or a justified skip note.

## Issue Workflow

- Use issue templates for bug/feature/calibration work.
- Link PRs to issues and milestones.

## Plan Archive Workflow

- `(new)codex(plans)/` is intentionally tracked as versioned planning history.
- Keep plan files additive and include execution evidence once implemented.
- Use `TODO.md` and milestone issues as the authoritative execution board.
