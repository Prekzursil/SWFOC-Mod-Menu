param(
    [Parameter(Mandatory = $true)][string]$ProfileId,
    [Parameter(Mandatory = $true)][string]$OutPath,
    [string]$StoryRoot = "",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot ".." "..")).ProviderPath
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($StoryRoot)) {
    $StoryRoot = Join-Path $repoRoot "tools/fixtures/story_flow/$ProfileId"
}

if (-not (Test-Path -Path $StoryRoot)) {
    throw "StoryRoot does not exist: $StoryRoot"
}

$flowAssembly = Join-Path $repoRoot "src/SwfocTrainer.Flow/bin/Release/net8.0/SwfocTrainer.Flow.dll"
$dataIndexAssembly = Join-Path $repoRoot "src/SwfocTrainer.DataIndex/bin/Release/net8.0/SwfocTrainer.DataIndex.dll"
if (-not (Test-Path -Path $flowAssembly) -or -not (Test-Path -Path $dataIndexAssembly)) {
    dotnet build SwfocTrainer.sln -c Release --no-restore | Out-Null
}

Add-Type -Path $dataIndexAssembly
Add-Type -Path $flowAssembly

$extractor = [SwfocTrainer.Flow.Services.StoryPlotFlowExtractor]::new()
$exporter = [SwfocTrainer.Flow.Services.StoryFlowGraphExporter]::new()
$plots = New-Object 'System.Collections.Generic.List[SwfocTrainer.Flow.Models.FlowPlotRecord]'
$diagnostics = New-Object 'System.Collections.Generic.List[string]'

$xmlFiles = Get-ChildItem -Path $StoryRoot -Recurse -File -Filter *.xml | Sort-Object FullName
foreach ($file in $xmlFiles) {
    $relative = [System.IO.Path]::GetRelativePath($StoryRoot, $file.FullName).Replace('\', '/')
    $content = Get-Content -Raw -Path $file.FullName
    $partial = $extractor.Extract($content, $relative)
    foreach ($plot in $partial.Plots) {
        [void]$plots.Add($plot)
    }
    foreach ($diagnostic in $partial.Diagnostics) {
        [void]$diagnostics.Add($diagnostic)
    }
}

$flowReport = [SwfocTrainer.Flow.Models.FlowIndexReport]::new($plots.ToArray(), $diagnostics.ToArray())
$graph = $exporter.Build($flowReport)
if ($Strict -and $graph.Nodes.Count -eq 0) {
    throw "Strict mode failed: story flow graph has zero nodes."
}

$outDirectory = Split-Path -Parent $OutPath
if (-not [string]::IsNullOrWhiteSpace($outDirectory) -and -not (Test-Path -Path $outDirectory)) {
    New-Item -Path $outDirectory -ItemType Directory | Out-Null
}

$payload = [ordered]@{
    profileId = $ProfileId
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    storyRoot = $StoryRoot
    nodeCount = $graph.Nodes.Count
    edgeCount = $graph.Edges.Count
    nodes = @($graph.Nodes)
    edges = @($graph.Edges)
    diagnostics = @($graph.Diagnostics)
}
$payload | ConvertTo-Json -Depth 10 | Set-Content -Path $OutPath

$markdownPath = [System.IO.Path]::ChangeExtension($OutPath, ".md")
$markdown = $exporter.BuildMarkdownSummary($ProfileId, $graph)
Set-Content -Path $markdownPath -Value $markdown

Write-Host "story-flow-graph exported to $OutPath"
Write-Host "story-flow markdown summary exported to $markdownPath"
