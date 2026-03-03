param(
    [string]$ConfigPath = "tools/quality/provider-gate.config.json",
    [switch]$Strict
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$helperPath = Join-Path $scriptDir "invoke-provider-zero-gate.ps1"
if (-not (Test-Path -Path $helperPath)) {
    throw "Provider gate helper not found: $helperPath"
}

$providerKey = "snyk"
& $helperPath -ProviderKey $providerKey -ConfigPath $ConfigPath -Strict:$Strict
