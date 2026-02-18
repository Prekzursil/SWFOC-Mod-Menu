param(
    [Parameter(Mandatory = $true)][string]$BaselineDir,
    [Parameter(Mandatory = $true)][string]$CandidateDir,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $CandidateDir)) {
    throw "CandidateDir not found: $CandidateDir"
}

$baselineExists = Test-Path -Path $BaselineDir

function Get-ImageFiles {
    param([string]$Root)

    if (-not (Test-Path -Path $Root)) {
        return @()
    }

    return Get-ChildItem -Path $Root -Recurse -File |
        Where-Object { $_.Extension -match "^\.(png|jpg|jpeg|bmp)$" } |
        ForEach-Object {
            [PSCustomObject]@{
                FullPath = $_.FullName
                RelativePath = $_.FullName.Substring((Resolve-Path $Root).Path.Length).TrimStart('\\', '/')
                Hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash
            }
        }
}

$baselineFiles = @()
if ($baselineExists) {
    $baselineFiles = @(Get-ImageFiles -Root $BaselineDir)
}
$candidateFiles = @(Get-ImageFiles -Root $CandidateDir)

$baselineByPath = @{}
foreach ($file in $baselineFiles) {
    $baselineByPath[$file.RelativePath] = $file
}

$candidateByPath = @{}
foreach ($file in $candidateFiles) {
    $candidateByPath[$file.RelativePath] = $file
}

$changed = New-Object System.Collections.Generic.List[object]
$newFiles = New-Object System.Collections.Generic.List[string]
$missing = New-Object System.Collections.Generic.List[string]

foreach ($candidate in $candidateFiles) {
    if (-not $baselineByPath.ContainsKey($candidate.RelativePath)) {
        $newFiles.Add($candidate.RelativePath)
        continue
    }

    $base = $baselineByPath[$candidate.RelativePath]
    if ($base.Hash -ne $candidate.Hash) {
        $changed.Add([PSCustomObject]@{
            path = $candidate.RelativePath
            baselineHash = $base.Hash
            candidateHash = $candidate.Hash
        })
    }
}

foreach ($base in $baselineFiles) {
    if (-not $candidateByPath.ContainsKey($base.RelativePath)) {
        $missing.Add($base.RelativePath)
    }
}

$status = if (-not $baselineExists) {
    "baseline_missing"
}
elseif ($changed.Count -eq 0 -and $newFiles.Count -eq 0 -and $missing.Count -eq 0) {
    "no_diff"
}
else {
    "diff_detected"
}

$result = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    baselineDir = $BaselineDir
    candidateDir = $CandidateDir
    status = $status
    counts = [ordered]@{
        baseline = $baselineFiles.Count
        candidate = $candidateFiles.Count
        changed = $changed.Count
        newFiles = $newFiles.Count
        missing = $missing.Count
    }
    changed = $changed
    newFiles = $newFiles
    missing = $missing
}

$parentDir = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($parentDir) -and -not (Test-Path -Path $parentDir)) {
    New-Item -Path $parentDir -ItemType Directory | Out-Null
}

$result | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath
Write-Host "visual compare report: $OutputPath"
Write-Host "status: $status"
