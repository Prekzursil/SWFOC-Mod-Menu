param(
    [string]$Configuration = "Release",
    [switch]$DeterministicOnly,
    [string]$ResultsRoot = "TestResults/coverage"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectPath = "tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj"
$resultsRootResolved = Resolve-Path -LiteralPath "." | ForEach-Object { Join-Path $_.Path $ResultsRoot }
if (Test-Path -Path $resultsRootResolved) {
    Remove-Item -Path $resultsRootResolved -Recurse -Force
}
New-Item -ItemType Directory -Path $resultsRootResolved | Out-Null

$rawResults = Join-Path $env:TEMP "swfoctrainer-coverage-raw"
if (Test-Path -Path $rawResults) {
    Remove-Item -Path $rawResults -Recurse -Force
}
New-Item -ItemType Directory -Path $rawResults | Out-Null

$filter = if ($DeterministicOnly) {
    'FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests'
}
else {
    ''
}

$arguments = @(
    "test",
    $projectPath,
    "-c", $Configuration,
    "--logger", "trx;LogFileName=coverage.trx",
    "-p:CollectCoverage=true",
    "-p:CoverletOutputFormat=cobertura",
    "-p:CoverletOutput=`"$(Join-Path $rawResults 'coverage')`"",
    "-p:ExcludeByFile=**/obj/**%2c**/*.g.cs%2c**/*.g.i.cs"
)

if (-not [string]::IsNullOrWhiteSpace($filter)) {
    $arguments += @("--filter", $filter)
}

function Resolve-DotnetCommand {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $dotnet) {
        return $dotnet.Source
    }

    $candidates = @(
        (Join-Path $env:USERPROFILE ".dotnet\\dotnet.exe"),
        (Join-Path $env:ProgramFiles "dotnet\\dotnet.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -Path $candidate) {
            return $candidate
        }
    }

    throw "Could not resolve dotnet executable. Install .NET SDK or add dotnet to PATH."
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

$expectedRawCoverage = Join-Path $rawResults "coverage.cobertura.xml"
for ($attempt = 0; $attempt -lt 400 -and -not (Test-Path -Path $expectedRawCoverage); $attempt++) {
    Start-Sleep -Milliseconds 250
}

if (-not (Test-Path -Path $expectedRawCoverage)) {
    throw "No coverage.cobertura.xml file was generated under $rawResults."
}

$primaryCoveragePath = $expectedRawCoverage
$targetCoverage = Join-Path $resultsRootResolved "cobertura.xml"
Copy-Item -Path $primaryCoveragePath -Destination $targetCoverage -Force

Write-Output "coverage_source=$primaryCoveragePath"
Write-Output "coverage_path=$targetCoverage"
