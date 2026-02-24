Set-StrictMode -Version Latest

function New-ValidationErrorList {
    return ,(New-Object System.Collections.ArrayList)
}

function Add-ValidationError {
    param(
        [Parameter()][System.Collections.ArrayList]$Errors,
        [Parameter(Mandatory = $true)][string]$Message
    )

    [void]$Errors.Add($Message)
}

function Confirm-ValidationField {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Field,
        [Parameter()][System.Collections.ArrayList]$Errors,
        [switch]$AllowNull,
        [string]$Prefix = ""
    )

    $fieldPath = if ([string]::IsNullOrWhiteSpace($Prefix)) { $Field } else { "$Prefix.$Field" }
    $prop = $Object.PSObject.Properties[$Field]

    if ($null -eq $prop) {
        Add-ValidationError -Errors $Errors -Message "missing required field: $fieldPath"
        return
    }

    $value = $prop.Value
    if ($null -eq $value -and -not $AllowNull) {
        Add-ValidationError -Errors $Errors -Message "null required field: $fieldPath"
        return
    }

    if (-not $AllowNull -and $value -is [string] -and [string]::IsNullOrWhiteSpace($value)) {
        Add-ValidationError -Errors $Errors -Message "empty required field: $fieldPath"
    }
}

function Write-ValidationResult {
    param(
        [Parameter()][System.Collections.ArrayList]$Errors,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ($Errors.Count -gt 0) {
        Write-Output "$Label validation failed:"
        foreach ($err in $Errors) {
            Write-Output " - $err"
        }

        exit 1
    }

    Write-Output "$Label validation passed: $Path"
}
