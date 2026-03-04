param(
    [string]$Configuration = "Release",
    [switch]$DeterministicOnly,
    [string]$ResultsRoot = "TestResults/coverage"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectPath = "tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj"
$repoRoot = (Resolve-Path -LiteralPath ".").Path
$resultsRootResolved = Join-Path $repoRoot $ResultsRoot
if (Test-Path -Path $resultsRootResolved) {
    Remove-Item -Path $resultsRootResolved -Recurse -Force
}
New-Item -ItemType Directory -Path $resultsRootResolved | Out-Null

$testResultsRoot = Join-Path $resultsRootResolved "dotnet-test-results"
if (Test-Path -Path $testResultsRoot) {
    Remove-Item -Path $testResultsRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $testResultsRoot | Out-Null

$filter = if ($DeterministicOnly) {
    'FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests'
}
else {
    ''
}

$excludeByFile = '**/obj/**;**/*.g.cs;**/*.g.i.cs'
$runSettingsPath = Join-Path $resultsRootResolved 'coverage.runsettings'
$runSettingsXml = @"
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat code coverage">
        <Configuration>
          <Format>cobertura</Format>
          <ExcludeByFile>$excludeByFile</ExcludeByFile>
          <ExcludeByAttribute>Obsolete,GeneratedCodeAttribute,CompilerGeneratedAttribute,ExcludeFromCodeCoverageAttribute</ExcludeByAttribute>
          <UseSourceLink>false</UseSourceLink>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
"@
Set-Content -Path $runSettingsPath -Value $runSettingsXml -Encoding UTF8

$arguments = @(
    'test',
    $projectPath,
    '-c', $Configuration,
    '--logger', 'trx;LogFileName=coverage.trx',
    '--results-directory', $testResultsRoot,
    '--collect', 'XPlat Code Coverage',
    '--settings', $runSettingsPath
)

if (-not [string]::IsNullOrWhiteSpace($filter)) {
    $arguments += @('--filter', $filter)
}

function Resolve-DotnetCommand {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $dotnet) {
        return $dotnet.Source
    }

    $candidates = @(
        (Join-Path $env:USERPROFILE '.dotnet\\dotnet.exe'),
        (Join-Path $env:ProgramFiles 'dotnet\\dotnet.exe')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -Path $candidate) {
            return $candidate
        }
    }

    throw 'Could not resolve dotnet executable. Install .NET SDK or add dotnet to PATH.'
}

$dotnetExe = Resolve-DotnetCommand
Write-Output "$dotnetExe $($arguments -join ' ')"
& $dotnetExe @arguments
$exitCode = if (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue) {
    [int]$global:LASTEXITCODE
}
else {
    0
}

if ($exitCode -ne 0) {
    throw "Coverage collection failed with exit code $exitCode."
}

$coverageCandidates = Get-ChildItem -Path $testResultsRoot -Recurse -File -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending
if (-not $coverageCandidates) {
    $coverageCandidates = Get-ChildItem -Path $testResultsRoot -Recurse -File -Filter 'coverage.xml' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending
}

if (-not $coverageCandidates) {
    throw "No coverage report was generated under $testResultsRoot."
}

$primaryCoveragePath = $coverageCandidates[0].FullName
$targetCoverage = Join-Path $resultsRootResolved 'cobertura.xml'
Copy-Item -Path $primaryCoveragePath -Destination $targetCoverage -Force

Write-Output "coverage_source=$primaryCoveragePath"
Write-Output "coverage_path=$targetCoverage"
