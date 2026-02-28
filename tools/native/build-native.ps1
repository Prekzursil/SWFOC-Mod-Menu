param(
    [ValidateSet("Auto", "Windows", "Wsl")][string]$Mode = "Auto",
    [string]$Configuration = "Release",
    [string]$BuildDir = "native/build-win-vs"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).ProviderPath
Set-Location $repoRoot

function Resolve-CMakePath {
    $script = Join-Path $PSScriptRoot "resolve-cmake.ps1"
    $raw = & $script -AsJson
    $exit = if (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue) { [int]$global:LASTEXITCODE } else { 0 }
    if ($exit -ne 0) {
        throw "resolve-cmake.ps1 failed."
    }

    $parsed = $raw | ConvertFrom-Json
    return $parsed
}

function Get-LastExitCodeOrZero {
    if (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue) {
        return [int]$global:LASTEXITCODE
    }

    return 0
}

function Resolve-ProviderAbsolutePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Path cannot be empty."
    }

    $candidate = if ([System.IO.Path]::IsPathRooted($Path)) {
        $Path
    }
    else {
        Join-Path (Get-Location) $Path
    }

    return [System.IO.Path]::GetFullPath($candidate)
}

function Resolve-HostArtifact {
    param(
        [Parameter(Mandatory = $true)][string]$OutDir,
        [Parameter(Mandatory = $true)][string]$Config
    )

    $searchCount = 0
    $expectedArtifacts = @(
        (Join-Path $OutDir "SwfocExtender.Bridge\\$Config\\SwfocExtender.Host.exe"),
        (Join-Path $OutDir "SwfocExtender.Bridge\\SwfocExtender.Host.exe"),
        (Join-Path $OutDir "SwfocExtender.Bridge\\x64\\$Config\\SwfocExtender.Host.exe"),
        (Join-Path $OutDir "x64\\$Config\\SwfocExtender.Host.exe"),
        (Join-Path $OutDir "$Config\\SwfocExtender.Host.exe"),
        (Join-Path $OutDir "SwfocExtender.Host.exe")
    )

    foreach ($path in $expectedArtifacts) {
        $searchCount += 1
        if (Test-Path -Path $path) {
            return [PSCustomObject]@{
                Path = $path
                SearchCount = $searchCount
                Source = "expected_path"
            }
        }
    }

    $recursiveHits = @(Get-ChildItem -Path $OutDir -Filter "SwfocExtender.Host.exe" -File -Recurse -ErrorAction SilentlyContinue)
    $searchCount += $recursiveHits.Count
    $recursiveArtifact = $recursiveHits | Select-Object -ExpandProperty FullName -First 1
    if (-not [string]::IsNullOrWhiteSpace($recursiveArtifact)) {
        return [PSCustomObject]@{
            Path = $recursiveArtifact
            SearchCount = $searchCount
            Source = "recursive_scan"
        }
    }

    return [PSCustomObject]@{
        Path = $null
        SearchCount = $searchCount
        Source = "not_found"
    }
}

function Resolve-WindowsGeneratorPlan {
    param(
        [string]$Configuration,
        [string]$RecommendedGenerator,
        [string]$VsInstancePath
    )

    if (-not [string]::IsNullOrWhiteSpace($RecommendedGenerator)) {
        $generatorArgs = @("-G", $RecommendedGenerator, "-A", "x64")
        if (-not [string]::IsNullOrWhiteSpace($VsInstancePath) -and (Test-Path -Path $VsInstancePath)) {
            $generatorArgs += "-DCMAKE_GENERATOR_INSTANCE=$VsInstancePath"
        }

        return [PSCustomObject]@{
            Name = $RecommendedGenerator
            Args = $generatorArgs
            UsesMultiConfig = $true
        }
    }

    $hasNinja = $null -ne (Get-Command ninja -ErrorAction SilentlyContinue)
    if ($hasNinja) {
        return [PSCustomObject]@{
            Name = "Ninja"
            Args = @("-G", "Ninja", "-DCMAKE_BUILD_TYPE=$Configuration")
            UsesMultiConfig = $false
        }
    }

    throw "No compatible Windows CMake generator detected. Install Visual Studio Build Tools with VC workload or install Ninja."
}

