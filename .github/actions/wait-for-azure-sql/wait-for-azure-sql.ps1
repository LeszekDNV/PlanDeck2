[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ServerInstance,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $DatabaseName,

    [Parameter()]
    [ValidateRange(1, [int]::MaxValue)]
    [int] $MaxWaitSeconds = 300
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

function Get-BoundedErrorSummary {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.ErrorRecord] $ErrorRecord
    )

    $summary = $ErrorRecord.Exception.Message -replace '\s+', ' '
    if ($summary.Length -gt 300) {
        return $summary.Substring(0, 300)
    }

    return $summary
}

Write-Host 'Installing and importing the SqlServer module.'
Install-Module SqlServer -Force -Scope CurrentUser -AllowClobber -ErrorAction Stop
Import-Module SqlServer -ErrorAction Stop

Write-Host 'Acquiring an Azure SQL access token.'
$accessToken = az account get-access-token `
    --resource 'https://database.windows.net/' `
    --query accessToken `
    --output tsv

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($accessToken)) {
    throw 'Failed to acquire an Azure SQL access token.'
}

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$attempt = 0
$nextDelaySeconds = 5
$lastErrorSummary = 'No connection attempt completed.'

while ($stopwatch.Elapsed.TotalSeconds -lt $MaxWaitSeconds) {
    $attempt++
    $remainingSeconds = $MaxWaitSeconds - $stopwatch.Elapsed.TotalSeconds
    $operationTimeoutSeconds = [Math]::Max(
        1,
        [Math]::Min(30, [Math]::Floor($remainingSeconds)))

    Write-Host (
        'Azure SQL readiness attempt {0} at {1:N1}s for {2}/{3}.' -f
        $attempt,
        $stopwatch.Elapsed.TotalSeconds,
        $ServerInstance,
        $DatabaseName)

    try {
        Invoke-Sqlcmd `
            -ServerInstance $ServerInstance `
            -Database $DatabaseName `
            -AccessToken $accessToken `
            -Query 'SELECT 1' `
            -ConnectionTimeout $operationTimeoutSeconds `
            -QueryTimeout $operationTimeoutSeconds `
            -AbortOnError `
            -ErrorAction Stop |
            Out-Null

        Write-Host (
            'Azure SQL became ready after {0} attempt(s) and {1:N1}s.' -f
            $attempt,
            $stopwatch.Elapsed.TotalSeconds)
        return
    }
    catch {
        $lastErrorSummary = Get-BoundedErrorSummary -ErrorRecord $_
    }

    $remainingSeconds = $MaxWaitSeconds - $stopwatch.Elapsed.TotalSeconds
    if ($remainingSeconds -le 0) {
        break
    }

    $delaySeconds = [Math]::Min(
        $nextDelaySeconds,
        [Math]::Floor($remainingSeconds))

    if ($delaySeconds -le 0) {
        break
    }

    Write-Warning (
        'Attempt {0} failed at {1:N1}s: {2} Retrying in {3}s.' -f
        $attempt,
        $stopwatch.Elapsed.TotalSeconds,
        $lastErrorSummary,
        $delaySeconds)

    Start-Sleep -Seconds $delaySeconds
    $nextDelaySeconds = [Math]::Min(60, $nextDelaySeconds * 2)
}

throw (
    'Azure SQL did not become ready within {0}s after {1} attempt(s). Last error: {2}' -f
    $MaxWaitSeconds,
    $attempt,
    $lastErrorSummary)
