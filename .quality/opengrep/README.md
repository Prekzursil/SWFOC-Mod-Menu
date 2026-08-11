# Curated SAST ruleset (lean gate 4)

Pinned tool: **opengrep 1.22.0** (CI, installed by
`Prekzursil/quality-zero-platform/.github/workflows/reusable-quality.yml`) —
locally interchangeable with **semgrep CE** (opengrep is a fork of semgrep and
consumes the same rule syntax).

## Why an in-repo ruleset instead of `--config auto`

`--config auto` / `p/*` registry packs are fetched from the network at scan time
and change underneath you: a repo that is green today goes red tomorrow with no
change of yours. That externally-refreshing finding-set is exactly the treadmill
the lean charter exists to escape. A fixed, reviewable ruleset committed to the
repo makes the gate deterministic — same result every run, offline, no registry
login — so **zero is both reachable and stable**.

## Languages this repo actually contains

Measured (`git ls-files` over the tracked tree, and
`gh api repos/Prekzursil/SWFOC-Mod-Menu/languages`):

| language   | files | where                                                      | rules |
| ---------- | ----- | ---------------------------------------------------------- | ----- |
| C#         | 398   | `src/` (192), `tests/` (206)                               | 6     |
| Python     | 60    | `tests/` (19), `swfoc_toolkit/` (17), `tools/` (10), `scripts/` (7), `swfoc_lua_bridge/` (3), `bridge/` (3) | 8 |
| C / C++    | 108   | `swfoc_overlay/`, `native/`, `swfoc_lua_bridge/`           | 3     |
| PowerShell | 48    | `tools/`, `bridge/`                                        | 0 — opengrep 1.22.0 does not support PowerShell (`opengrep show supported-languages`); those scripts are out of reach of gate 4 and are noted as a residual, not silently implied covered. |
| Lua        | 5     | `profiles/`, `mods/`, `bridge/`                            | 0 — no `load`/`loadstring`/`dofile`/`os.execute`/`io.popen` exists in them today; a rule with no reachable sink would be decoration. |

`knowledge-base/` (46,165 files, almost all machine-generated RE decompile/data
JSON) carries no code and is scanned only by the 2 language-agnostic secret
rules. It is **not** excluded.

## Contents — 19 rules

- `csharp-security.yaml` — `BinaryFormatter`/`SoapFormatter`/
  `NetDataContractSerializer`/`LosFormatter`; Newtonsoft
  `TypeNameHandling != None`; a TLS validation callback that returns `true`
  unconditionally; MD5/SHA-1; `Process.Start` with a runtime-built command
  string (interpolation / concatenation / `string.Format`); `Assembly.Load*`
  from a computed path.
- `python-security.yaml` — `exec`, `eval`, `subprocess(..., shell=True)`,
  `os.system`/`os.popen`, unsafe `yaml.load`, `pickle`/`marshal` loads,
  md5/sha1, TLS verification disabled.
- `cpp-security.yaml` — unbounded `strcpy`/`strcat`/`sprintf`/`gets` with a
  **non-literal** source, `system`/`popen`/`_wsystem`,
  `tmpnam`/`mktemp`/`tempnam`.
- `general-security.yaml` — committed PEM private key, AWS access key id
  (defence-in-depth alongside gate 5 / gitleaks, a different engine).

### Vendored third-party trees excluded from the C/C++ rules

Named explicitly, and only on the C/C++ rules (mirrors the DevExtreme ruleset's
`vendor/` exclusion):

| path                          | upstream                                            |
| ----------------------------- | --------------------------------------------------- |
| `swfoc_overlay/imgui/**`      | Dear ImGui — MIT, (c) 2014-2024 Omar Cornut         |
| `swfoc_lua_bridge/minhook/**` | MinHook — (c) 2009-2017 Tsuda Kageyu                |

Both carry their upstream `LICENSE.txt` in-tree. This is not used to hide a
first-party hit: with the exclusions removed, the only additional C/C++ matches
would still be zero, because every `strcpy` in this repo (vendored or not) has a
**string-literal** source and the rule already exempts that shape.

The Python rules are deliberately **wider** than `ruff.toml`'s
`extend-exclude`: a security lane should not inherit a style lane's scope, so
`swfoc_toolkit/`, `swfoc_lua_bridge/`, `bridge/` and `ghidra_scripts/` **are**
scanned here.

## Running the gate

