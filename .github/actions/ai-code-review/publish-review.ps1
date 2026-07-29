[CmdletBinding()]
param(
    [AllowEmptyString()]
    [string] $ResultPath,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ReviewJobResult,

    [ValidateNotNullOrEmpty()]
    [string] $Model = 'openai/gpt-4.1-mini',

    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$commentMarker = '<!-- ai-cr:summary:v1 -->'
$maximumCommentLength = 60000
$labels = @(
    @{ Name = 'ai-cr:review'; Color = '5319e7'; Description = 'Request another advisory AI code review.' },
    @{ Name = 'ai-cr:passed'; Color = '0e8a16'; Description = 'The latest advisory AI code review passed.' },
    @{ Name = 'ai-cr:failed'; Color = 'd93f0b'; Description = 'The latest advisory AI code review found concerns.' },
    @{ Name = 'ai-cr:error'; Color = 'b60205'; Description = 'The AI code review automation failed.' }
)

function Invoke-GitHubApi {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('GET', 'POST', 'PATCH', 'DELETE')]
        [string] $Method,

        [Parameter(Mandatory)]
        [string] $Path,

        [AllowNull()]
        [object] $Body,

        [switch] $IgnoreNotFound
    )

    $parameters = @{
        Method = $Method
        Uri = "https://api.github.com$Path"
        Headers = @{
            Accept = 'application/vnd.github+json'
            Authorization = "Bearer $($env:GITHUB_TOKEN)"
            'X-GitHub-Api-Version' = '2022-11-28'
        }
    }

    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = $Body | ConvertTo-Json -Depth 20 -Compress
    }

    try {
        return Invoke-RestMethod @parameters
    }
    catch {
        $statusCode = [int] $_.Exception.Response.StatusCode
        if ($IgnoreNotFound -and $statusCode -eq 404) {
            return $null
        }

        throw "GitHub API request failed with status $statusCode for $Method $Path."
    }
}

function ConvertTo-SafeMarkdown {
    param(
        [AllowNull()]
        [string] $Value,

        [int] $MaximumLength = 1000
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return '_None_'
    }

    $bounded = if ($Value.Length -le $MaximumLength) {
        $Value
    }
    else {
        $Value.Substring(0, $MaximumLength) + '...'
    }

    return $bounded.
        Replace('&', '&amp;').
        Replace('<', '&lt;').
        Replace('>', '&gt;').
        Replace('|', '\|').
        Replace("`r", ' ').
        Replace("`n", ' ')
}

function Ensure-Labels {
    param(
        [Parameter(Mandatory)]
        [string] $Repository
    )

    foreach ($label in $labels) {
        $encodedName = [uri]::EscapeDataString($label.Name)
        $existing = Invoke-GitHubApi `
            -Method GET `
            -Path "/repos/$Repository/labels/$encodedName" `
            -IgnoreNotFound

        $body = @{
            name = $label.Name
            color = $label.Color
            description = $label.Description
        }

        if ($null -eq $existing) {
            [void] (Invoke-GitHubApi -Method POST -Path "/repos/$Repository/labels" -Body $body)
        }
        elseif ($existing.color -cne $label.Color -or $existing.description -cne $label.Description) {
            [void] (Invoke-GitHubApi `
                -Method PATCH `
                -Path "/repos/$Repository/labels/$encodedName" `
                -Body $body)
        }
    }
}

function Add-Label {
    param(
        [Parameter(Mandatory)]
        [string] $Repository,

        [Parameter(Mandatory)]
        [int] $PullRequestNumber,

        [Parameter(Mandatory)]
        [string] $Name
    )

    [void] (Invoke-GitHubApi `
        -Method POST `
        -Path "/repos/$Repository/issues/$PullRequestNumber/labels" `
        -Body @{ labels = @($Name) })
}

function Remove-Label {
    param(
        [Parameter(Mandatory)]
        [string] $Repository,

        [Parameter(Mandatory)]
        [int] $PullRequestNumber,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $encodedName = [uri]::EscapeDataString($Name)
    [void] (Invoke-GitHubApi `
        -Method DELETE `
        -Path "/repos/$Repository/issues/$PullRequestNumber/labels/$encodedName" `
        -IgnoreNotFound)
}

