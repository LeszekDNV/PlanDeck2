[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $Model = 'openai/gpt-4.1-mini',

    [ValidateNotNullOrEmpty()]
    [string] $ExpectedBaseBranch = 'develop',

    [ValidateRange(1000, 8000)]
    [int] $InputTokenBudget = 8000,

    [ValidateRange(500, 4000)]
    [int] $OutputTokenBudget = 4000
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$maxTitleLength = 256
$maxBodyLength = 8192
$charactersPerToken = 3
$minimumDiffCharacters = 1000
$credentialPattern = '(?im)(api[_-]?key|client[_-]?secret|password|authorization\s*:|bearer\s+)[^\r\n]{4,}'

function Get-BoundedText {
    param(
        [AllowNull()]
        [string] $Value,

        [Parameter(Mandatory)]
        [int] $MaximumLength
    )

    if ([string]::IsNullOrEmpty($Value)) {
        return ''
    }

    if ($Value.Length -le $MaximumLength) {
        return $Value
    }

    return $Value.Substring(0, $MaximumLength)
}

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & git @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed: git $($Arguments -join ' ')."
    }

    return ($output -join [Environment]::NewLine)
}

function Get-BoundedDiff {
    param(
        [Parameter(Mandatory)]
        [string] $Diff,

        [Parameter(Mandatory)]
        [int] $MaximumLength
    )

    $limitations = [System.Collections.Generic.List[string]]::new()
    $complete = $true
    $redacted = [regex]::IsMatch($Diff, $credentialPattern)
    $safeDiff = [regex]::Replace($Diff, $credentialPattern, '$1 [REDACTED]')

    if ($redacted) {
        $complete = $false
        $limitations.Add('Suspected credential-bearing diff content was redacted.')
    }

    if ($safeDiff -match '(?m)^Binary files .* differ$') {
        $complete = $false
        $limitations.Add('Binary file contents were not reviewable as text.')
    }

    if ($safeDiff.Length -le $MaximumLength) {
        return [pscustomobject]@{
            Text = $safeDiff
            Complete = $complete
            Limitations = @($limitations)
        }
    }

    $sections = @([regex]::Split($safeDiff, '(?m)(?=^diff --git |^@@ )') | Where-Object { $_ })
    $builder = [System.Text.StringBuilder]::new()

    foreach ($section in $sections) {
        if ($builder.Length + $section.Length -gt $MaximumLength) {
            break
        }

        [void] $builder.Append($section)
    }

    $complete = $false
    $limitations.Add('The textual diff exceeded the one-call input budget and was truncated at a file boundary.')

    return [pscustomobject]@{
        Text = $builder.ToString()
        Complete = $complete
        Limitations = @($limitations)
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_EVENT_PATH)) {
        throw 'GITHUB_EVENT_PATH is required.'
    }

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        throw 'GITHUB_TOKEN is required.'
    }

    $event = Get-Content -LiteralPath $env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json -Depth 100
    if ($null -eq $event.pull_request) {
        throw 'The event does not contain pull request metadata.'
    }

    if ($event.pull_request.draft) {
        throw 'Draft pull requests are not eligible for AI review.'
    }

    $baseBranch = [string] $event.pull_request.base.ref
    if ($baseBranch -cne $ExpectedBaseBranch) {
        throw "Pull request targets '$baseBranch', expected '$ExpectedBaseBranch'."
    }

    $pullRequestNumber = [int] $event.number
    $baseSha = [string] $event.pull_request.base.sha
    $headSha = [string] $event.pull_request.head.sha
    if ($baseSha -notmatch '^[0-9a-fA-F]{40}$' -or $headSha -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'Pull request base or head SHA is invalid.'
    }

    Write-Output 'Collecting trusted pull request metadata.'
    [void] (Invoke-Git -Arguments @(
        'fetch',
        '--no-tags',
        '--no-recurse-submodules',
        'origin',
        "+refs/heads/$baseBranch`:refs/remotes/ai-code-review/base-$pullRequestNumber",
        "+refs/pull/$pullRequestNumber/head:refs/remotes/ai-code-review/pr-$pullRequestNumber"
    ))

    if ((Invoke-Git -Arguments @('rev-parse', '--is-shallow-repository')).Trim() -eq 'true') {
        # --unshallow must be used without explicit refspecs; fetch all history first,
        # then the refs created above remain available for the diff.
        [void] (Invoke-Git -Arguments @(
            'fetch',
            '--no-tags',
            '--no-recurse-submodules',
            '--unshallow',
            'origin'
        ))
    }

    $fetchedHeadSha = (Invoke-Git -Arguments @(
        'rev-parse',
        "refs/remotes/ai-code-review/pr-$pullRequestNumber"
    )).Trim()

    if ($fetchedHeadSha -ine $headSha) {
        throw "Fetched pull request head SHA does not match event metadata."
    }

    $diff = Invoke-Git -Arguments @(
        'diff',
        '--no-ext-diff',
        '--no-textconv',
        "$baseSha...$headSha"
    )

    $policy = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'review-policy.md') -Raw
    $procedure = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'review-prompt.md') -Raw
    $schema = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'review-result.schema.json') -Raw |
        ConvertFrom-Json -Depth 100

    $rawTitle = [string] $event.pull_request.title
    $rawBody = [string] $event.pull_request.body
    $titleWasRedacted = [regex]::IsMatch($rawTitle, $credentialPattern)
    $bodyWasRedacted = [regex]::IsMatch($rawBody, $credentialPattern)
    $safeTitle = [regex]::Replace($rawTitle, $credentialPattern, '$1 [REDACTED]')
    $safeBody = [regex]::Replace($rawBody, $credentialPattern, '$1 [REDACTED]')
    $title = Get-BoundedText -Value $safeTitle -MaximumLength $maxTitleLength
    $body = Get-BoundedText -Value $safeBody -MaximumLength $maxBodyLength
    $fixedInput = [pscustomobject]@{
        title = $title
        body = $body
        baseSha = $baseSha
        headSha = $headSha
    } | ConvertTo-Json -Depth 10 -Compress

    $fixedCharacterCount = $policy.Length + $procedure.Length +
        ($schema | ConvertTo-Json -Depth 100 -Compress).Length + $fixedInput.Length
    $maximumInputCharacters = $InputTokenBudget * $charactersPerToken
    $maximumDiffCharacters = $maximumInputCharacters - $fixedCharacterCount
    Write-Output "Token budget: input=$InputTokenBudget output=$OutputTokenBudget."
    Write-Output "Character budget: input=$maximumInputCharacters fixed=$fixedCharacterCount diff=$maximumDiffCharacters."
    if ($maximumDiffCharacters -lt $minimumDiffCharacters) {
        throw 'Trusted policy, schema, and metadata leave insufficient room for a reviewable diff.'
    }

    $boundedDiff = Get-BoundedDiff -Diff $diff -MaximumLength $maximumDiffCharacters
    Write-Output "Diff length: raw=$($diff.Length) bounded=$($boundedDiff.Text.Length) complete=$($boundedDiff.Complete)."
    $trustedLimitations = @($boundedDiff.Limitations)
    if ($titleWasRedacted) {
        $trustedLimitations += 'Suspected credential-bearing pull request title content was redacted.'
    }

    if ($bodyWasRedacted) {
        $trustedLimitations += 'Suspected credential-bearing pull request description content was redacted.'
    }

    if ($title.Length -lt $safeTitle.Length) {
        $trustedLimitations += 'The pull request title exceeded its limit and was truncated.'
    }

    if ($body.Length -lt $safeBody.Length) {
        $trustedLimitations += 'The pull request description exceeded its limit and was truncated.'
    }

    $collectionComplete = $boundedDiff.Complete -and $trustedLimitations.Count -eq 0
    $reviewInput = [pscustomobject]@{
        trustedMetadata = [pscustomobject]@{
            reviewedHeadSha = $headSha
            collectionComplete = $collectionComplete
            limitations = $trustedLimitations
            ciSignal = 'unavailable'
        }
        pullRequest = [pscustomobject]@{
            title = $title
            body = $body
        }
        diff = $boundedDiff.Text
    }

    $request = [pscustomobject]@{
        model = $Model
        messages = @(
            [pscustomobject]@{
                role = 'system'
                content = "$policy`n`n$procedure"
            },
            [pscustomobject]@{
                role = 'user'
                content = $reviewInput | ConvertTo-Json -Depth 20 -Compress
            }
        )
        temperature = 0.1
        max_tokens = $OutputTokenBudget
        stream = $false
        response_format = [pscustomobject]@{
            type = 'json_schema'
            json_schema = [pscustomobject]@{
                name = 'plandeck_ai_code_review'
                strict = $true
                schema = $schema
            }
        }
    }

    Write-Output 'Requesting one structured GitHub Models review.'
    $requestBody = $request | ConvertTo-Json -Depth 100 -Compress
    Write-Output "Request body length: $($requestBody.Length) characters."
    try {
        $response = Invoke-RestMethod `
        -Method Post `
        -Uri 'https://models.github.ai/inference/chat/completions' `
        -Headers @{
            Accept = 'application/vnd.github+json'
            Authorization = "Bearer $($env:GITHUB_TOKEN)"
            'X-GitHub-Api-Version' = '2022-11-28'
        } `
        -ContentType 'application/json' `
        -Body $requestBody
    }
    catch {
        $errorResponse = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($errorResponse)) {
            $errorResponse = $_.Exception.Message
        }
        Write-Output "GitHub Models error response: $errorResponse"
        throw "GitHub Models request failed: $errorResponse"
    }

    $content = [string] $response.choices[0].message.content
    if ([string]::IsNullOrWhiteSpace($content)) {
        throw 'GitHub Models returned no structured review content.'
    }

    try {
        $result = $content | ConvertFrom-Json -Depth 100
    }
    catch {
        throw 'GitHub Models returned unparseable structured content.'
    }

    $modelLimitations = @($result.analysis.limitations | Where-Object { $_ })
    $allLimitations = @(
        $trustedLimitations + $modelLimitations |
            Select-Object -Unique |
            Select-Object -First 20
    )
    $result.reviewedHeadSha = $headSha
    $result.analysis.complete = $collectionComplete -and [bool] $result.analysis.complete
    $result.analysis.limitations = if ($result.analysis.complete) { @() } else { $allLimitations }

    if (-not $result.analysis.complete -and @($result.analysis.limitations).Count -eq 0) {
        $result.analysis.limitations = @('The model reported an incomplete static analysis.')
    }

    $runnerTemp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
    $resultPath = Join-Path $runnerTemp "ai-code-review-$headSha.json"
    $result | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $resultPath -Encoding utf8NoBOM

    $validatorPath = Join-Path $PSScriptRoot 'validate-review-result.ps1'
    $verdict = & $validatorPath -ResultPath $resultPath -ExpectedHeadSha $headSha
    if ($LASTEXITCODE -ne 0 -or $verdict -notin @('passed', 'failed')) {
        throw 'The structured model result failed trusted validation.'
    }

    if ($env:GITHUB_OUTPUT) {
        "result-path=$resultPath" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8NoBOM
        "analysis-complete=$($result.analysis.complete.ToString().ToLowerInvariant())" |
            Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8NoBOM
        "reviewed-head-sha=$headSha" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8NoBOM
    }

    Write-Output "Structured review completed with trusted verdict '$verdict'."
}
catch {
    Write-Error "AI code review failed: $($_.Exception.Message)"
    exit 1
}

