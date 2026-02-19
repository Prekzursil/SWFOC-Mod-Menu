param(
    [Parameter(Mandatory = $true)][string]$ModulePath,
    [string]$ProfilePath,
    [string]$RunId,
    [string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
}

$repoRoot = Resolve-Path (Join-Path (Join-Path $PSScriptRoot "..") "..")
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "TestResults/research/$RunId"
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$fingerprintPath = Join-Path $OutputRoot "fingerprint.json"
$signaturePath = Join-Path $OutputRoot "signature-pack.json"
$notesPath = Join-Path $OutputRoot "analysis-notes.md"

& (Join-Path $PSScriptRoot "capture-binary-fingerprint.ps1") `
    -ModulePath $ModulePath `
    -RunId $RunId `
    -OutputPath $fingerprintPath

& (Join-Path $PSScriptRoot "generate-signature-candidates.ps1") `
    -FingerprintPath $fingerprintPath `
    -ProfilePath $ProfilePath `
    -RunId $RunId `
    -OutputPath $signaturePath

& (Join-Path $PSScriptRoot "normalize-signature-pack.ps1") `
    -InputPath $signaturePath `
    -OutputPath $signaturePath

$notes = @"
# Capability Intel Run

- runId: $RunId
- modulePath: $([System.IO.Path]::GetFullPath($ModulePath))
- fingerprint: $fingerprintPath
- signaturePack: $signaturePath
- generatedAtUtc: $((Get-Date).ToUniversalTime().ToString("o"))

## Notes
- Add operation anchor rationale and analyst comments here.
- Validate outputs against schemas before promoting maps.
"@

$notes | Set-Content -Path $notesPath -Encoding UTF8

Write-Host "Capability intel run complete"
Write-Host " - fingerprint: $fingerprintPath"
Write-Host " - signature pack: $signaturePath"
Write-Host " - notes: $notesPath"
