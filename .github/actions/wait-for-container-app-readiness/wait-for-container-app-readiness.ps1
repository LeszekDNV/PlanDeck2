[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ResourceGroup,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ContainerAppName,

    [Parameter()]
    [ValidateRange(1, [int]::MaxValue)]
    [int] $RevisionTimeoutSeconds = 600,

    [Parameter()]
    [ValidateRange(1, [int]::MaxValue)]
    [int] $PublicHealthTimeoutSeconds = 300,

    [Parameter()]
    [ValidatePattern('^/')]
    [string] $HealthPath = '/health'
)

$ErrorActionPreference = 'Stop'

function Get-BoundedSummary {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Text
    )

    $summary = $Text -replace '\s+', ' '
    if ($summary.Length -gt 300) {
        return $summary.Substring(0, 300)
    }

    return $summary
}

function Invoke-AzureCli {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & az @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $summary = Get-BoundedSummary -Text ($output -join ' ')
        throw "Azure CLI request failed: $summary"
    }

    return ($output -join [Environment]::NewLine)
}

function Get-NormalizedState {
    param(
        [Parameter()]
        [AllowNull()]
        [string] $State
    )

    if ([string]::IsNullOrWhiteSpace($State)) {
        return ''
    }

    return ($State -replace '[\s_-]', '').ToLowerInvariant()
}

function Get-RevisionDiagnostic {
    param(
        [Parameter()]
        [AllowNull()]
        [string] $ProvisioningState,

        [Parameter()]
        [AllowNull()]
        [string] $HealthState,

        [Parameter()]
        [AllowNull()]
        [string] $RunningState
    )

    return (
        'provisioning={0}, health={1}, running={2}' -f
        ($ProvisioningState ?? '<empty>'),
        ($HealthState ?? '<empty>'),
        ($RunningState ?? '<empty>'))
}

$revisionName = (
    Invoke-AzureCli -Arguments @(
        'containerapp', 'show',
        '--resource-group', $ResourceGroup,
        '--name', $ContainerAppName,
        '--query', 'properties.latestRevisionName',
        '--output', 'tsv',
        '--only-show-errors')
).Trim()

if ([string]::IsNullOrWhiteSpace($revisionName)) {
    throw "Container App '$ContainerAppName' has no latest revision to verify."
}

Write-Host (
    "Verifying Container App '$ContainerAppName' revision '$revisionName' in '$ResourceGroup'.")

$revisionStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$revisionAttempt = 0
$nextRevisionDelaySeconds = 5
$lastRevisionDiagnostic = 'No revision query completed.'
$revisionReady = $false

