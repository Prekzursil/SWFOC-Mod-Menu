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

$stagingRoot = Join-Path $env:TEMP "swfoctrainer-coverage"
if (Test-Path -Path $stagingRoot) {
    Remove-Item -Path $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingRoot | Out-Null

$rawResults = Join-Path $stagingRoot "raw"
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
    "-p:CoverletOutput=$(Join-Path $rawResults 'coverage.cobertura.xml')",
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

$coverageFiles = Get-ChildItem -Path $rawResults -Filter "coverage.cobertura.xml" -Recurse -File
if (@($coverageFiles).Count -eq 0) {
    throw "No coverage.cobertura.xml file was generated under $rawResults"
}

$primaryCoverage = $coverageFiles | Select-Object -First 1
$targetCoverage = Join-Path $resultsRootResolved "cobertura.xml"
Copy-Item -Path $primaryCoverage.FullName -Destination $targetCoverage -Force

Write-Output "coverage_path=$targetCoverage"
