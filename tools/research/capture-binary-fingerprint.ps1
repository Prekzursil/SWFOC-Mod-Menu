param(
    [Parameter(Mandatory = $true)][string]$ModulePath,
    [int]$ProcessId,
    [string]$RunId,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
}

$repoRoot = Resolve-Path (Join-Path (Join-Path $PSScriptRoot "..") "..")
$moduleFullPath = [System.IO.Path]::GetFullPath($ModulePath)
if (-not (Test-Path -Path $moduleFullPath)) {
    throw "Module not found: $moduleFullPath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputDir = Join-Path $repoRoot "TestResults/research/$RunId"
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    $OutputPath = Join-Path $outputDir "fingerprint.json"
} else {
    $outputDir = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutputPath))
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$hash = (Get-FileHash -Algorithm SHA256 -Path $moduleFullPath).Hash.ToLowerInvariant()
$file = Get-Item -LiteralPath $moduleFullPath
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($moduleFullPath)

$moduleList = @()
if ($ProcessId -gt 0) {
    try {
        $process = Get-Process -Id $ProcessId -ErrorAction Stop
        $moduleList = @($process.Modules | ForEach-Object { $_.ModuleName } | Sort-Object -Unique)
    }
    catch {
        $moduleList = @()
    }
}

$moduleName = [System.IO.Path]::GetFileName($moduleFullPath)
$moduleStem = [System.IO.Path]::GetFileNameWithoutExtension($moduleName).ToLowerInvariant().Replace(' ', '_')
$fingerprintId = "{0}_{1}" -f $moduleStem, $hash.Substring(0, [Math]::Min(16, $hash.Length))

$result = [ordered]@{
    schemaVersion = "1.0"
    runId = $RunId
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    fingerprintId = $fingerprintId
    fileSha256 = $hash
    moduleName = $moduleName
    productVersion = $version.ProductVersion
    fileVersion = $version.FileVersion
    timestampUtc = $file.LastWriteTimeUtc.ToString("o")
    moduleList = $moduleList
    sourcePath = $moduleFullPath
}

($result | ConvertTo-Json -Depth 8) | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host "Fingerprint written: $OutputPath"