while ($revisionStopwatch.Elapsed.TotalSeconds -lt $RevisionTimeoutSeconds) {
    $revisionAttempt++

    try {
        $revisionJson = Invoke-AzureCli -Arguments @(
            'containerapp', 'revision', 'show',
            '--resource-group', $ResourceGroup,
            '--name', $ContainerAppName,
            '--revision', $revisionName,
            '--output', 'json',
            '--only-show-errors')
        $revision = $revisionJson | ConvertFrom-Json

        $provisioningState = [string] $revision.properties.provisioningState
        $healthState = [string] $revision.properties.healthState
        $runningState = [string] $revision.properties.runningState
        $lastRevisionDiagnostic = Get-RevisionDiagnostic `
            -ProvisioningState $provisioningState `
            -HealthState $healthState `
            -RunningState $runningState

        Write-Host (
            'Revision attempt {0} at {1:N1}s: {2}.' -f
            $revisionAttempt,
            $revisionStopwatch.Elapsed.TotalSeconds,
            $lastRevisionDiagnostic)

        $runningStateNormalized = Get-NormalizedState -State $runningState
        if (
            $provisioningState -eq 'Provisioned' -and
            $healthState -eq 'Healthy' -and
            $runningStateNormalized -in @(
                'running',
                'running(atmax)',
                'runningatmaxscale')
        ) {
            $revisionReady = $true
            break
        }

        $provisioningStateNormalized = Get-NormalizedState -State $provisioningState
        $healthStateNormalized = Get-NormalizedState -State $healthState
        if (
            $provisioningStateNormalized -in @('failed', 'canceled', 'cancelled') -or
            $healthStateNormalized -in @('unhealthy', 'degraded') -or
            $runningStateNormalized -in @('failed', 'activationfailed')
        ) {
            throw (
                "Revision '$revisionName' reached a terminal state: {0}." -f
                $lastRevisionDiagnostic)
        }
    }
    catch {
        $lastRevisionDiagnostic = Get-BoundedSummary -Text $_.Exception.Message
        if ($lastRevisionDiagnostic -match 'reached a terminal state') {
            throw
        }

        Write-Warning (
            'Revision attempt {0} could not read a ready state: {1}' -f
            $revisionAttempt,
            $lastRevisionDiagnostic)
    }

    $remainingSeconds = $RevisionTimeoutSeconds - $revisionStopwatch.Elapsed.TotalSeconds
    if ($remainingSeconds -le 0) {
        break
    }

    $delaySeconds = [Math]::Min(
        $nextRevisionDelaySeconds,
        [Math]::Floor($remainingSeconds))
    if ($delaySeconds -le 0) {
        break
    }

    Start-Sleep -Seconds $delaySeconds
    $nextRevisionDelaySeconds = [Math]::Min(60, $nextRevisionDelaySeconds * 2)
}

if (-not $revisionReady) {
    throw (
        "Revision '$revisionName' did not become ready within {0} seconds. Last state: {1}." -f
        $RevisionTimeoutSeconds,
        $lastRevisionDiagnostic)
}

Write-Host (
    "Revision '$revisionName' is provisioned, healthy, and running.")

$fqdn = (
    Invoke-AzureCli -Arguments @(
        'containerapp', 'show',
        '--resource-group', $ResourceGroup,
        '--name', $ContainerAppName,
        '--query', 'properties.configuration.ingress.fqdn',
        '--output', 'tsv',
        '--only-show-errors')
).Trim()

if ([string]::IsNullOrWhiteSpace($fqdn)) {
    throw "Container App '$ContainerAppName' has no public ingress FQDN."
}

$healthUri = "https://$fqdn$HealthPath"
Write-Host "Verifying public readiness at '$healthUri'."

$healthStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$healthAttempt = 0
$nextHealthDelaySeconds = 5
$lastHealthDiagnostic = 'No HTTP request completed.'

while ($healthStopwatch.Elapsed.TotalSeconds -lt $PublicHealthTimeoutSeconds) {
    $healthAttempt++
    $remainingSeconds = $PublicHealthTimeoutSeconds - $healthStopwatch.Elapsed.TotalSeconds
    $operationTimeoutSeconds = [Math]::Max(
        1,
        [Math]::Min(30, [Math]::Floor($remainingSeconds)))

    try {
        $response = Invoke-WebRequest `
            -Uri $healthUri `
            -Method Get `
            -MaximumRedirection 0 `
            -SkipHttpErrorCheck `
            -TimeoutSec $operationTimeoutSeconds
        $statusCode = [int] $response.StatusCode
        $lastHealthDiagnostic = "HTTP $statusCode"

        Write-Host (
            'Public health attempt {0} at {1:N1}s: HTTP {2}.' -f
            $healthAttempt,
            $healthStopwatch.Elapsed.TotalSeconds,
            $statusCode)

        if ($statusCode -eq 200) {
            Write-Host (
                "Deployment readiness passed for revision '$revisionName' at '$healthUri'.")
            return
        }
    }
    catch {
        $lastHealthDiagnostic = Get-BoundedSummary -Text $_.Exception.Message
        Write-Warning (
            'Public health attempt {0} failed: {1}' -f
            $healthAttempt,
            $lastHealthDiagnostic)
    }

    $remainingSeconds = $PublicHealthTimeoutSeconds - $healthStopwatch.Elapsed.TotalSeconds
    if ($remainingSeconds -le 0) {
        break
    }

    $delaySeconds = [Math]::Min(
        $nextHealthDelaySeconds,
        [Math]::Floor($remainingSeconds))
    if ($delaySeconds -le 0) {
        break
    }

    Start-Sleep -Seconds $delaySeconds
    $nextHealthDelaySeconds = [Math]::Min(60, $nextHealthDelaySeconds * 2)
}

throw (
    "Public readiness '$healthUri' did not return HTTP 200 within {0} seconds. Last result: {1}." -f
    $PublicHealthTimeoutSeconds,
    $lastHealthDiagnostic)