function Set-MarkerComment {
    param(
        [Parameter(Mandatory)]
        [string] $Repository,

        [Parameter(Mandatory)]
        [int] $PullRequestNumber,

        [Parameter(Mandatory)]
        [string] $Body
    )

    if ($Body.Length -gt $maximumCommentLength) {
        throw 'Rendered review comment exceeds the trusted size limit.'
    }

    $existingComment = $null
    for ($page = 1; $null -eq $existingComment; $page++) {
        $comments = @(
            Invoke-GitHubApi `
                -Method GET `
                -Path "/repos/$Repository/issues/$PullRequestNumber/comments?per_page=100&page=$page"
        )

        $existingComment = $comments |
            Where-Object {
                $_.user.login -ceq 'github-actions[bot]' -and
                ([string] $_.body).Contains($commentMarker)
            } |
            Select-Object -First 1

        if ($comments.Count -lt 100) {
            break
        }
    }

    if ($null -eq $existingComment) {
        [void] (Invoke-GitHubApi `
            -Method POST `
            -Path "/repos/$Repository/issues/$PullRequestNumber/comments" `
            -Body @{ body = $Body })
    }
    else {
        [void] (Invoke-GitHubApi `
            -Method PATCH `
            -Path "/repos/$Repository/issues/comments/$($existingComment.id)" `
            -Body @{ body = $Body })
    }
}

function Get-ReviewComment {
    param(
        [Parameter(Mandatory)]
        [object] $Result,

        [Parameter(Mandatory)]
        [string] $Verdict,

        [Parameter(Mandatory)]
        [string] $RunUrl
    )

    $rows = foreach ($criterion in $Result.criteria) {
        $evidence = @($criterion.evidence | ForEach-Object {
            "$(ConvertTo-SafeMarkdown $_.changedPath 260): $(ConvertTo-SafeMarkdown $_.finding 500)"
        })
        $detail = if ($criterion.score -eq 'N/A') {
            ConvertTo-SafeMarkdown $criterion.naReason 300
        }
        elseif ($evidence.Count -eq 0) {
            '_No evidence_'
        }
        else {
            $evidence -join '<br>'
        }

        "| ``$(ConvertTo-SafeMarkdown $criterion.id 80)`` | $($criterion.score) | $detail |"
    }

    $limitations = @($Result.analysis.limitations | ForEach-Object {
        "- $(ConvertTo-SafeMarkdown $_ 300)"
    })
    if ($limitations.Count -eq 0) {
        $limitations = @('- None')
    }

    $blockers = @($Result.blockers | ForEach-Object {
        "- **``$(ConvertTo-SafeMarkdown $_.criterionId 80)``** at " +
            "``$(ConvertTo-SafeMarkdown $_.changedPath 260)``: " +
            (ConvertTo-SafeMarkdown $_.finding 500)
    })
    if ($blockers.Count -eq 0) {
        $blockers = @('- None')
    }

    return @"
$commentMarker
## Advisory AI code review: $Verdict

- **Reviewed head:** ``$($Result.reviewedHeadSha)``
- **Model:** ``$(ConvertTo-SafeMarkdown $Model 100)``
- **Analysis complete:** ``$($Result.analysis.complete.ToString().ToLowerInvariant())``
- **CI signal:** unavailable (static diff review only)
- **Workflow run:** [Open run]($RunUrl)

### Summary

$(ConvertTo-SafeMarkdown $Result.summary 1000)

### Limitations

$($limitations -join [Environment]::NewLine)

### Scores

| Criterion | Score | Evidence |
| --- | ---: | --- |
$($rows -join [Environment]::NewLine)

### Blockers

$($blockers -join [Environment]::NewLine)
"@
}

function Get-ErrorComment {
    param(
        [Parameter(Mandatory)]
        [string] $RunUrl,

        [Parameter(Mandatory)]
        [string] $Category
    )

    return @"
$commentMarker
## Advisory AI code review: automation error

The review could not be published because the **$Category** stage failed. The
last `ai-cr:passed` or `ai-cr:failed` label was preserved.

- **CI signal:** unavailable (static diff review only)
- **Workflow run:** [Open run]($RunUrl)
"@
}

