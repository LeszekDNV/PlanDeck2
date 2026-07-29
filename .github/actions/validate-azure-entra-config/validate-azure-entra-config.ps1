[CmdletBinding()]
param(
    [Parameter()]
    [AllowEmptyString()]
    [string] $TenantId,

    [Parameter()]
    [AllowEmptyString()]
    [string] $ClientId,

    [Parameter()]
    [AllowEmptyString()]
    [string] $ClientSecret,

    [Parameter()]
    [AllowEmptyString()]
    [string] $PublishTarget
)

$ErrorActionPreference = 'Stop'

$requiredSettings = [ordered] @{
    'AZURE_ENTRA_TENANT_ID' = $TenantId
    'AZURE_ENTRA_CLIENT_ID' = $ClientId
    'AZURE_ENTRA_CLIENT_SECRET' = $ClientSecret
    'PLANDECK_PUBLISH_TARGET' = $PublishTarget
}

$missingSettings = @(
    $requiredSettings.GetEnumerator() |
        Where-Object { [string]::IsNullOrWhiteSpace($_.Value) } |
        ForEach-Object { $_.Key }
)

if ($missingSettings.Count -gt 0) {
    throw (
        'Required deployment configuration is missing: {0}.' -f
        ($missingSettings -join ', '))
}

Write-Host 'Required Microsoft Entra deployment configuration is present.'
