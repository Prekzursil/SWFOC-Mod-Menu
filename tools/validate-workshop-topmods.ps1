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
    Add-ValidationError -Errors $errors -Message "workshop top-mods file not found: $Path"
    Write-ValidationResult -Errors $errors -Label "workshop top-mods" -Path $Path
}

if (-not (Test-Path -Path $SchemaPath)) {
    Add-ValidationError -Errors $errors -Message "schema file not found: $SchemaPath"
    Write-ValidationResult -Errors $errors -Label "workshop top-mods" -Path $Path
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

    if ([int]$payload.appId -ne 32470) {
        Add-ValidationError -Errors $errors -Message "appId must be 32470 for SWFOC discovery payload"
    }

    if (@($payload.sources).Count -lt 1) {
        Add-ValidationError -Errors $errors -Message "sources must include at least one retrieval URL"
    }

    $mods = @($payload.topMods)
    for ($i = 0; $i -lt $mods.Count; $i++) {
        $mod = $mods[$i]
        foreach ($requiredField in @(
            "workshopId",
            "title",
            "url",
            "subscriptions",
            "lifetimeSubscriptions",
            "timeUpdated",
            "parentDependencies",
            "launchHints",
            "candidateBaseProfile",
            "confidence",
            "riskLevel",
            "normalizedTags")) {
            Confirm-ValidationField -Object $mod -Field $requiredField -Errors $errors -Prefix "topMods[$i]"
        }

        if ([string]$mod.workshopId -notmatch "^[0-9]{4,}$") {
            Add-ValidationError -Errors $errors -Message "topMods[$i].workshopId must be numeric"
        }

        if ([string]$mod.url -notmatch "^https://steamcommunity\.com/sharedfiles/filedetails/\?id=[0-9]+$") {
            Add-ValidationError -Errors $errors -Message "topMods[$i].url must be a Steam workshop details URL"
        }

        if ([double]$mod.confidence -lt 0.0 -or [double]$mod.confidence -gt 1.0) {
            Add-ValidationError -Errors $errors -Message "topMods[$i].confidence must be within [0,1]"
        }

        $launchHints = $mod.launchHints
        Confirm-ValidationField -Object $launchHints -Field "steamModIds" -Errors $errors -Prefix "topMods[$i].launchHints"
        Confirm-ValidationField -Object $launchHints -Field "modPathHints" -Errors $errors -Prefix "topMods[$i].launchHints"

        foreach ($steamModId in @($launchHints.steamModIds)) {
            if ([string]$steamModId -notmatch "^[0-9]{4,}$") {
                Add-ValidationError -Errors $errors -Message "topMods[$i].launchHints.steamModIds must be numeric"
            }
        }

        foreach ($dependencyId in @($mod.parentDependencies)) {
            if ([string]$dependencyId -notmatch "^[0-9]{4,}$") {
                Add-ValidationError -Errors $errors -Message "topMods[$i].parentDependencies must be numeric"
            }
        }
    }
}

Write-ValidationResult -Errors $errors -Label "workshop top-mods" -Path $Path