```bash
# CI (opengrep, exactly as reusable-quality.yml gate 4 invokes it):
opengrep scan --config .quality/opengrep --error \
  --exclude .venv --exclude node_modules --exclude dist --exclude out --exclude build .

# Local (semgrep CE, rule-compatible):
semgrep scan --config .quality/opengrep --error --metrics off \
  --exclude .venv --exclude node_modules --exclude dist --exclude out --exclude build .
```

Gate passes on **0 findings** (clean-zero lock; no baseline file).
Runtime measured on a native Linux filesystem: **~140-210 s for 46,724 files**.
(On a Windows `/mnt/c` 9p mount the same scan is I/O-bound and did not finish in
26 minutes — do not benchmark it there.)

## Known coverage residuals — disclosed, not implied away

opengrep reports these as `level: warn`. **They do not change the exit code**, so
gate 4 stays green while a rule has silently stopped covering a file. Measured
on this repo, 2026-08-11:

| residual                                                                                            | effect                                                                                             |
| --------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| `Scan skipped: 78 files larger than 1.0 MB` — all in `knowledge-base/`                              | the 2 generic secret rules do not read them. Gate 5 (gitleaks) still scans them with a different engine. |
| `swfoc_lua_bridge/lua_bridge.cpp` → `Memprof_limits.Limit_reached`                                  | the 3 C/C++ rules do not fully analyse the largest native file (~9.9k lines).                       |
| PartialParsing in `swfoc_overlay/overlay.cpp`, `swfoc_lua_bridge/proxy.cpp`, `src/SwfocTrainer.App/ViewModels/MainViewModel.cs`, `src/SwfocTrainer.Runtime/Services/BackendRouter.cs` | opengrep's parser cannot fully parse a construct in each; the rest of each file is still analysed.  |
| An intermittent 5 s rule timeout on `src/SwfocTrainer.Runtime/Services/RuntimeAdapter.cs` (6,187 lines / 230 KB) | load-dependent, not deterministic: 0/5 timeouts when that file is scanned alone (9.4-33 s wall), but it appeared in 2 of 3 whole-repo runs on a loaded machine. It can only *reduce* coverage, never red the gate. |

PowerShell (48 `.ps1` files) has no lane at all — opengrep 1.22.0 does not
support the language.

## Suppressions

Inline, greppable, one per site (`grep -rn nosemgrep`). No `paths: exclude`
entry narrows a rule to hide a first-party hit.

| site                        | rule                            | why                                                                                                                                                                                                       |
| --------------------------- | ------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `swfoc_toolkit/live_test.py` `launch_game()` | `python-subprocess-shell-true` | The argv is a fixed literal list whose only interpolation is the module-level `STEAM_APP_ID` constant, so no untrusted input reaches the shell. **`shell=True` is nonetheless redundant** (the list already invokes `cmd /c`) and should be dropped — recorded as a follow-up, since this harness drives a real game process and cannot be exercised in CI. |

semgrep/opengrep only honour a `nosemgrep` marker on the **same line as the
finding or the line immediately above it** — a reason block ending 4 lines above
does not suppress anything.

## Refreshing against upstream

Upstream registry rules (`p/csharp`, `p/python`, `p/c`, `p/r2c-security-audit`)
are Apache-2.0 / LGPL-2.1; rule logic is reproduced / adapted here. To refresh,
diff the registry packs and port new high-signal rules in one-in-one-out,
re-running the control fixtures below.

## Proving the ruleset can fire (do this on every change)

A ruleset that has never gone red is indistinguishable from an empty file. Both
states must be checked, and the KNOWN-GOOD run's **exit code** matters as much
as its finding count: opengrep exits **2** on a rule parse error while still
printing `0 findings`, so a broken rule reads as "clean" locally and reds the
gate permanently in CI.

```bash
# KNOWN-BAD: must exit 1, and every declared rule id must appear
opengrep scan --config .quality/opengrep --error --json --output bad.json  fixtures/bad/
# KNOWN-GOOD: must exit 0, 0 findings, and 0 entries in .errors
opengrep scan --config .quality/opengrep --error --json --output good.json fixtures/good/
```

Take the fired-rule set from the **JSON** `results[].check_id`, not from the
pretty console output — the console wraps long rule ids and under-reports which
rules fired.

Measured 2026-08-11 with opengrep 1.22.0: 19/19 declared rules fired on the
known-bad fixtures (32 findings, exit 1); known-good fixtures 0 findings, exit 0,
0 engine errors; this repo `Ran 19 rules on 46724 files: 1 finding` before the
suppression above and **0 findings, exit 0** after.

Also check the warning lines (`timeout error`, `Partially scanned`,
`Scan skipped`) on every change — they are the only signal that a rule stopped
covering a file, and they never affect the exit code.
