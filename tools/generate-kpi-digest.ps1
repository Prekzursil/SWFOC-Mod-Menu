#!/usr/bin/env pwsh
<#
.SYNOPSIS
Generate weekly KPI digest for AI-assisted engineering operations.

.DESCRIPTION
Analyzes recent repository activity and generates a KPI report covering:
- Intake-to-PR lead time
- PR cycle time
- Queue failure rate
- Agent rework rate
- Evidence completeness rate
- Regression incident count

.PARAMETER OutputPath
Optional path to save the digest report. If not provided, outputs to console.

.PARAMETER DaysBack
Number of days to analyze. Default is 7 (one week).

.EXAMPLE
pwsh ./tools/generate-kpi-digest.ps1

.EXAMPLE
pwsh ./tools/generate-kpi-digest.ps1 -OutputPath TestResults/kpi-digest.md -DaysBack 14
#>

param(
    [string]$OutputPath,
    [int]$DaysBack = 7
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

Write-Host "Generating KPI digest for last $DaysBack days..." -ForegroundColor Cyan

$now = Get-Date
$since = $now.AddDays(-$DaysBack)
$windowStart = $since.ToString("yyyy-MM-ddTHH:mm:ssZ")
$windowEnd = $now.ToString("yyyy-MM-ddTHH:mm:ssZ")

# Initialize counters
$openedPRs = 0
$mergedPRs = 0
$closedPRs = 0
$openAgentIssues = 0
$openRegressionIssues = 0
$completedIssues = 0

# Check if gh CLI is available
$ghAvailable = $null -ne (Get-Command gh -ErrorAction SilentlyContinue)

if ($ghAvailable) {
    Write-Host "Using gh CLI to fetch repository metrics..." -ForegroundColor Green
    
    # Fetch PR metrics
    try {
        $prsJson = gh pr list --state all --json number,state,createdAt,mergedAt,closedAt --limit 200 2>&1
        if ($LASTEXITCODE -eq 0) {
            $prs = $prsJson | ConvertFrom-Json
            $openedPRs = ($prs | Where-Object { (Get-Date $_.createdAt) -ge $since }).Count
            $mergedPRs = ($prs | Where-Object { $_.mergedAt -and (Get-Date $_.mergedAt) -ge $since }).Count
            $closedPRs = ($prs | Where-Object { $_.closedAt -and (Get-Date $_.closedAt) -ge $since }).Count
        }
    }
    catch {
        Write-Warning "Failed to fetch PR metrics: $($_.Exception.Message)"
    }
    
    # Fetch issue metrics
    try {
        $issuesJson = gh issue list --state open --json number,labels,title --limit 200 2>&1
        if ($LASTEXITCODE -eq 0) {
            $issues = $issuesJson | ConvertFrom-Json
            $openAgentIssues = ($issues | Where-Object { 
                $labels = $_.labels | ForEach-Object { $_.name }
                ($labels -contains "agent:ready") -or ($labels -contains "agent:in-progress") -or ($labels -contains "agent:blocked")
            }).Count
            
            $openRegressionIssues = ($issues | Where-Object {
                $labels = $_.labels | ForEach-Object { $_.name }
                $labels -contains "regression:escaped"
            }).Count
        }
    }
    catch {
        Write-Warning "Failed to fetch issue metrics: $($_.Exception.Message)"
    }
    
    # Fetch completed issues
    try {
        $closedIssuesJson = gh issue list --state closed --json number,closedAt --limit 200 2>&1
        if ($LASTEXITCODE -eq 0) {
            $closedIssues = $closedIssuesJson | ConvertFrom-Json
            $completedIssues = ($closedIssues | Where-Object { (Get-Date $_.closedAt) -ge $since }).Count
        }
    }
    catch {
        Write-Warning "Failed to fetch closed issue metrics: $($_.Exception.Message)"
    }
}
else {
    Write-Warning "gh CLI not available. Install from https://cli.github.com/ for automated metrics."
}

# Build digest report
$digest = @"
# Weekly KPI Digest - $($now.ToString("yyyy-MM-dd"))

## Summary

- Window start: $windowStart
- Window end: $windowEnd
- Analysis period: $DaysBack days

## Automated Snapshot

- PRs opened ($DaysBack d): $openedPRs
- PRs merged ($DaysBack d): $mergedPRs
- PRs closed without merge ($DaysBack d): $($closedPRs - $mergedPRs)
- Issues completed ($DaysBack d): $completedIssues
- Open agent-tracked issues: $openAgentIssues
- Open escaped-regression issues: $openRegressionIssues

## KPI Fields (manual verification required)

### Intake-to-PR lead time
- Median time from issue creation to first PR opened:
- Target: < 3 days for agent-ready issues
- Status: [FILL]

### PR cycle time
- Median time from PR opened to merged:
- Target: < 2 days for agent-generated PRs
- Status: [FILL]

### Queue failure rate
- Agent tasks that failed validation or were reverted:
- Target: < 10% failure rate
- Status: [FILL]

### Agent rework rate
- PRs requiring significant human rework after agent completion:
- Target: < 15% rework rate
- Status: [FILL]

### Evidence completeness rate
- Runtime/tooling PRs with complete evidence (repro bundle or justified skip):
- Target: 100% compliance
- Status: [FILL]

### Regression incident count
- Escaped regressions detected in production:
- Target: 0 per week
- Current: $openRegressionIssues open

## Notes

### Blockers
- [FILL: List any blockers preventing agent task completion]

### Regressions
- [FILL: Describe any regressions and remediation actions]

### Process improvements
- [FILL: Suggested process or tooling improvements]

## Week-over-week comparison

- Previous week PRs merged: [FILL]
- Current week PRs merged: $mergedPRs
- Trend: [FILL]

## Action items

- [ ] Review and close stale agent-tracked issues
- [ ] Address any escaped regressions with hotfixes
- [ ] Update baseline-lite package if process changes identified
- [ ] Archive this digest in project documentation

---
Generated: $($now.ToString("yyyy-MM-dd HH:mm:ss"))
Tool: tools/generate-kpi-digest.ps1
"@

if ($OutputPath) {
    $outputDir = Split-Path -Parent $OutputPath
    if ($outputDir -and -not (Test-Path -Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }
    
    $digest | Out-File -FilePath $OutputPath -Encoding UTF8
    Write-Host "KPI digest saved to: $OutputPath" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host $digest
}

Write-Host ""
Write-Host "KPI digest generation complete." -ForegroundColor Cyan
