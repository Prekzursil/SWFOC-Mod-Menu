param(
    [Parameter(Mandatory = $true)][string]$RepoOwner,
    [Parameter(Mandatory = $true)][string]$RepoName,
    [Parameter(Mandatory = $true)][int]$PullNumber,
    [string]$RosterPath = "config/reviewer-roster.json",
    [string]$Token,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$token = if (-not [string]::IsNullOrWhiteSpace($Token)) {
    $Token
}
elseif (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
    $env:GH_TOKEN
}
elseif (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
    $env:GITHUB_TOKEN
}
else {
    $null
}

if ([string]::IsNullOrWhiteSpace($token)) {
    throw "GitHub API token is required. Set GH_TOKEN or GITHUB_TOKEN."
}

function Get-HttpStatusCode {
    param([System.Exception]$Exception)

    if ($Exception.PSObject.Properties.Name -contains "Response" -and $Exception.Response) {
        $statusCode = $Exception.Response.StatusCode
        if ($statusCode -is [int]) {
            return $statusCode
        }

        try {
            return [int]$statusCode
        }
        catch {
            return $null
        }
    }

    return $null
}

function Invoke-GitHubApi {
    param(
        [Parameter(Mandatory = $true)][string]$Endpoint,
        [ValidateSet("GET", "POST")][string]$Method = "GET",
        [object]$Body
    )

    $uri = "https://api.github.com/$Endpoint"
    $headers = @{
        Authorization = "Bearer $token"
        Accept = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
    }

    try {
        if ($Method -eq "GET") {
            return Invoke-RestMethod -Method Get -Uri $uri -Headers $headers
        }

        $json = if ($null -ne $Body) { $Body | ConvertTo-Json -Depth 10 } else { $null }
        if ($null -eq $json) {
            return Invoke-RestMethod -Method Post -Uri $uri -Headers $headers
        }

        return Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $json -ContentType "application/json"
    }
    catch {
        $statusCode = Get-HttpStatusCode -Exception $_.Exception
        $message = "GitHub API failed ($Method $Endpoint)"
        if ($statusCode) {
            $message += " status=$statusCode"
        }

        $message += ": $($_.Exception.Message)"
        throw $message
    }
}

function Ensure-LabelExists {
    param(
        [Parameter(Mandatory = $true)][string]$Owner,
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$LabelName,
        [switch]$NoMutation
    )

    $escapedLabel = [Uri]::EscapeDataString($LabelName)
    try {
        [void](Invoke-GitHubApi -Endpoint "repos/$Owner/$Repo/labels/$escapedLabel")
        return "existing"
    }
    catch {
        if ($_.Exception.Message -notmatch "status=404") {
            throw
        }

        if ($NoMutation) {
            return "would-create"
        }

        [void](Invoke-GitHubApi -Method "POST" -Endpoint "repos/$Owner/$Repo/labels" -Body @{
            name        = $LabelName
            color       = "d73a4a"
            description = "No eligible non-author reviewer available"
        })

        return "created"
    }
}

function Add-FallbackCommentIfMissing {
    param(
        [Parameter(Mandatory = $true)][string]$Owner,
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][int]$Number,
        [Parameter(Mandatory = $true)][string]$Marker,
        [Parameter(Mandatory = $true)][string]$Body,
        [switch]$NoMutation
    )

    $comments = @(Invoke-GitHubApi -Endpoint "repos/$Owner/$Repo/issues/$Number/comments?per_page=100")
    foreach ($comment in $comments) {
        if (-not [string]::IsNullOrWhiteSpace($comment.body) -and ($comment.body -like "*$Marker*")) {
            return "existing"
        }
    }

    if ($NoMutation) {
        return "would-create"
    }

    [void](Invoke-GitHubApi -Method "POST" -Endpoint "repos/$Owner/$Repo/issues/$Number/comments" -Body @{
        body = $Body
    })

    return "created"
}

$resolvedRosterPath = Resolve-Path -Path $RosterPath -ErrorAction Stop
$roster = Get-Content -Raw -Path $resolvedRosterPath | ConvertFrom-Json

