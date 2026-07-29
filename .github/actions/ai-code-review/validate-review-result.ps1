[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ResultPath,

    [ValidateNotNullOrEmpty()]
    [string] $SchemaPath = (Join-Path $PSScriptRoot 'review-result.schema.json'),

    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $ExpectedHeadSha
)

$ErrorActionPreference = 'Stop'

$criterionIds = @(
    'solid-design',
    'clean-architecture',
    'dry-kiss-yagni',
    'blazor-mudblazor',
    'correctness-maintainability',
    'dependency-configuration',
    'data-persistence',
    'async-concurrency',
    'security-authorization',
    'errors-observability',
    'performance-resources',
    'accessibility-ux',
    'testing-verification',
    'api-contracts',
    'pr-quality-scope'
)

try {
    $resolvedResultPath = (Resolve-Path -LiteralPath $ResultPath).Path
    $resolvedSchemaPath = (Resolve-Path -LiteralPath $SchemaPath).Path
    $json = Get-Content -LiteralPath $resolvedResultPath -Raw

    if (-not ($json | Test-Json -SchemaFile $resolvedSchemaPath -ErrorAction Stop)) {
        throw 'The review result does not conform to the JSON Schema.'
    }

    $result = $json | ConvertFrom-Json -Depth 100
    $actualIds = @($result.criteria | ForEach-Object { $_.id })
    $duplicateIds = @($actualIds | Group-Object | Where-Object Count -gt 1)
    $missingIds = @($criterionIds | Where-Object { $_ -notin $actualIds })
    $unexpectedIds = @($actualIds | Where-Object { $_ -notin $criterionIds })

    if ($duplicateIds.Count -gt 0) {
        throw "Duplicate criterion IDs: $($duplicateIds.Name -join ', ')."
    }

    if ($missingIds.Count -gt 0 -or $unexpectedIds.Count -gt 0) {
        throw "Criterion IDs do not match the trusted set. Missing: $($missingIds -join ', '); unexpected: $($unexpectedIds -join ', ')."
    }

    foreach ($criterion in $result.criteria) {
        $hasNaReason = $criterion.PSObject.Properties.Name -contains 'naReason'

        if ($criterion.score -eq 'N/A') {
            if (-not $hasNaReason -or [string]::IsNullOrWhiteSpace($criterion.naReason)) {
                throw "Criterion '$($criterion.id)' uses N/A without a reason."
            }

            if (@($criterion.evidence).Count -ne 0) {
                throw "Criterion '$($criterion.id)' uses N/A but also supplies evidence."
            }
        }
        else {
            if ($hasNaReason -and -not [string]::IsNullOrWhiteSpace($criterion.naReason)) {
                throw "Criterion '$($criterion.id)' has a numeric score and must not supply a non-empty naReason."
            }

            if (@($criterion.evidence).Count -eq 0) {
                throw "Criterion '$($criterion.id)' has a numeric score without changed-path evidence."
            }
        }
    }

    if ($result.analysis.complete -and @($result.analysis.limitations).Count -ne 0) {
        throw 'A complete analysis must not contain limitations.'
    }

    if (-not $result.analysis.complete -and @($result.analysis.limitations).Count -eq 0) {
        throw 'An incomplete analysis must explain at least one limitation.'
    }

    if ($ExpectedHeadSha -and $result.reviewedHeadSha -ine $ExpectedHeadSha) {
        throw "Reviewed head SHA '$($result.reviewedHeadSha)' does not match expected SHA '$ExpectedHeadSha'."
    }

    $applicableScores = @(
        $result.criteria |
            Where-Object score -ne 'N/A' |
            ForEach-Object { [int] $_.score }
    )

    $passed = $result.analysis.complete `
        -and @($result.blockers).Count -eq 0 `
        -and @($applicableScores | Where-Object { $_ -lt 7 }).Count -eq 0

    if ($passed) {
        Write-Output 'passed'
    }
    else {
        Write-Output 'failed'
    }
}
catch {
    throw 'Invalid review result.'
}
