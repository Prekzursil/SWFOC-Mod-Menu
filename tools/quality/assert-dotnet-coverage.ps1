param(
    [Parameter(Mandatory = $true)][string]$CoveragePath,
    [double]$MinLine = 100,
    [double]$MinBranch = 100,
    [string]$Scope = "src"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $CoveragePath)) {
    throw "Coverage file not found: $CoveragePath"
}

[xml]$coverageXml = Get-Content -Raw -Path $CoveragePath
if ($null -eq $coverageXml.coverage) {
    throw "Invalid coverage report: missing <coverage> root"
}

function Convert-ToPercent {
    param([string]$Raw)

    if ([string]::IsNullOrWhiteSpace($Raw)) {
        return 0.0
    }

    return [Math]::Round(([double]$Raw) * 100.0, 2)
}

$overallLine = Convert-ToPercent -Raw ([string]$coverageXml.coverage.'line-rate')
$overallBranch = Convert-ToPercent -Raw ([string]$coverageXml.coverage.'branch-rate')

$packages = @($coverageXml.coverage.packages.package)
if ($packages.Count -eq 0) {
    throw "Coverage report has no package entries."
}

$violations = New-Object System.Collections.Generic.List[string]
foreach ($package in $packages) {
    $packageName = [string]$package.name
    $line = Convert-ToPercent -Raw ([string]$package.'line-rate')
    $branch = Convert-ToPercent -Raw ([string]$package.'branch-rate')

    if ($line -lt $MinLine -or $branch -lt $MinBranch) {
        [void]$violations.Add("package=$packageName line=$line branch=$branch")
    }
}

Write-Output "coverage_overall line=$overallLine branch=$overallBranch scope=$Scope"
if ($overallLine -lt $MinLine -or $overallBranch -lt $MinBranch) {
    throw "Coverage threshold failed (overall): line=$overallLine branch=$overallBranch required=$MinLine/$MinBranch"
}

if ($violations.Count -gt 0) {
    throw "Coverage threshold failed (package): $($violations -join '; ')"
}

Write-Output "Coverage gate passed: overall and package thresholds meet $MinLine/$MinBranch."
