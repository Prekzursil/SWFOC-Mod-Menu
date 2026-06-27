# deploy_and_launch.ps1 — deploy the latest bridge DLL and (optionally) launch SWFOC.
#
# Usage:
#   .\deploy_and_launch.ps1                 # deploy + verify (no game launch)
#   .\deploy_and_launch.ps1 -Launch         # deploy + launch StarWarsG.exe
#   .\deploy_and_launch.ps1 -DryRun         # show what would happen
#   .\deploy_and_launch.ps1 -Restore        # restore the most recent .bak.preLiveSmoke
#
# Always creates a timestamped backup before overwriting. Safe to re-run.

[CmdletBinding()]
param(
    [switch]$Launch,
    [switch]$DryRun,
    [switch]$Restore
)

$ErrorActionPreference = "Stop"

# 1. Locate the game folder (Steam library autodetect)
$libraryFoldersVdf = "C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf"
if (-not (Test-Path -LiteralPath $libraryFoldersVdf)) {
    Write-Error "Steam libraryfolders.vdf not found. Is Steam installed?"
    exit 1
}

$libraryRoots = [regex]::Matches((Get-Content -LiteralPath $libraryFoldersVdf -Raw),
    '"path"\s+"([^"]+)"') | ForEach-Object { $_.Groups[1].Value.Replace('\\','\') }

$gameDir = $null
foreach ($root in $libraryRoots) {
    $candidate = Join-Path $root "steamapps\common\Star Wars Empire at War\corruption"
    if (Test-Path -LiteralPath (Join-Path $candidate "StarWarsG.exe")) {
        $gameDir = $candidate
        break
    }
}

if (-not $gameDir) {
    Write-Error "SWFOC corruption folder not found in any Steam library. Install / locate StarWarsG.exe first."
    exit 1
}

Write-Output "Game folder: $gameDir"

$deployedDll = Join-Path $gameDir "powrprof.dll"
$builtDll    = "C:\Users\Prekzursil\Downloads\swfoc_memory\swfoc_lua_bridge\powrprof.dll"

if (-not (Test-Path -LiteralPath $builtDll)) {
    Write-Error "Built bridge DLL not found at $builtDll. Run swfoc_lua_bridge/build.bat first."
    exit 1
}

# 2. Restore mode: roll back to most recent .bak.preLiveSmoke
if ($Restore) {
    $backups = Get-ChildItem -LiteralPath $gameDir -Filter "powrprof.dll.bak.preLiveSmoke_*" |
               Sort-Object LastWriteTime -Descending
    if ($backups.Count -eq 0) {
        Write-Error "No preLiveSmoke backups found in $gameDir"
        exit 1
    }
    $latest = $backups[0]
    Write-Output "Restoring from: $($latest.Name) ($($latest.Length) bytes)"
    if ($DryRun) { Write-Output "(dry-run; not copying)"; exit 0 }
    Copy-Item -LiteralPath $latest.FullName -Destination $deployedDll -Force
    Write-Output "Restored. Game folder DLL is now the backup contents."
    exit 0
}

# 3. Compare hashes to detect already-deployed-latest
$builtHash    = (Get-FileHash -LiteralPath $builtDll    -Algorithm SHA256).Hash
$deployedHash = if (Test-Path -LiteralPath $deployedDll) {
                    (Get-FileHash -LiteralPath $deployedDll -Algorithm SHA256).Hash
                } else { "<none>" }

Write-Output "Built bridge:    $((Get-Item $builtDll).Length) bytes  $((Get-Item $builtDll).LastWriteTime)"
Write-Output "Deployed bridge: $(if (Test-Path $deployedDll) { (Get-Item $deployedDll).Length.ToString() + ' bytes  ' + (Get-Item $deployedDll).LastWriteTime } else { '(missing)' })"

if ($builtHash -eq $deployedHash) {
    Write-Output "Already up to date (SHA256 match). Skipping deploy."
} else {
    # 4. Backup + deploy
    $stamp      = Get-Date -Format 'yyyy-MM-dd_HHmmss'
    $backupName = "powrprof.dll.bak.preLiveSmoke_$stamp"
    $backupPath = Join-Path $gameDir $backupName

    if ($DryRun) {
        Write-Output "(dry-run) would backup deployed -> $backupName"
        Write-Output "(dry-run) would copy $builtDll -> $deployedDll"
    } else {
        if (Test-Path -LiteralPath $deployedDll) {
            Copy-Item -LiteralPath $deployedDll -Destination $backupPath
            Write-Output "Backup created: $backupName"
        }
        Copy-Item -LiteralPath $builtDll -Destination $deployedDll -Force
        $newHash = (Get-FileHash -LiteralPath $deployedDll -Algorithm SHA256).Hash
        if ($newHash -ne $builtHash) {
            Write-Error "Deploy hash mismatch! Aborting."
            exit 1
        }
        Write-Output "Deployed. SHA256: $newHash"
    }
}

# 5. Optional launch
if ($Launch) {
    if ($DryRun) {
        Write-Output "(dry-run) would launch StarWarsG.exe"
    } else {
        $exe = Join-Path $gameDir "StarWarsG.exe"
        Write-Output "Launching SWFOC..."
        Start-Process -FilePath $exe -WorkingDirectory $gameDir
        Write-Output "Game launched. Run live_smoke.py once the main menu is reached."
    }
}
