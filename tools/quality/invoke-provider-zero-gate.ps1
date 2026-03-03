param(
    [Parameter(Mandatory = $true)][string]$ProviderKey,
    [string]$ConfigPath = "tools/quality/provider-gate.config.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-TemplateValue {
    param([Parameter(Mandatory = $true)][string]$Value)

    $pattern = '\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}'
    return [regex]::Replace($Value, $pattern, {
        param($match)
        $name = $match.Groups['name'].Value
        $resolved = [Environment]::GetEnvironmentVariable($name)
        if ([string]::IsNullOrWhiteSpace($resolved)) {
            return ""
        }

        return $resolved
    })
}

function Get-RequiredEnvironmentValues {
    param([Parameter(Mandatory = $true)][object[]]$RequiredEnv)

    $resolved = @{}
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($varName in $RequiredEnv) {
        $name = [string]$varName
        if ([string]::IsNullOrWhiteSpace($name)) {
            continue
        }

        $value = [Environment]::GetEnvironmentVariable($name)
        if ([string]::IsNullOrWhiteSpace($value)) {
            [void]$missing.Add($name)
        }
        else {
            $resolved[$name] = $value
        }
    }

    return [PSCustomObject]@{
        Values = $resolved
        Missing = @($missing)
    }
}

function Get-JsonValueByPath {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    $current = $Object
    foreach ($segment in ($Path -split '\.')) {
        if ($null -eq $current) {
            return $null
        }

        if ($current -is [System.Collections.IDictionary]) {
            if (-not $current.Contains($segment)) {
                return $null
            }
            $current = $current[$segment]
            continue
        }

        $prop = $current.PSObject.Properties[$segment]
        if ($null -eq $prop) {
            return $null
        }

        $current = $prop.Value
    }

    return $current
}

function Build-QueryString {
    param([hashtable]$Pairs)

    if ($Pairs.Count -eq 0) {
        return ""
    }

    $parts = foreach ($key in ($Pairs.Keys | Sort-Object)) {
        $encodedKey = [uri]::EscapeDataString([string]$key)
        $encodedValue = [uri]::EscapeDataString([string]$Pairs[$key])
        "${encodedKey}=${encodedValue}"
    }

    return ($parts -join '&')
}

if (-not (Test-Path -Path $ConfigPath)) {
    throw "Provider gate configuration not found: $ConfigPath"
}

$config = Get-Content -Raw -Path $ConfigPath | ConvertFrom-Json
if ($null -eq $config.providers) {
    throw "Invalid provider gate config: missing 'providers' section."
}

$provider = $config.providers.$ProviderKey
if ($null -eq $provider) {
    throw "Provider '$ProviderKey' not defined in $ConfigPath"
}

$strictMissingConfig = $Strict.IsPresent -or [bool]$config.strictMissingConfig
$required = @()
if ($null -ne $provider.requiredEnv) {
    $required = @($provider.requiredEnv)
}

$requiredValues = Get-RequiredEnvironmentValues -RequiredEnv $required
if ($strictMissingConfig -and $requiredValues.Missing.Count -gt 0) {
    throw "Provider '$ProviderKey' missing required configuration env vars: $($requiredValues.Missing -join ', ')"
}

$urlTemplate = [string]$provider.url
$url = Resolve-TemplateValue -Value $urlTemplate
if ([string]::IsNullOrWhiteSpace($url)) {
    throw "Provider '$ProviderKey' resolved URL is empty. Check url template and env vars in $ConfigPath"
}

$queryPairs = @{}
if ($null -ne $provider.query) {
    foreach ($property in $provider.query.PSObject.Properties) {
        $queryPairs[$property.Name] = Resolve-TemplateValue -Value ([string]$property.Value)
    }
}

$queryString = Build-QueryString -Pairs $queryPairs
$uri = if ([string]::IsNullOrWhiteSpace($queryString)) { $url } else { "$url?$queryString" }

$headers = @{}
$authScheme = [string]$provider.authScheme
$tokenEnv = [string]$provider.tokenEnv
$token = if ([string]::IsNullOrWhiteSpace($tokenEnv)) { "" } else { [Environment]::GetEnvironmentVariable($tokenEnv) }
if (-not [string]::IsNullOrWhiteSpace($token)) {
    switch ($authScheme) {
        "basicToken" {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes("${token}:")
            $headers["Authorization"] = "Basic $([Convert]::ToBase64String($bytes))"
        }
        "bearer" {
            $headers["Authorization"] = "Bearer $token"
        }
        "apiTokenHeader" {
            $headerName = [string]$provider.tokenHeaderName
            if ([string]::IsNullOrWhiteSpace($headerName)) {
                throw "Provider '$ProviderKey' authScheme apiTokenHeader requires tokenHeaderName in config."
            }
            $headers[$headerName] = $token
        }
        default {
            # no-op
        }
    }
}

$method = [string]$provider.method
if ([string]::IsNullOrWhiteSpace($method)) {
    $method = "GET"
}

Write-Output "provider=$ProviderKey uri=$uri"
$response = Invoke-WebRequest -Uri $uri -Method $method -Headers $headers -ContentType "application/json" -UseBasicParsing
if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) {
    throw "Provider '$ProviderKey' request failed: HTTP $($response.StatusCode)"
}

$json = $null
if (-not [string]::IsNullOrWhiteSpace($response.Content)) {
    $json = $response.Content | ConvertFrom-Json
}

$count = $null
$countHeader = [string]$provider.countHeader
if (-not [string]::IsNullOrWhiteSpace($countHeader) -and $response.Headers[$countHeader]) {
    $count = [int]$response.Headers[$countHeader]
}

if ($null -eq $count) {
    $countPath = [string]$provider.countJsonPath
    if (-not [string]::IsNullOrWhiteSpace($countPath) -and $null -ne $json) {
        $rawCount = Get-JsonValueByPath -Object $json -Path $countPath
        if ($null -ne $rawCount -and "$rawCount" -match '^\d+$') {
            $count = [int]$rawCount
        }
    }
}

if ($null -eq $count -and $json -is [System.Collections.IEnumerable]) {
    $arrayValues = @($json)
    $count = $arrayValues.Count
}

if ($null -eq $count) {
    throw "Provider '$ProviderKey' did not return a parseable issue count."
}

Write-Output "provider=$ProviderKey openCount=$count"
if ($count -gt 0) {
    throw "Provider '$ProviderKey' has $count open issues. Zero-issue policy failed."
}

Write-Output "provider=$ProviderKey zero-backlog check passed."