function Invoke-WindowsBuild {
    param(
        [string]$CmakePath,
        [string]$Config,
        [string]$OutDir,
        [string]$RecommendedGenerator,
        [string]$VsInstancePath,
        [string]$VsProductLineVersion
    )

    if ([string]::IsNullOrWhiteSpace($CmakePath)) {
        throw "cmake path is required for Windows build."
    }

    & $CmakePath --version
    if ((Get-LastExitCodeOrZero) -ne 0) {
        throw "cmake --version failed for '$CmakePath'."
    }

    $resolvedOutDir = Resolve-ProviderAbsolutePath -Path $OutDir
    $generatorPlan = Resolve-WindowsGeneratorPlan `
        -Configuration $Config `
        -RecommendedGenerator $RecommendedGenerator `
        -VsInstancePath $VsInstancePath

    $expectedGenerator = [string]$generatorPlan.Name
    $configureArgs = @("-S", "native", "-B", $resolvedOutDir)
    $configureArgs += @($generatorPlan.Args)
    Write-Output "Windows native configure generator: $expectedGenerator"
    Write-Output "resolvedBuildDir: $resolvedOutDir"
    Write-Output "target: SwfocExtender.Host"
    if (-not [string]::IsNullOrWhiteSpace($VsProductLineVersion)) {
        Write-Output "Visual Studio product line: $VsProductLineVersion"
    }
    if (-not [string]::IsNullOrWhiteSpace($VsInstancePath)) {
        Write-Output "Visual Studio instance: $VsInstancePath"
    }

    $cachePath = Join-Path $resolvedOutDir "CMakeCache.txt"
    if (Test-Path -Path $cachePath) {
        $cacheLine = Select-String -Path $cachePath -Pattern '^CMAKE_GENERATOR:INTERNAL=(.+)$' | Select-Object -First 1
        if ($null -ne $cacheLine) {
            $cachedGenerator = $cacheLine.Matches[0].Groups[1].Value
            if (-not [string]::Equals($cachedGenerator, $expectedGenerator, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-Warning "CMake generator changed from '$cachedGenerator' to '$expectedGenerator'; clearing stale cache in '$resolvedOutDir'."
                Remove-Item -Path $cachePath -Force -ErrorAction SilentlyContinue
                Remove-Item -Path (Join-Path $resolvedOutDir "CMakeFiles") -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    & $CmakePath @configureArgs
    if ((Get-LastExitCodeOrZero) -ne 0) {
        throw "native configure failed for Windows mode."
    }

    $buildArgs = @("--build", $resolvedOutDir, "--target", "SwfocExtender.Host")
    if ([bool]$generatorPlan.UsesMultiConfig) {
        $buildArgs += @("--config", $Config)
    }

    & $CmakePath @buildArgs
    if ((Get-LastExitCodeOrZero) -ne 0) {
        throw "native build failed for Windows mode."
    }

    $artifactResolution = Resolve-HostArtifact -OutDir $resolvedOutDir -Config $Config
    if ([string]::IsNullOrWhiteSpace($artifactResolution.Path)) {
        Write-Warning "Host artifact not found on first scan; retrying target build with verbose output."
        $fallbackBuildArgs = @("--build", $resolvedOutDir, "--target", "SwfocExtender.Host", "--verbose")
        if ([bool]$generatorPlan.UsesMultiConfig) {
            $fallbackBuildArgs += @("--config", $Config)
        }

        & $CmakePath @fallbackBuildArgs
        if ((Get-LastExitCodeOrZero) -ne 0) {
            throw "native fallback build failed for Windows mode."
        }

        $artifactResolution = Resolve-HostArtifact -OutDir $resolvedOutDir -Config $Config
    }

    $artifact = [string]$artifactResolution.Path
    if ([string]::IsNullOrWhiteSpace($artifact)) {
        throw "native build completed but SwfocExtender.Host.exe artifact was not found under '$resolvedOutDir'."
    }

    Write-Output "Windows host artifact: $artifact"
    Write-Output "artifactSearchCount: $($artifactResolution.SearchCount)"
    Write-Output "artifactResolvedPath: $artifact"
    Write-Output "artifactResolutionSource: $($artifactResolution.Source)"

    $runtimeDir = Join-Path $repoRoot "native/runtime"
    if (-not (Test-Path -Path $runtimeDir)) {
        New-Item -Path $runtimeDir -ItemType Directory -Force | Out-Null
    }

    $runtimeArtifact = Join-Path $runtimeDir "SwfocExtender.Host.exe"
    Copy-Item -Path $artifact -Destination $runtimeArtifact -Force
    Write-Output "Runtime host artifact: $runtimeArtifact"
}

function Invoke-WslBuild {
    param(
        [string]$Config
    )

    $script = Join-Path $repoRoot "tools/native/build-native.sh"
    $linuxScript = $script.Replace("\\", "/")
    $linuxRoot = ([string]$repoRoot).Replace("\\", "/")
    $quotedCommand = "bash '$linuxScript' '$linuxRoot/native/build-wsl' '$Config'"

    & wsl.exe -e bash -lc $quotedCommand
    if ((Get-LastExitCodeOrZero) -ne 0) {
        throw "native build failed for WSL mode."
    }
}

$resolved = Resolve-CMakePath

switch ($Mode) {
    "Windows" {
        if (-not [bool]$resolved.found) {
            throw "Windows mode requested but cmake was not found."
        }

        Invoke-WindowsBuild `
            -CmakePath ([string]$resolved.primary) `
            -Config $Configuration `
            -OutDir $BuildDir `
            -RecommendedGenerator ([string]$resolved.recommendedGenerator) `
            -VsInstancePath ([string]$resolved.vsInstancePath) `
            -VsProductLineVersion ([string]$resolved.vsProductLineVersion)
    }
    "Wsl" {
        Invoke-WslBuild -Config $Configuration
    }
    default {
        if ($resolved.found) {
            try {
                Invoke-WindowsBuild `
                    -CmakePath ([string]$resolved.primary) `
                    -Config $Configuration `
                    -OutDir $BuildDir `
                    -RecommendedGenerator ([string]$resolved.recommendedGenerator) `
                    -VsInstancePath ([string]$resolved.vsInstancePath) `
                    -VsProductLineVersion ([string]$resolved.vsProductLineVersion)
            }
            catch {
                Write-Warning "Windows native build path failed ($($_.Exception.Message)); trying WSL path."
                Invoke-WslBuild -Config $Configuration
            }
        }
        else {
            Invoke-WslBuild -Config $Configuration
        }
    }
}

Write-Output "Native build completed ($Mode mode request, config=$Configuration)."
