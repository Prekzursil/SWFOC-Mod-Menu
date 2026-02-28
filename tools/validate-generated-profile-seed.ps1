param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$SchemaPath = "tools/schemas/generated-profile-seed.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "validation-helpers.ps1")

$errors = New-ValidationErrorList

if (-not (Test-Path -Path $Path)) {
    Add-ValidationError -Errors $errors -Message "generated profile seed file not found: $Path"
    Write-ValidationResult -Errors $errors -Label "generated profile seed" -Path $Path
}

if (-not (Test-Path -Path $SchemaPath)) {
    Add-ValidationError -Errors $errors -Message "schema file not found: $SchemaPath"
    Write-ValidationResult -Errors $errors -Label "generated profile seed" -Path $Path
}

$payload = Get-Content -Raw -Path $Path | ConvertFrom-Json
$schema = Get-Content -Raw -Path $SchemaPath | ConvertFrom-Json

foreach ($required in $schema.required) {
    Confirm-ValidationField -Object $payload -Field ([string]$required) -Errors $errors
}

if ($Strict) {
    if ([string]$payload.schemaVersion -ne "1.0") {
        Add-ValidationError -Errors $errors -Message "schemaVersion must be 1.0"
    }

    $appId = [string]$payload.appId
    if ($appId -notmatch "^[0-9]+$") {
        Add-ValidationError -Errors $errors -Message "appId must be numeric"
    }

    $seeds = @($payload.seeds)
    if ($seeds.Count -eq 0) {
        Add-ValidationError -Errors $errors -Message "seeds must contain at least one entry"
    }

    $requiredSeedFields = @($schema.properties.seeds.items.required)
    $allowedRiskLevels = @("low", "medium", "high")
    $topSourceRunId = [string]$payload.sourceRunId

    for ($i = 0; $i -lt $seeds.Count; $i++) {
        $seed = $seeds[$i]

        foreach ($requiredField in $requiredSeedFields) {
            Confirm-ValidationField -Object $seed -Field ([string]$requiredField) -Errors $errors -Prefix "seeds[$i]"
        }

        if ([string]$seed.workshopId -notmatch "^[0-9]+$") {
            Add-ValidationError -Errors $errors -Message "seeds[$i].workshopId must be numeric"
        }

        if ($allowedRiskLevels -notcontains [string]$seed.riskLevel) {
            Add-ValidationError -Errors $errors -Message "seeds[$i].riskLevel must be one of: $($allowedRiskLevels -join ', ')"
        }

        if ([double]$seed.confidence -lt 0.0 -or [double]$seed.confidence -gt 1.0) {
            Add-ValidationError -Errors $errors -Message "seeds[$i].confidence must be between 0 and 1"
        }

        if ([string]$seed.sourceRunId -ne $topSourceRunId) {
            Add-ValidationError -Errors $errors -Message "seeds[$i].sourceRunId must match top-level sourceRunId"
        }

        if (@($seed.requiredCapabilities).Count -eq 0) {
            Add-ValidationError -Errors $errors -Message "seeds[$i].requiredCapabilities must include at least one capability"
        }

        if (@($seed.anchorHints).Count -eq 0) {
            Add-ValidationError -Errors $errors -Message "seeds[$i].anchorHints must include at least one hint"
        }

        foreach ($dep in @($seed.parentDependencies)) {
            if ([string]$dep -notmatch "^[0-9]+$") {
                Add-ValidationError -Errors $errors -Message "seeds[$i].parentDependencies contains non-numeric id: $dep"
            }
        }
    }
}

Write-ValidationResult -Errors $errors -Label "generated profile seed" -Path $Path
