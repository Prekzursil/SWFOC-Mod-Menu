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

$rawResults = Join-Path $resultsRootResolved "raw"
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
    "--no-build",
    "--collect:XPlat Code Coverage",
    "--results-directory", $rawResults,
    "--logger", "trx;LogFileName=coverage.trx"
)

if (-not [string]::IsNullOrWhiteSpace($filter)) {
    $arguments += @("--filter", $filter)
}

$arguments += @(
    "--",
    "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura",
    "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include=[SwfocTrainer.*]*"
)

Write-Output "dotnet $($arguments -join ' ')"
$process = Start-Process -FilePath "dotnet" -ArgumentList $arguments -NoNewWindow -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw "Coverage collection failed with exit code $($process.ExitCode)."
}

$coverageFiles = Get-ChildItem -Path $rawResults -Filter "coverage.cobertura.xml" -Recurse -File
if (@($coverageFiles).Count -eq 0) {
    throw "No coverage.cobertura.xml file was generated under $rawResults"
}

$primaryCoverage = $coverageFiles | Select-Object -First 1
$targetCoverage = Join-Path $resultsRootResolved "cobertura.xml"
Copy-Item -Path $primaryCoverage.FullName -Destination $targetCoverage -Force

Write-Output "coverage_path=$targetCoverage"
