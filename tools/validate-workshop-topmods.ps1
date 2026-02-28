param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$SchemaPath = "tools/schemas/workshop-topmods.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "validation-helpers.ps1")

$errors = New-ValidationErrorList

if (-not (Test-Path -Path $Path)) {
    Add-ValidationError -Errors $errors -Message "workshop topmods file not found: $Path"
    Write-ValidationResult -Errors $errors -Label "workshop topmods" -Path $Path
}

if (-not (Test-Path -Path $SchemaPath)) {
    Add-ValidationError -Errors $errors -Message "schema file not found: $SchemaPath"
    Write-ValidationResult -Errors $errors -Label "workshop topmods" -Path $Path
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

    $sources = @($payload.sources)
    if ($sources.Count -eq 0) {
        Add-ValidationError -Errors $errors -Message "sources must include at least one source"
    }

    $mods = @($payload.topMods)
    if ($mods.Count -eq 0) {
        Add-ValidationError -Errors $errors -Message "topMods must contain at least one mod"
    }

    $requiredModFields = @($schema.properties.topMods.items.required)
    $allowedRiskLevels = @("low", "medium", "high")
    $allowedBaseProfiles = @("base_sweaw", "base_swfoc", "aotr_1397421866_swfoc", "roe_3447786229_swfoc")

    for ($i = 0; $i -lt $mods.Count; $i++) {
        $mod = $mods[$i]
        foreach ($requiredField in $requiredModFields) {
            Confirm-ValidationField -Object $mod -Field ([string]$requiredField) -Errors $errors -Prefix "topMods[$i]"
        }

        if ([string]$mod.workshopId -notmatch "^[0-9]+$") {
            Add-ValidationError -Errors $errors -Message "topMods[$i].workshopId must be numeric"
        }

        if ([string]$mod.url -notmatch "^https://steamcommunity\.com/sharedfiles/filedetails/\?id=[0-9]+$") {
            Add-ValidationError -Errors $errors -Message "topMods[$i].url must be a sharedfiles details URL"
        }

        if ([int]$mod.subscriptions -lt 0 -or [int]$mod.lifetimeSubscriptions -lt 0) {
            Add-ValidationError -Errors $errors -Message "topMods[$i] subscriptions values must be non-negative"
        }

        if ([int]$mod.lifetimeSubscriptions -lt [int]$mod.subscriptions) {
            Add-ValidationError -Errors $errors -Message "topMods[$i].lifetimeSubscriptions must be >= subscriptions"
        }

        if ($allowedRiskLevels -notcontains [string]$mod.riskLevel) {
            Add-ValidationError -Errors $errors -Message "topMods[$i].riskLevel must be one of: $($allowedRiskLevels -join ', ')"
        }

        if ([double]$mod.confidence -lt 0.0 -or [double]$mod.confidence -gt 1.0) {
            Add-ValidationError -Errors $errors -Message "topMods[$i].confidence must be between 0 and 1"
        }

        if ($allowedBaseProfiles -notcontains [string]$mod.candidateBaseProfile) {
            Add-ValidationError -Errors $errors -Message "topMods[$i].candidateBaseProfile must be one of: $($allowedBaseProfiles -join ', ')"
        }

        foreach ($dep in @($mod.parentDependencies)) {
            if ([string]$dep -notmatch "^[0-9]+$") {
                Add-ValidationError -Errors $errors -Message "topMods[$i].parentDependencies contains non-numeric id: $dep"
            }
        }

        foreach ($tag in @($mod.normalizedTags)) {
            if ([string]$tag -notmatch "^[a-z0-9][a-z0-9_-]*$") {
                Add-ValidationError -Errors $errors -Message "topMods[$i].normalizedTags contains invalid token: $tag"
            }
        }
    }
}

Write-ValidationResult -Errors $errors -Label "workshop topmods" -Path $Path
