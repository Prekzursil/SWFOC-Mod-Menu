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

Local verification of the lean gate (runs the QZP preflight at the exact
commit `.github/workflows/quality.yml` pins):

```bash
bash scripts/verify
```

## Measured Coverage Scope (QZP lean gate 3)

The lean gate enforces STRICT 100% line AND branch coverage on each declared
scope. The declared scopes and documented omissions are:

- **Python** (`src/`, `scripts/`, tooling) — measured by the gate's pytest
  lane at 100% line+branch.
- **C++ `swfoc_overlay` header units** — `scripts/coverage-gate.sh` compiles
  every `swfoc_overlay/*_test.cpp` with `g++ --coverage`, runs it, and
  asserts 100% line+branch over the `swfoc_overlay/*.h` headers those tests
  exercise (`coverage/cpp/`).
- **Declared out of the Linux-measured scope** (Win32/MinGW-only code that
  cannot link on the ubuntu runner — reasoned, never a silent pass):
  `swfoc_lua_bridge/` (needs `<windows.h>` + MinHook +
  `x86_64-w64-mingw32-g++`; built and run on Windows by its own `.bat`
  scripts) and the `swfoc_overlay` DLL translation units that need
  ImGui/D3D9/Win32 (`dllmain.cpp`, `overlay.cpp`, `hud_state.cpp`,
  `overlay_action_worker.cpp`; built by `swfoc_overlay/build.bat`). Their
  logic lives in the header units measured above.
- **JS/TS** (`tools/visual-chromatic`) — no test files by design; the gate
  skips it as no-test-surface.

## Pull Request Requirements

Each PR should include:

1. Scope summary and rationale.
2. Risk notes (runtime safety / profile compatibility).
3. Evidence of validation:

- deterministic test output and/or
- live/manual notes with exact profile + build context.

1. Any metadata/profile schema changes called out explicitly.

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
