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
    Confirm-ValidationField -Object $payload -Field $required -Errors $errors
}

if ($Strict) {
    if ([string]$payload.schemaVersion -ne "1.0") {
        Add-ValidationError -Errors $errors -Message "schemaVersion must be 1.0"
    }

    $seeds = @($payload.seeds)
    if ([int]$payload.seedCount -ne $seeds.Count) {
        Add-ValidationError -Errors $errors -Message "seedCount must match the number of seeds entries"
    }

    for ($i = 0; $i -lt $seeds.Count; $i++) {
        $seed = $seeds[$i]
        foreach ($requiredField in @(
            "workshopId",
            "title",
            "parentDependencies",
            "launchHints",
            "candidateBaseProfile",
            "requiredCapabilities",
            "anchorHints",
            "riskLevel",
            "confidence",
            "sourceRunId")) {
            Confirm-ValidationField -Object $seed -Field $requiredField -Errors $errors -Prefix "seeds[$i]"
        }

        if ([string]$seed.workshopId -notmatch "^[0-9]{4,}$") {
            Add-ValidationError -Errors $errors -Message "seeds[$i].workshopId must be numeric"
        }

        if ([double]$seed.confidence -lt 0.0 -or [double]$seed.confidence -gt 1.0) {
            Add-ValidationError -Errors $errors -Message "seeds[$i].confidence must be within [0,1]"
        }

        if (@($seed.requiredCapabilities).Count -eq 0) {
            Add-ValidationError -Errors $errors -Message "seeds[$i].requiredCapabilities must not be empty"
        }

        $launchHints = $seed.launchHints
        Confirm-ValidationField -Object $launchHints -Field "steamModIds" -Errors $errors -Prefix "seeds[$i].launchHints"
        Confirm-ValidationField -Object $launchHints -Field "modPathHints" -Errors $errors -Prefix "seeds[$i].launchHints"

        foreach ($steamModId in @($launchHints.steamModIds)) {
            if ([string]$steamModId -notmatch "^[0-9]{4,}$") {
                Add-ValidationError -Errors $errors -Message "seeds[$i].launchHints.steamModIds must be numeric"
            }
        }

        foreach ($dependencyId in @($seed.parentDependencies)) {
            if ([string]$dependencyId -notmatch "^[0-9]{4,}$") {
                Add-ValidationError -Errors $errors -Message "seeds[$i].parentDependencies must be numeric"
            }
        }
    }
}

Write-ValidationResult -Errors $errors -Label "generated profile seed" -Path $Path
