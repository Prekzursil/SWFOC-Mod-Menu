param(
    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-VsWherePath {
    $fromPath = Get-Command vswhere -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) {
        return $fromPath.Source
    }

    $wellKnown = @(
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"),
        (Join-Path $env:ProgramFiles "Microsoft Visual Studio\Installer\vswhere.exe"),
        "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe",
        "C:\Program Files\Microsoft Visual Studio\Installer\vswhere.exe",
        "D:\Program Files\Microsoft Visual Studio\Installer\vswhere.exe",
        "/mnt/c/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe",
        "/mnt/c/Program Files/Microsoft Visual Studio/Installer/vswhere.exe",
        "/mnt/d/Program Files/Microsoft Visual Studio/Installer/vswhere.exe"
    )

    foreach ($candidate in $wellKnown) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path -Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Resolve-RecommendedGenerator {
    param(
        [string]$ProductLineVersion
    )

    if ([string]::IsNullOrWhiteSpace($ProductLineVersion)) {
        return $null
    }

    $majorToken = ($ProductLineVersion -split '[^0-9]')[0]
    [int]$major = 0
    if (-not [int]::TryParse($majorToken, [ref]$major)) {
        return $null
    }
    switch ($major) {
        18 { return "Visual Studio 18 2026" }
        17 { return "Visual Studio 17 2022" }
        16 { return "Visual Studio 16 2019" }
        default { return $null }
    }
}

function Resolve-VisualStudioMetadata {
    function Resolve-VisualStudioMetadataFromDisk {
        $roots = @(
            "C:\Program Files\Microsoft Visual Studio",
            "D:\Program Files\Microsoft Visual Studio",
            "/mnt/c/Program Files/Microsoft Visual Studio",
            "/mnt/d/Program Files/Microsoft Visual Studio"
        )

        $instances = New-Object System.Collections.Generic.List[object]
        foreach ($root in $roots) {
            if (-not (Test-Path -Path $root)) {
                continue
            }

            $majorDirs = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^\d{2}$' } |
                Sort-Object Name -Descending

            foreach ($majorDir in $majorDirs) {
                $editions = Get-ChildItem -Path $majorDir.FullName -Directory -ErrorAction SilentlyContinue |
                    Sort-Object Name
                foreach ($edition in $editions) {
                    $instances.Add([PSCustomObject]@{
                        instancePath = $edition.FullName
                        productLineVersion = $majorDir.Name
                    })
                }
            }
        }

        $instance = $instances | Select-Object -First 1
        if ($null -eq $instance) {
            return [PSCustomObject]@{
                vsInstancePath = $null
                vsProductLineVersion = $null
                recommendedGenerator = $null
            }
        }

        return [PSCustomObject]@{
            vsInstancePath = [string]$instance.instancePath
            vsProductLineVersion = [string]$instance.productLineVersion
            recommendedGenerator = Resolve-RecommendedGenerator -ProductLineVersion ([string]$instance.productLineVersion)
        }
    }

    $vswherePath = Resolve-VsWherePath
    if ([string]::IsNullOrWhiteSpace($vswherePath)) {
        return Resolve-VisualStudioMetadataFromDisk
    }

    try {
        $vswhereArgs = @(
            "-latest",
            "-products", "*",
            "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "-format", "json"
        )
        $raw = & $vswherePath @vswhereArgs
        $exitCode = if (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue) { [int]$global:LASTEXITCODE } else { 0 }
        if ($exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($raw)) {
            return Resolve-VisualStudioMetadataFromDisk
        }

        $parsed = $raw | ConvertFrom-Json
        $instance = @($parsed) | Select-Object -First 1
        if ($null -eq $instance) {
            return Resolve-VisualStudioMetadataFromDisk
        }

        $productLineVersion = [string]$instance.catalog.productLineVersion
        if ([string]::IsNullOrWhiteSpace($productLineVersion)) {
            $productLineVersion = [string]$instance.catalog.productDisplayVersion
        }
        if ([string]::IsNullOrWhiteSpace($productLineVersion)) {
            $productLineVersion = [string]$instance.installationVersion
        }

        return [PSCustomObject]@{
            vsInstancePath = [string]$instance.installationPath
            vsProductLineVersion = $productLineVersion
            recommendedGenerator = Resolve-RecommendedGenerator -ProductLineVersion $productLineVersion
        }
    }
    catch {
        return Resolve-VisualStudioMetadataFromDisk
    }
}

$candidates = New-Object System.Collections.Generic.List[string]

$fromPath = Get-Command cmake -ErrorAction SilentlyContinue
if ($null -ne $fromPath) {
    $candidates.Add($fromPath.Source)
}

$wellKnown = @(
    "C:\Program Files\CMake\bin\cmake.exe",
    "C:\Program Files (x86)\CMake\bin\cmake.exe",
    "$env:LOCALAPPDATA\Programs\CMake\bin\cmake.exe",
    "/mnt/c/Program Files/CMake/bin/cmake.exe",
    "/mnt/c/Program Files (x86)/CMake/bin/cmake.exe"
)

foreach ($candidate in $wellKnown) {
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        continue
    }

    if (Test-Path -Path $candidate) {
        $candidates.Add($candidate)
    }
}

$resolved = $candidates | Select-Object -Unique
$primary = $resolved | Select-Object -First 1
$vsMetadata = Resolve-VisualStudioMetadata
$result = [PSCustomObject]@{
    found = $null -ne $primary
    primary = $primary
    candidates = @($resolved)
    vsInstancePath = [string]$vsMetadata.vsInstancePath
    vsProductLineVersion = [string]$vsMetadata.vsProductLineVersion
    recommendedGenerator = [string]$vsMetadata.recommendedGenerator
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 4
}
else {
    if ($result.found) {
        Write-Host "cmake: $($result.primary)"
    }
    else {
        Write-Host "cmake: not found"
    }

    if (-not [string]::IsNullOrWhiteSpace($result.vsProductLineVersion)) {
        Write-Host "visual studio: $($result.vsProductLineVersion) ($($result.vsInstancePath))"
    }
    else {
        Write-Host "visual studio: not detected"
    }

    if (-not [string]::IsNullOrWhiteSpace($result.recommendedGenerator)) {
        Write-Host "recommended generator: $($result.recommendedGenerator)"
    }
}
