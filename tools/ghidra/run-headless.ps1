param(
    [Parameter(Mandatory = $true)][string]$BinaryPath,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [string]$AnalysisRunId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($AnalysisRunId)) {
    $AnalysisRunId = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
}

if (-not $env:GHIDRA_HOME) {
    throw "GHIDRA_HOME is required."
}

$analyzeHeadless = Join-Path $env:GHIDRA_HOME "support/analyzeHeadless.bat"
if (-not (Test-Path -Path $analyzeHeadless)) {
    throw "analyzeHeadless not found: $analyzeHeadless"
}

$binaryFullPath = [System.IO.Path]::GetFullPath($BinaryPath)
if (-not (Test-Path -Path $binaryFullPath)) {
    throw "Binary not found: $binaryFullPath"
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$projectDir = Join-Path $OutputDir "ghidra-project"
$projectName = "swfoc-$AnalysisRunId"
$rawSymbolsPath = Join-Path $OutputDir "raw-symbols.json"
$symbolPackPath = Join-Path $OutputDir "symbol-pack.json"
$summaryPath = Join-Path $OutputDir "analysis-summary.json"
$determinismDir = Join-Path $OutputDir "determinism"
$artifactIndexPath = Join-Path $OutputDir "artifact-index.json"
$decompileArchivePath = [string]$env:SWFOC_GHIDRA_DECOMP_ARCHIVE_PATH

& $analyzeHeadless `
    $projectDir `
    $projectName `
    -import $binaryFullPath `
    -scriptPath (Join-Path $repoRoot "tools/ghidra") `
    -postScript export_symbols.py $rawSymbolsPath `
    -deleteProject

$emitScript = Join-Path $repoRoot "tools/ghidra/emit-symbol-pack.py"
$emitArgs = @(
    "--raw-symbols", $rawSymbolsPath,
    "--binary-path", $binaryFullPath,
    "--analysis-run-id", $AnalysisRunId,
    "--output-pack", $symbolPackPath,
    "--output-summary", $summaryPath
)
if (-not [string]::IsNullOrWhiteSpace($decompileArchivePath)) {
    $emitArgs += @("--decompile-archive-path", $decompileArchivePath)
}
python $emitScript @emitArgs

$determinismScript = Join-Path $repoRoot "tools/ghidra/check-determinism.py"
python $determinismScript `
    --raw-symbols $rawSymbolsPath `
    --binary-path $binaryFullPath `
    --analysis-run-id-base $AnalysisRunId `
    --output-dir $determinismDir

$artifactIndexScript = Join-Path $repoRoot "tools/ghidra/emit-artifact-index.py"
$indexArgs = @(
    "--analysis-run-id", $AnalysisRunId,
    "--binary-path", $binaryFullPath,
    "--raw-symbols", $rawSymbolsPath,
    "--symbol-pack", $symbolPackPath,
    "--summary", $summaryPath,
    "--output", $artifactIndexPath
)
if (-not [string]::IsNullOrWhiteSpace($decompileArchivePath)) {
    $indexArgs += @("--decompile-archive", $decompileArchivePath)
}
python $artifactIndexScript @indexArgs

Write-Output "ghidra headless analysis complete"
Write-Output " - raw symbols: $rawSymbolsPath"
Write-Output " - symbol pack: $symbolPackPath"
Write-Output " - summary: $summaryPath"
Write-Output " - determinism report: $(Join-Path $determinismDir 'determinism-report.json')"
Write-Output " - artifact index: $artifactIndexPath"
