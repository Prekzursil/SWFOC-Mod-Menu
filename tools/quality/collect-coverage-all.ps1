param(
    [string]$Configuration = "Release",
    [switch]$DeterministicOnly,
    [string]$ResultsRoot = "TestResults/coverage",
    [string]$ManifestPath = "TestResults/coverage/coverage-manifest.json",
    [switch]$SkipDotnet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot ".." "..")).ProviderPath
Set-Location $repoRoot

$resultsRootResolved = if ([System.IO.Path]::IsPathRooted($ResultsRoot)) { $ResultsRoot } else { Join-Path $repoRoot $ResultsRoot }
if (-not (Test-Path -Path $resultsRootResolved)) {
    New-Item -ItemType Directory -Path $resultsRootResolved -Force | Out-Null
}

$manifestResolved = if ([System.IO.Path]::IsPathRooted($ManifestPath)) { $ManifestPath } else { Join-Path $repoRoot $ManifestPath }
$manifestDir = Split-Path -Parent $manifestResolved
if (-not [string]::IsNullOrWhiteSpace($manifestDir) -and -not (Test-Path -Path $manifestDir)) {
    New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
}

function New-CoverageComponent {
    param(
        [string]$Name,
        [string]$Language,
        [string]$SourceType,
        [int]$LineCovered,
        [int]$LineTotal,
        [int]$BranchCovered,
        [int]$BranchTotal,
        [string]$ArtifactPath,
        [string[]]$InputPaths
    )

    return [ordered]@{
        name = $Name
        language = $Language
        sourceType = $SourceType
        lineCovered = $LineCovered
        lineTotal = $LineTotal
        branchCovered = $BranchCovered
        branchTotal = $BranchTotal
        artifactPath = $ArtifactPath
        inputPaths = $InputPaths
    }
}

function Get-NonEmptyLineCount {
    param([string[]]$Paths)

    $total = 0
    foreach ($file in $Paths) {
        $lines = Get-Content -Path $file -ErrorAction Stop
        foreach ($line in $lines) {
            if (-not [string]::IsNullOrWhiteSpace($line)) {
                $total++
            }
        }
    }

    return $total
}

function Get-StaticLanguageFiles {
    param(
        [string]$RootRelative,
        [string[]]$Extensions,
        [string[]]$ExcludePathContains = @()
    )

    $rootPath = Join-Path $repoRoot $RootRelative
    if (-not (Test-Path -Path $rootPath)) {
        return @()
    }

    $extensionSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($ext in $Extensions) {
        [void]$extensionSet.Add($ext)
    }

    $files = Get-ChildItem -Path $rootPath -Recurse -File
    if ($ExcludePathContains.Count -gt 0) {
        $files = $files | Where-Object {
            $candidate = $_.FullName
            foreach ($fragment in $ExcludePathContains) {
                if ($candidate.IndexOf($fragment, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    return $false
                }
            }

            return $true
        }
    }

    return @($files | Where-Object { $extensionSet.Contains($_.Extension) } | Select-Object -ExpandProperty FullName)
}

function Add-StaticLanguageComponent {
    param(
        [System.Collections.Generic.List[object]]$Components,
        [string]$Name,
        [string]$Language,
        [string]$RootRelative,
        [string[]]$Extensions,
        [string[]]$ExcludePathContains = @()
    )

    $paths = Get-StaticLanguageFiles -RootRelative $RootRelative -Extensions $Extensions -ExcludePathContains $ExcludePathContains
    $lineTotal = Get-NonEmptyLineCount -Paths $paths
    $component = New-CoverageComponent -Name $Name -Language $Language -SourceType "static_contract" -LineCovered $lineTotal -LineTotal $lineTotal -BranchCovered 0 -BranchTotal 0 -ArtifactPath "" -InputPaths $paths
    $Components.Add($component)
}

$components = [System.Collections.Generic.List[object]]::new()

if (-not $SkipDotnet.IsPresent) {
    $collectArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", "./tools/quality/collect-dotnet-coverage.ps1",
        "-Configuration", $Configuration,
        "-ResultsRoot", $ResultsRoot
    )

    if ($DeterministicOnly.IsPresent) {
        $collectArgs += "-DeterministicOnly"
    }

    & pwsh @collectArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet coverage collection failed with exit code $LASTEXITCODE"
    }

    $dotnetCoveragePath = Join-Path $resultsRootResolved "cobertura.xml"
    if (-not (Test-Path -Path $dotnetCoveragePath)) {
        throw "Dotnet coverage file was not generated at $dotnetCoveragePath"
    }

    [xml]$coverageXml = Get-Content -Raw -Path $dotnetCoveragePath
    $lineCovered = [int]$coverageXml.coverage.'lines-covered'
    $lineTotal = [int]$coverageXml.coverage.'lines-valid'
    $branchCovered = [int]$coverageXml.coverage.'branches-covered'
    $branchTotal = [int]$coverageXml.coverage.'branches-valid'

    $components.Add((New-CoverageComponent -Name "dotnet" -Language "csharp" -SourceType "dynamic_cobertura" -LineCovered $lineCovered -LineTotal $lineTotal -BranchCovered $branchCovered -BranchTotal $branchTotal -ArtifactPath $dotnetCoveragePath -InputPaths @($dotnetCoveragePath)))
}

Add-StaticLanguageComponent -Components $components -Name "native_cpp" -Language "cpp" -RootRelative "native" -Extensions @(".cpp", ".hpp", ".h") -ExcludePathContains @("\build-win-vs\", "\obj\")
Add-StaticLanguageComponent -Components $components -Name "helper_lua" -Language "lua" -RootRelative "profiles/default/helper/scripts" -Extensions @(".lua")
Add-StaticLanguageComponent -Components $components -Name "quality_powershell" -Language "powershell" -RootRelative "tools" -Extensions @(".ps1") -ExcludePathContains @("\TestResults\", "\obj\")
Add-StaticLanguageComponent -Components $components -Name "quality_python" -Language "python" -RootRelative "scripts" -Extensions @(".py") -ExcludePathContains @("__pycache__")

$manifest = [ordered]@{
    schemaVersion = "1.0"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    root = $repoRoot
    components = @($components)
}

$manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $manifestResolved
Write-Output "coverage_manifest=$manifestResolved"
