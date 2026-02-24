# Security Alert Triage

This file tracks GitHub code-scanning alert disposition for `SWFOC-Mod-Menu`.

Threat model boundary for this wave:

- Runtime/app paths that can affect normal product execution are treated as production-risk and fixed in code.
- Test fixtures and offline tooling paths are allowed to accept local file arguments by design; those alerts are triaged with explicit rationale.

## Alert Disposition

| Alert # | Rule | Path | Disposition | Notes |
|---|---|---|---|---|
| 1 | `actions/missing-workflow-permissions` | `.github/workflows/release-portable.yml` | fixed | Added explicit `permissions: contents: read`. |
| 2 | `actions/missing-workflow-permissions` | `.github/workflows/ci.yml` | fixed | Added explicit `permissions: contents: read`. |
| 14 | `cs/path-injection` | `src/SwfocTrainer.App/App.xaml.cs` | fixed | App data root now resolved via trusted policy rooted in `LocalApplicationData/SwfocTrainer`. |
| 15 | `cs/path-injection` | `src/SwfocTrainer.Core/Logging/FileAuditLogger.cs` | fixed | Log path now constrained to trusted app root with canonical subpath validation. |
| 16 | `cs/path-injection` | `src/SwfocTrainer.App/ViewModels/MainViewModel.cs` | fixed | Hotkey file read path normalized and constrained under trusted app root. |
| 17 | `cs/path-injection` | `src/SwfocTrainer.App/ViewModels/MainViewModel.cs` | fixed | Hotkey file read path normalized and constrained under trusted app root. |
| 18 | `cs/path-injection` | `src/SwfocTrainer.App/ViewModels/MainViewModel.cs` | fixed | Hotkey file write path constrained under trusted app root. |
| 19 | `cs/path-injection` | `src/SwfocTrainer.App/ViewModels/MainViewModel.cs` | fixed | Hotkey file write path constrained under trusted app root. |
| 20 | `cs/path-injection` | `src/SwfocTrainer.Saves/Services/BinarySaveCodec.cs` | fixed | Save read path now canonicalized + extension validated (`.sav`). |
| 21 | `cs/path-injection` | `src/SwfocTrainer.Saves/Services/BinarySaveCodec.cs` | fixed | Save write path now canonicalized + extension validated; output directory creation is bounded. |
| 22 | `cs/path-injection` | `src/SwfocTrainer.Saves/Services/BinarySaveCodec.cs` | fixed | Roundtrip temp path is canonicalized and constrained under system temp root. |
| 23 | `cs/path-injection` | `src/SwfocTrainer.Saves/Services/BinarySaveCodec.cs` | fixed | Roundtrip temp file deletion occurs only on validated temp path. |
| 24-29 | `cs/path-injection` | `tests/SwfocTrainer.Tests/Runtime/ModDependencyValidatorTests.cs` | dismissed | Test-only code intentionally creates temp directories/files from controlled fixtures; not shipped runtime surface. |
| 30-32 | `cs/path-injection` | `tests/SwfocTrainer.Tests/Saves/SaveCodecTests.cs` | dismissed | Deterministic test harness creates temporary `.sav` files under temp root to validate codec behavior; not runtime user input surface. |
| 3-13 | `py/path-injection` | `tools/*.py` | dismissed | Offline diagnostics/tooling scripts accept operator-supplied file paths by design; no privileged runtime path writes. |

## Revalidation Procedure

1. Run code scanning after merge of this wave.
2. Confirm alerts listed as `fixed` are closed by new analysis.
3. Dismiss test/tool alerts with comments referencing this document and the threat-model boundary above.
