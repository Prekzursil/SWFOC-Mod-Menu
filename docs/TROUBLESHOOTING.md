# Troubleshooting Guide

This guide helps diagnose and resolve common issues with test scripts, builds, and verification workflows.

## Quick Verification Commands

### Deterministic Test Suite

The deterministic test suite runs without requiring a live SWFOC process. Use this for CI/local validation:

**Option 1: Using Makefile (Recommended)**
```bash
make verify
```

**Option 2: Using Windows batch script**
```cmd
run-deterministic-tests.cmd
```

**Option 3: Direct dotnet test command**
```powershell
dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj `
  -c Release --no-build `
  --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"
```

### Expected Output

**Success scenario:**
```
[Makefile] Running deterministic test suite...
  Determining projects to restore...
  All projects are up-to-date for restore.
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47, Duration: 2.5s
[Makefile] Verification complete.
```

**Build not found scenario:**
```
error: Could not find file '.../bin/Release/net8.0/SwfocTrainer.Tests.dll'
```

**Resolution:** Run `make build` or `dotnet build SwfocTrainer.sln -c Release` first.

## Common Issues and Resolutions

### 1. .NET SDK Version Mismatch

**Symptom:**
```
error NETSDK1045: The current .NET SDK does not support targeting .NET 8.0
```

**Cause:** The installed .NET SDK version doesn't match the version pinned in `global.json`.

**Resolution:**

1. Check required version: `cat global.json`
2. Check installed version: `dotnet --version`
3. Install the required .NET 8.x SDK from <https://dotnet.microsoft.com/download>
4. Restart your terminal/IDE after installation

### 2. Build Artifacts Missing

**Symptom:**
```
Could not find file '.../bin/Release/net8.0/SwfocTrainer.Tests.dll'
```

**Cause:** Tests were run with `--no-build` flag but no build artifacts exist.

**Resolution:**
```bash
# Build first, then run tests
make build
make verify
```

Or run tests without `--no-build`:
```powershell
dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release
```

### 3. Live Tests Running When Not Expected

**Symptom:**
Tests hang or skip with messages like "No SWFOC process detected" or "Live attach failed".

**Cause:** The test filter is not properly excluding live tests.

**Resolution:**
Ensure the filter excludes both `Live` and `RuntimeAttachSmokeTests`:
```powershell
--filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"
```

### 4. Test Failures in CI But Passing Locally

**Symptom:**
Tests pass on your machine but fail in GitHub Actions.

**Common causes:**

- **Path separators:** Use `/` in cross-platform paths or `Path.Combine()`
- **Line endings:** Git autocrlf causing snapshot comparison failures
- **Timezone differences:** Use UTC for timestamp tests
- **File system case sensitivity:** Linux is case-sensitive, Windows is not

**Resolution:**

1. Check `.gitattributes` for proper line ending configuration
2. Run tests in a Linux container locally to reproduce
3. Review test assertions for hardcoded paths or timestamps

### 5. Permission Denied Errors

**Symptom (Windows):**
```
Access to the path '...' is denied
```

**Common causes:**

- File is open in another application (Visual Studio, IDE, antivirus scanner)
- Process hasn't released file handle
- Insufficient permissions

**Resolution:**

1. Close IDEs and file explorers
2. Check for locked processes: `tasklist | findstr SwfocTrainer`
3. Run as Administrator if necessary (though shouldn't be required)
4. Clean build artifacts: `make clean`

### 6. Make Command Not Found

**Symptom (Windows):**
```
'make' is not recognized as an internal or external command
```

**Cause:** GNU Make is not installed or not in PATH.

**Resolution:**

**Option 1:** Use the Windows batch script instead:
```cmd
run-deterministic-tests.cmd
```

**Option 2:** Install Make for Windows:

- Install via Chocolatey: `choco install make`
- Install via Scoop: `scoop install make`
- Install Git Bash (includes Make)
- Add to PATH after installation

**Option 3:** Use Windows Subsystem for Linux (WSL):
```bash
wsl make verify
```

### 7. Test Discovery Failures

**Symptom:**
```
No test is available in ...
```

**Common causes:**

- Test method missing `[Fact]` or `[Theory]` attribute
- Test class not public
- Project not properly built

**Resolution:**

1. Rebuild the solution: `make build`
2. Check that test classes are `public`
3. Verify xUnit attributes are present
4. Clear NuGet cache if needed: `dotnet nuget locals all --clear`

## Verification Workflow Best Practices

### Pre-Commit Verification

Before committing changes:
```bash
# 1. Build
make build

# 2. Run deterministic tests
make verify

# 3. Check git status
git status
```

### Full Build + Test Cycle

```bash
# Clean, build, and verify in one go
make clean && make build && make verify
```

### CI/CD Verification

The GitHub Actions workflow uses the same deterministic test suite:
```yaml
- name: Run deterministic tests
  run: dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"
```

## Live Validation Tests

Live tests require a running SWFOC process and are excluded from deterministic verification.

**Running live tests manually:**
```cmd
run-live-tests.cmd
```

**Or with full evidence collection:**
```powershell
pwsh ./tools/run-live-validation.ps1 `
  -Configuration Release `
  -NoBuild `
  -Scope FULL `
  -EmitReproBundle $true
```

See `docs/LIVE_VALIDATION_RUNBOOK.md` for complete live validation procedures.

## Test Output Interpretation

### Understanding Test Results

```
Passed!  - Failed: 0, Passed: 47, Skipped: 0, Total: 47, Duration: 2.5s
         └─ All tests passed
                    └─ Number of successful tests
                              └─ Tests intentionally skipped
                                        └─ Total test count
                                                  └─ Execution time
```

### Skipped Tests

If tests are skipped, check:

1. Are conditional tests requiring specific environment setup?
2. Is the skip intentional (documented with `[Fact(Skip = "reason")]`)?
3. Review test output for skip reasons

### Failed Tests

When tests fail:

1. Review the test output for the specific assertion that failed
2. Check if recent code changes affected the test
3. Run the individual test with verbose output:
   ```powershell
   dotnet test --filter "FullyQualifiedName~YourTestName" --logger "console;verbosity=detailed"
   ```
4. Review test evidence artifacts if applicable (check `TestResults/`)

## Performance Issues

### Slow Test Execution

If tests are taking longer than expected:

1. Check for unintentional live test inclusion (should complete in < 10s)
2. Profile individual slow tests
3. Verify no antivirus/security software is scanning test assemblies
4. Consider running in Release mode: `-c Release`

### Build Performance

For faster builds during development:
```bash
# Build only what changed
dotnet build SwfocTrainer.sln -c Debug --no-restore
```

## Getting Help

If issues persist:

1. Check existing GitHub Issues for similar problems
2. Review recent commits for breaking changes
3. Ensure all prerequisites are installed (see README.md)
4. Create a new issue with:
   - Full error output
   - Steps to reproduce
   - Environment details (OS, .NET SDK version)
   - Recent changes made

## Related Documentation

- `README.md` - Build and test prerequisites
- `CONTRIBUTING.md` - Development workflow and PR requirements
- `docs/TEST_PLAN.md` - Complete test suite documentation
- `docs/LIVE_VALIDATION_RUNBOOK.md` - Live validation procedures
- `.github/workflows/ci.yml` - CI pipeline configuration
