param(
    [string]$Configuration = "Release",
    [switch]$DeterministicOnly,
    [string]$ResultsRoot = "TestResults/coverage"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectPath = "tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj"
$resolvedRepoRoot = Resolve-Path -LiteralPath "."
$repoRoot = $resolvedRepoRoot.ProviderPath
$resultsRootResolved = Join-Path $repoRoot $ResultsRoot
if (Test-Path -Path $resultsRootResolved) {
    Remove-Item -Path $resultsRootResolved -Recurse -Force
}
New-Item -ItemType Directory -Path $resultsRootResolved | Out-Null

function Use-NativeWindowsCoverageStaging {
    param([string]$PathValue)

    return $PathValue.StartsWith("\\wsl.localhost\", [System.StringComparison]::OrdinalIgnoreCase)
}

$useNativeWindowsCoverageStaging = Use-NativeWindowsCoverageStaging -PathValue $repoRoot

if ($useNativeWindowsCoverageStaging) {
    $collectorRoot = Join-Path $env:TEMP ("swfoctrainer-coverage-" + [Guid]::NewGuid().ToString("N"))
} else {
    $collectorRoot = $resultsRootResolved
}

if (Test-Path -Path $collectorRoot) {
    Remove-Item -Path $collectorRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $collectorRoot | Out-Null

$testResultsRoot = Join-Path $collectorRoot "dotnet-test-results"
New-Item -ItemType Directory -Path $testResultsRoot | Out-Null

$filter = if ($DeterministicOnly) {
    'FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests'
}
else {
    ''
}

$excludeByFile = '**/obj/**;**/*.g.cs;**/*.g.i.cs'
$runSettingsPath = Join-Path $collectorRoot 'coverage.runsettings'
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

function Invoke-DotnetCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string[]]$InvocationArguments
    )

    Write-Output "$Executable $($InvocationArguments -join ' ')"
    & $Executable @InvocationArguments
}

function Get-LastExitCodeOrZero {
    if (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue) {
        return [int]$global:LASTEXITCODE
    }

    return 0
}

function Stop-WindowsDotnetTestProcesses {
    param([switch]$Enabled)

    if (-not $Enabled) {
        return
    }

    $processNames = @('dotnet', 'testhost', 'vstest.console')
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        $targets = Get-Process -Name $processNames -ErrorAction SilentlyContinue
        if (-not $targets) {
            return
        }

        foreach ($target in $targets) {
            try {
                Stop-Process -Id $target.Id -Force -ErrorAction Stop
            }
            catch {
                Write-Warning "Failed to stop lingering process $($target.ProcessName) ($($target.Id)): $($_.Exception.Message)"
            }
        }

        Start-Sleep -Milliseconds 500
    }

    $remaining = Get-Process -Name $processNames -ErrorAction SilentlyContinue
    if ($remaining) {
        $summary = ($remaining | Sort-Object ProcessName, Id | ForEach-Object { "$($_.ProcessName):$($_.Id)" }) -join ', '
        throw "Unable to clear lingering Windows dotnet/testhost processes before coverage collection: $summary"
    }
}

$dotnetExe = Resolve-DotnetCommand
if (-not $useNativeWindowsCoverageStaging) {
    $previousClinkNoAutorun = [Environment]::GetEnvironmentVariable('CLINK_NOAUTORUN', 'Process')
    [Environment]::SetEnvironmentVariable('CLINK_NOAUTORUN', '1', 'Process')
    try {
        Invoke-DotnetCommand -Executable $dotnetExe -InvocationArguments $arguments
    }
    finally {
        [Environment]::SetEnvironmentVariable('CLINK_NOAUTORUN', $previousClinkNoAutorun, 'Process')
    }

    $exitCode = Get-LastExitCodeOrZero
    if ($exitCode -ne 0) {
        throw "Coverage collection failed with exit code $exitCode."
    }
}

$coverageCandidates = @()
if (-not $useNativeWindowsCoverageStaging) {
    $coverageCandidates = Get-ChildItem -Path $testResultsRoot -Recurse -File -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending
    if (-not $coverageCandidates) {
        $coverageCandidates = Get-ChildItem -Path $testResultsRoot -Recurse -File -Filter 'coverage.xml' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending
    }
}

if (-not $coverageCandidates) {
    $fallbackCoveragePath = Join-Path $collectorRoot 'coverage-msbuild.cobertura.xml'
    $msbuildArguments = @(
        'test',
        $projectPath,
        '-c', $Configuration,
        '--logger', 'trx;LogFileName=coverage-msbuild.trx',
        '--results-directory', $testResultsRoot,
        '/p:CollectCoverage=true',
        '/p:CoverletOutputFormat=cobertura',
        "/p:CoverletOutput=$fallbackCoveragePath",
        "/p:ExcludeByFile=$excludeByFile",
        '/p:ExcludeByAttribute=Obsolete,GeneratedCodeAttribute,CompilerGeneratedAttribute,ExcludeFromCodeCoverageAttribute',
        '/p:UseSourceLink=false'
    )

    if (-not [string]::IsNullOrWhiteSpace($filter)) {
        $msbuildArguments += @('--filter', $filter)
    }

    Stop-WindowsDotnetTestProcesses -Enabled:$useNativeWindowsCoverageStaging

    $previousClinkNoAutorun = [Environment]::GetEnvironmentVariable('CLINK_NOAUTORUN', 'Process')
    [Environment]::SetEnvironmentVariable('CLINK_NOAUTORUN', '1', 'Process')
    try {
        Invoke-DotnetCommand -Executable $dotnetExe -InvocationArguments $msbuildArguments
    }
    finally {
        [Environment]::SetEnvironmentVariable('CLINK_NOAUTORUN', $previousClinkNoAutorun, 'Process')
    }

    $exitCode = Get-LastExitCodeOrZero
    if ($exitCode -ne 0) {
        throw "Coverage collection fallback failed with exit code $exitCode."
    }

    if (Test-Path -Path $fallbackCoveragePath) {
        $coverageCandidates = @(Get-Item -LiteralPath $fallbackCoveragePath)
    }
}

if (-not $coverageCandidates) {
    throw "No coverage report was generated under $testResultsRoot or fallback path $fallbackCoveragePath."
}

$primaryCoveragePath = $coverageCandidates[0].FullName
$targetCoverage = Join-Path $resultsRootResolved 'cobertura.xml'
Copy-Item -Path $primaryCoveragePath -Destination $targetCoverage -Force

$localRunSettingsPath = Join-Path $resultsRootResolved 'coverage.runsettings'
Copy-Item -Path $runSettingsPath -Destination $localRunSettingsPath -Force

Write-Output "coverage_source=$primaryCoveragePath"
Write-Output "coverage_path=$targetCoverage"
