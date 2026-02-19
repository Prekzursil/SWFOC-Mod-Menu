# External Tools Setup

This repository can augment internal reliability tooling with external quality signals.

## Secrets

Configure these repository secrets in GitHub:

- `SONAR_TOKEN`: token for SonarCloud scanning.
- `APPLITOOLS_API_KEY`: optional key for visual audit uploads.

## SonarCloud

1. Create SonarCloud project for this repository.
2. Ensure project key in `sonar-project.properties` matches SonarCloud configuration.
3. Push/PR runs trigger `.github/workflows/sonarcloud.yml`.

## Applitools (optional)

1. Capture visual pack artifacts per `docs/VISUAL_AUDIT_RUNBOOK.md`.
2. Use workflow `.github/workflows/visual-audit.yml` to produce compare artifacts.
3. If key exists, publish selected captures to Applitools for team review.

## jscpd duplication detection

- Config: `.jscpd.json`
- Workflow: `.github/workflows/duplication-check.yml`
- Report artifact: `jscpd-report`

## GitHub Releases (Distribution Channel)

- Workflow: `.github/workflows/release-portable.yml`
- Uses built-in `GITHUB_TOKEN` for release publish.
- Produces:
  - `SwfocTrainer-portable.zip`
  - `SwfocTrainer-portable.zip.sha256`
- Operational instructions: `docs/RELEASE_RUNBOOK.md`

## Native Extender Toolchain (vNext)

The native bridge/extender tree is under `native/` and supports both WSL and Windows build paths.

### WSL path (preferred for this shell)

1. Bootstrap toolchain:
```bash
bash tools/native/bootstrap-wsl-toolchain.sh
```

2. Build:
```bash
bash tools/native/build-native.sh
```

If apt install is unavailable (for example sudo password prompts in non-interactive shells), the bootstrap script falls back to a portable local toolchain under `tools/native/.local/`.

### Windows path

Prerequisites:
- `cmake.exe` (resolved by `tools/native/resolve-cmake.ps1`).
- A native compiler toolchain:
  - Visual Studio / Build Tools with VC workload (`Microsoft.VisualStudio.Component.VC.Tools.x86.x64`), or
  - Ninja + compatible C/C++ toolchain configured for CMake.

`tools/native/resolve-cmake.ps1 -AsJson` reports:
- `vsInstancePath`
- `vsProductLineVersion`
- `recommendedGenerator` (for example `Visual Studio 18 2026` or `Visual Studio 17 2022`)

1. Resolve cmake:
```powershell
pwsh ./tools/native/resolve-cmake.ps1
```

2. Build (Auto mode prefers Windows, then falls back to WSL):
```powershell
pwsh ./tools/native/build-native.ps1 -Mode Auto -Configuration Release
```

3. Force Windows-only path:
```powershell
pwsh ./tools/native/build-native.ps1 -Mode Windows -Configuration Release
```

`build-native.ps1` auto-selects the Visual Studio generator from installed VS major and passes `CMAKE_GENERATOR_INSTANCE` from `vswhere` when available.

If Windows-only configure fails with `could not find any instance of Visual Studio`, install VC Build Tools or run `-Mode Auto`/`-Mode Wsl`.

### Native host binary

The bridge host executable target is `SwfocExtender.Host` and listens on named pipe `SwfocExtenderBridge` by default.
Override with environment variable:

```powershell
$env:SWFOC_EXTENDER_PIPE_NAME = "SwfocExtenderBridge"
```