if ($ValidateOnly) {
    if ([string]::IsNullOrWhiteSpace($ResultPath)) {
        Write-Error 'ResultPath is required for validation.'
        exit 1
    }

    try {
        $validatorPath = Join-Path $PSScriptRoot 'validate-review-result.ps1'
        $validationOutput = & $validatorPath -ResultPath $ResultPath
        if ($validationOutput -notin @('passed', 'failed')) {
            throw 'Unexpected validator output.'
        }

        Write-Output $validationOutput
        return
    }
    catch {
        Write-Error 'The review result failed trusted validation.'
        exit 1
    }
}

if ([string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN) -or
    [string]::IsNullOrWhiteSpace($env:GITHUB_EVENT_PATH) -or
    [string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) {
    Write-Error 'GITHUB_TOKEN, GITHUB_EVENT_PATH, and GITHUB_REPOSITORY are required.'
    exit 1
}

$event = Get-Content -LiteralPath $env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json -Depth 100
$pullRequestNumber = [int] $event.number
$repository = $env:GITHUB_REPOSITORY
$runUrl = "https://github.com/$repository/actions/runs/$($env:GITHUB_RUN_ID)"
$retryTriggered = $event.action -ceq 'labeled' -and $event.label.name -ceq 'ai-cr:review'
$automationError = $false

try {
    Ensure-Labels -Repository $repository

    if ($ReviewJobResult -cne 'success') {
        throw 'review'
    }

    if ([string]::IsNullOrWhiteSpace($ResultPath) -or -not (Test-Path -LiteralPath $ResultPath -PathType Leaf)) {
        throw 'artifact'
    }

    $resultJson = Get-Content -LiteralPath $ResultPath -Raw
    try {
        $result = $resultJson | ConvertFrom-Json -Depth 100
    }
    catch {
        throw 'schema'
    }

    $validatorPath = Join-Path $PSScriptRoot 'validate-review-result.ps1'
    try {
        $validationOutput = & $validatorPath -ResultPath $ResultPath 2>$null
    }
    catch {
        throw 'schema'
    }

    if ($validationOutput -notin @('passed', 'failed')) {
        throw 'schema'
    }

    $pullRequest = Invoke-GitHubApi `
        -Method GET `
        -Path "/repos/$repository/pulls/$pullRequestNumber"

    if ([string] $pullRequest.head.sha -ine [string] $result.reviewedHeadSha) {
        Write-Output 'Ignoring stale AI review result; the pull request head has changed.'
        exit 0
    }

    $verdict = [string] $validationOutput
    $comment = Get-ReviewComment -Result $result -Verdict $verdict -RunUrl $runUrl
    Set-MarkerComment `
        -Repository $repository `
        -PullRequestNumber $pullRequestNumber `
        -Body $comment

    if ($verdict -ceq 'passed') {
        Add-Label -Repository $repository -PullRequestNumber $pullRequestNumber -Name 'ai-cr:passed'
        Remove-Label -Repository $repository -PullRequestNumber $pullRequestNumber -Name 'ai-cr:failed'
    }
    else {
        Add-Label -Repository $repository -PullRequestNumber $pullRequestNumber -Name 'ai-cr:failed'
        Remove-Label -Repository $repository -PullRequestNumber $pullRequestNumber -Name 'ai-cr:passed'
    }

    Remove-Label -Repository $repository -PullRequestNumber $pullRequestNumber -Name 'ai-cr:error'
    Write-Output "Published advisory AI review with verdict '$verdict'."
}
catch {
    $automationError = $true
    $category = if ($_.Exception.Message -in @('review', 'artifact', 'schema')) {
        $_.Exception.Message
    }
    else {
        'publishing'
    }

    try {
        $comment = Get-ErrorComment -RunUrl $runUrl -Category $category
        Set-MarkerComment `
            -Repository $repository `
            -PullRequestNumber $pullRequestNumber `
            -Body $comment
        Add-Label -Repository $repository -PullRequestNumber $pullRequestNumber -Name 'ai-cr:error'
    }
    catch {
        Write-Error 'The publishing error could not be reported to the pull request.'
    }

    Write-Error "AI code review automation failed in the '$category' stage."
}
finally {
    if ($retryTriggered) {
        try {
            Remove-Label `
                -Repository $repository `
                -PullRequestNumber $pullRequestNumber `
                -Name 'ai-cr:review'
        }
        catch {
            Write-Error 'The retry label could not be consumed.'
            $automationError = $true
        }
    }
}

if ($automationError) {
    exit 1
}
