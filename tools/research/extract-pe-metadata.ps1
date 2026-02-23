param(
    [Parameter(Mandatory = $true)][string]$ModulePath,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$moduleFullPath = [System.IO.Path]::GetFullPath($ModulePath)
if (-not (Test-Path -Path $moduleFullPath)) {
    throw "Module not found: $moduleFullPath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = [System.IO.Path]::ChangeExtension($moduleFullPath, ".metadata.json")
}

$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($moduleFullPath)
$file = Get-Item -LiteralPath $moduleFullPath

$result = [ordered]@{
    schemaVersion = "1.0"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    modulePath = $moduleFullPath
    moduleName = $file.Name
    lengthBytes = $file.Length
    createdUtc = $file.CreationTimeUtc.ToString("o")
    lastWriteUtc = $file.LastWriteTimeUtc.ToString("o")
    productName = $version.ProductName
    productVersion = $version.ProductVersion
    fileVersion = $version.FileVersion
    originalFilename = $version.OriginalFilename
    fileDescription = $version.FileDescription
    language = $version.Language
}

($result | ConvertTo-Json -Depth 6) | Set-Content -Path $OutputPath -Encoding UTF8
Write-Output "PE metadata written: $OutputPath"
