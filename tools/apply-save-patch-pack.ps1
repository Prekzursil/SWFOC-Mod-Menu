param(
    [Parameter(Mandatory = $true)][string]$TargetSavePath,
    [Parameter(Mandatory = $true)][string]$PatchPackPath,
    [Parameter(Mandatory = $true)][string]$TargetProfileId,
    [string]$SchemaRootPath = "profiles/default/schemas",
    [bool]$Strict = $true,
    [switch]$BuildIfNeeded
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSEdition -ne "Core") {
    throw "Run this script with PowerShell 7 (pwsh)."
}

function Resolve-LoggingAbstractionsPath {
    param([string]$RepoRoot)

    $candidates = New-Object System.Collections.Generic.List[string]
    $candidates.Add((Join-Path $RepoRoot "src/SwfocTrainer.Saves/bin/Release/net8.0/Microsoft.Extensions.Logging.Abstractions.dll"))
    $candidates.Add((Join-Path $RepoRoot "src/SwfocTrainer.Core/bin/Release/net8.0/Microsoft.Extensions.Logging.Abstractions.dll"))

    $sharedRoot = "C:\\Program Files\\dotnet\\shared\\Microsoft.AspNetCore.App"
    if (Test-Path $sharedRoot) {
        $sharedDll = Get-ChildItem -Path $sharedRoot -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "Microsoft.Extensions.Logging.Abstractions.dll" } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($sharedDll) {
            $candidates.Add($sharedDll)
        }
    }

    $nugetDll = Join-Path $env:USERPROFILE ".nuget\\packages\\microsoft.extensions.logging.abstractions\\8.0.3\\lib\\net8.0\\Microsoft.Extensions.Logging.Abstractions.dll"
    $candidates.Add($nugetDll)

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    throw "Could not locate Microsoft.Extensions.Logging.Abstractions.dll."
}

$repoRoot = Resolve-Path "."
$coreDll = Join-Path $repoRoot "src/SwfocTrainer.Core/bin/Release/net8.0/SwfocTrainer.Core.dll"
$savesDll = Join-Path $repoRoot "src/SwfocTrainer.Saves/bin/Release/net8.0/SwfocTrainer.Saves.dll"

if ($BuildIfNeeded -or -not (Test-Path $coreDll) -or -not (Test-Path $savesDll)) {
    dotnet build src/SwfocTrainer.Saves/SwfocTrainer.Saves.csproj -c Release --nologo
}

if (-not (Test-Path $coreDll) -or -not (Test-Path $savesDll)) {
    throw "Required assemblies not found. Build failed or output path changed."
}

$loggingDll = Resolve-LoggingAbstractionsPath -RepoRoot $repoRoot
[void][System.Reflection.Assembly]::LoadFrom($coreDll)
[void][System.Reflection.Assembly]::LoadFrom($loggingDll)
[void][System.Reflection.Assembly]::LoadFrom($savesDll)

$saveOptions = [SwfocTrainer.Saves.Config.SaveOptions]::new()
$saveOptions.SchemaRootPath = [System.IO.Path]::GetFullPath($SchemaRootPath)

$codecLoggerType = [Microsoft.Extensions.Logging.Abstractions.NullLogger``1].MakeGenericType([SwfocTrainer.Saves.Services.BinarySaveCodec])
$codecLoggerField = $codecLoggerType.GetField("Instance", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
if ($codecLoggerField) {
    $codecLogger = $codecLoggerField.GetValue($null)
}
else {
    $codecLoggerProperty = $codecLoggerType.GetProperty("Instance", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
    if (-not $codecLoggerProperty) {
        throw "Could not resolve NullLogger instance for BinarySaveCodec."
    }

    $codecLogger = $codecLoggerProperty.GetValue($null)
}

$applyLoggerType = [Microsoft.Extensions.Logging.Abstractions.NullLogger``1].MakeGenericType([SwfocTrainer.Saves.Services.SavePatchApplyService])
$applyLoggerField = $applyLoggerType.GetField("Instance", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
if ($applyLoggerField) {
    $applyLogger = $applyLoggerField.GetValue($null)
}
else {
    $applyLoggerProperty = $applyLoggerType.GetProperty("Instance", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
    if (-not $applyLoggerProperty) {
        throw "Could not resolve NullLogger instance for SavePatchApplyService."
    }

    $applyLogger = $applyLoggerProperty.GetValue($null)
}

$codec = [SwfocTrainer.Saves.Services.BinarySaveCodec]::new($saveOptions, $codecLogger)
$patchService = [SwfocTrainer.Saves.Services.SavePatchPackService]::new($saveOptions)
$applyService = [SwfocTrainer.Saves.Services.SavePatchApplyService]::new($codec, $patchService, $applyLogger)

$pack = $patchService.LoadPackAsync([System.IO.Path]::GetFullPath($PatchPackPath)).GetAwaiter().GetResult()
$result = $applyService.ApplyAsync([System.IO.Path]::GetFullPath($TargetSavePath), $pack, $TargetProfileId, $Strict).GetAwaiter().GetResult()

$options = [System.Text.Json.JsonSerializerOptions]::new()
$options.WriteIndented = $true
$options.Converters.Add([System.Text.Json.Serialization.JsonStringEnumConverter]::new())
$serialized = [System.Text.Json.JsonSerializer]::Serialize($result, $options)
Write-Output $serialized

if (-not $result.Applied) {
    exit 1
}
