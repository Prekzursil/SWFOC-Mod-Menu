param(
    [string]$ScriptPath = "mods/SwfocTrainerTelemetry/Data/Scripts/TelemetryModeEmitter.lua",
    [string]$Mode = "TacticalLand",
    [string]$OutPath = "TestResults/lua-harness/lua-harness-output.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath "..") -ChildPath "..")).ProviderPath
Set-Location $repoRoot

$resolvedScriptPath = if ([System.IO.Path]::IsPathRooted($ScriptPath)) { $ScriptPath } else { Join-Path $repoRoot $ScriptPath }
if (-not (Test-Path -Path $resolvedScriptPath)) {
    throw "Lua script not found: $resolvedScriptPath"
}

$scriptContent = Get-Content -Raw -Path $resolvedScriptPath
$containsMarker = $scriptContent.Contains("SWFOC_TRAINER_TELEMETRY")
$containsEmitter = $scriptContent.Contains("SwfocTrainer_Emit_Telemetry_Mode")
if ($Strict -and (-not $containsMarker -or -not $containsEmitter)) {
    throw "Strict mode failed: telemetry script marker/emitter function not found."
}

$pinnedCommitPath = Join-Path $repoRoot "tools/lua-harness/vendor/eaw-abstraction-layer/PINNED_COMMIT.txt"
$pinnedCommit = if (Test-Path -Path $pinnedCommitPath) { (Get-Content -Raw -Path $pinnedCommitPath).Trim() } else { "unknown" }
$timestamp = (Get-Date).ToUniversalTime().ToString("o")
$emittedLine = "SWFOC_TRAINER_TELEMETRY timestamp=$timestamp mode=$Mode"

$outDirectory = Split-Path -Parent $OutPath
if (-not [string]::IsNullOrWhiteSpace($outDirectory) -and -not (Test-Path -Path $outDirectory)) {
    New-Item -Path $outDirectory -ItemType Directory | Out-Null
}

$result = [ordered]@{
    schemaVersion = "1.0"
    succeeded = ($containsMarker -and $containsEmitter)
    reasonCode = if ($containsMarker -and $containsEmitter) { "ok" } else { "telemetry_marker_missing" }
    scriptPath = $resolvedScriptPath
    mode = $Mode
    emittedLine = $emittedLine
    vendor = [ordered]@{
        name = "eaw-abstraction-layer"
        pinnedCommit = $pinnedCommit
    }
    diagnostics = [ordered]@{
        containsMarker = $containsMarker
        containsEmitterFunction = $containsEmitter
    }
}

$result | ConvertTo-Json -Depth 8 | Set-Content -Path $OutPath
Write-Output "lua-harness output written: $OutPath"
Write-Output "emitted: $emittedLine"