foreach ($key in @("version", "users", "teams", "fallbackLabel", "fallbackCommentEnabled")) {
    if (-not ($roster.PSObject.Properties.Name -contains $key)) {
        throw "Roster file '$resolvedRosterPath' is missing required key '$key'."
    }
}

if ([string]::IsNullOrWhiteSpace($roster.fallbackLabel)) {
    throw "Roster fallbackLabel is required."
}

$pr = Invoke-GitHubApi -Endpoint "repos/$RepoOwner/$RepoName/pulls/$PullNumber"
if (-not $pr -or -not $pr.user -or [string]::IsNullOrWhiteSpace($pr.user.login)) {
    throw "Unable to resolve pull request author for #$PullNumber."
}

$author = $pr.user.login
$configuredUsers = @(@($roster.users) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$configuredTeams = @(@($roster.teams) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

$eligibleUsers = @($configuredUsers |
    Where-Object { -not $_.Equals($author, [System.StringComparison]::OrdinalIgnoreCase) } |
    Sort-Object -Unique)

$eligibleTeams = @($configuredTeams | Sort-Object -Unique)

$requested = $false
$fallbackApplied = $false
$labelStatus = "skipped"
$commentStatus = "skipped"
$reason = ""
$requestError = $null

if ($eligibleUsers.Count -gt 0 -or $eligibleTeams.Count -gt 0) {
    try {
        if ($DryRun) {
            $requested = $true
            $reason = "dry_run_request"
        }
        else {
            [void](Invoke-GitHubApi -Method "POST" -Endpoint "repos/$RepoOwner/$RepoName/pulls/$PullNumber/requested_reviewers" -Body @{
                reviewers = @($eligibleUsers)
                team_reviewers = @($eligibleTeams)
            })

            $requested = $true
            $reason = "reviewers_requested"
        }
    }
    catch {
        $requestError = $_.Exception.Message
        $reason = "reviewer_request_failed"
    }
}

if (-not $requested) {
    $fallbackApplied = $true
    $reason = if ([string]::IsNullOrWhiteSpace($reason)) { "no_eligible_reviewer" } else { "$reason;fallback" }

    $labelStatus = Ensure-LabelExists -Owner $RepoOwner -Repo $RepoName -LabelName $roster.fallbackLabel -NoMutation:$DryRun

    if (-not $DryRun) {
        [void](Invoke-GitHubApi -Method "POST" -Endpoint "repos/$RepoOwner/$RepoName/issues/$PullNumber/labels" -Body @{
            labels = @($roster.fallbackLabel)
        })
    }
    elseif ($labelStatus -eq "existing") {
        $labelStatus = "would-add"
    }

    if ([bool]$roster.fallbackCommentEnabled) {
        $marker = "<!-- reviewer-automation:needs-reviewer -->"
        $body = @"
$marker
Reviewer automation could not request a non-author reviewer for this PR.

- PR author: @$author
- configured users: $($configuredUsers -join ', ')
- configured teams: $($configuredTeams -join ', ')
- fallback label applied: $($roster.fallbackLabel)

Next steps:
1. Add at least one non-author collaborator/team to `config/reviewer-roster.json`.
2. Re-run reviewer automation (or request review manually).
"@

        $commentStatus = Add-FallbackCommentIfMissing -Owner $RepoOwner -Repo $RepoName -Number $PullNumber -Marker $marker -Body $body -NoMutation:$DryRun
    }
}

$result = [ordered]@{
    repository         = "$RepoOwner/$RepoName"
    pullNumber         = $PullNumber
    author             = $author
    dryRun             = [bool]$DryRun
    requested          = $requested
    requestedUsers     = @($eligibleUsers)
    requestedTeams     = @($eligibleTeams)
    fallbackApplied    = $fallbackApplied
    fallbackLabel      = $roster.fallbackLabel
    labelStatus        = $labelStatus
    commentStatus      = $commentStatus
    reason             = $reason
    requestError       = $requestError
    rosterPath         = $resolvedRosterPath.Path
    timestampUtc       = (Get-Date).ToUniversalTime().ToString("o")
}

$result | ConvertTo-Json -Depth 6
