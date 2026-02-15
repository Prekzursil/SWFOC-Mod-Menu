param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "artifacts/publish"
$zipPath = Join-Path $repoRoot "artifacts/SwfocTrainer-portable.zip"
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    $fallback = "C:\Program Files\dotnet\dotnet.exe"
    if (Test-Path $fallback) {
        $dotnetExe = $fallback
    } else {
        throw "dotnet executable was not found on PATH and fallback path does not exist."
    }
} else {
    $dotnetExe = $dotnet.Source
}

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

# Build app
& "$dotnetExe" publish "$repoRoot/src/SwfocTrainer.App/SwfocTrainer.App.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $publishDir

# Copy default profile pack
Copy-Item "$repoRoot/profiles" "$publishDir/profiles" -Recurse -Force

# Zip
Compress-Archive -Path "$publishDir/*" -DestinationPath $zipPath -Force
Write-Host "Portable package written: $zipPath"
