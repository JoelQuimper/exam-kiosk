[CmdletBinding()]
$ErrorActionPreference = 'Stop'
$VerbosePreference = 'Continue'
$displayName = 'Exam Kiosk Web - dev'
$localRedirectUri = 'https://localhost:7136/signin-oidc'
$localCredentialDisplayName = 'Exam Kiosk Local Development'

. (Join-Path $PSScriptRoot 'Private\Initialize-ExamKioskEntraApplication.ps1')

Write-Verbose 'Checking Azure CLI authentication for development application initialization.'
$accountJson = & az account show `
    --query '{tenantId:tenantId}' `
    --output json | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Checking Azure CLI authentication failed with exit code $LASTEXITCODE."
}
$account = $accountJson | ConvertFrom-Json

$entraApplication = Initialize-ExamKioskEntraApplication `
    -DisplayName $displayName `
    -RedirectUris $localRedirectUri

Write-Verbose 'Reading the active Azure cloud authentication endpoint.'
$entraInstance = & az cloud show `
    --query endpoints.activeDirectory `
    --output tsv
if ($LASTEXITCODE -ne 0 -or -not $entraInstance) {
    throw "Reading the active Azure cloud failed with exit code $LASTEXITCODE."
}
$entraInstance = "$($entraInstance.TrimEnd('/'))/"

Write-Verbose 'Removing the previous local-development credential when present.'
$credentialsJson = & az ad app credential list `
    --id $entraApplication.ClientId `
    --query '[].{keyId:keyId,displayName:displayName}' `
    --output json | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Listing the Entra application credentials failed with exit code $LASTEXITCODE."
}
$localCredentials = @($credentialsJson | ConvertFrom-Json) |
    Where-Object displayName -CEQ $localCredentialDisplayName
foreach ($localCredential in $localCredentials) {
    & az ad app credential delete `
        --id $entraApplication.ClientId `
        --key-id $localCredential.keyId `
        --output none
    if ($LASTEXITCODE -ne 0) {
        throw "Removing the previous local-development credential failed with exit code $LASTEXITCODE."
    }
}

Write-Verbose 'Creating a local-development client credential.'
$credentialJson = & az ad app credential reset `
    --id $entraApplication.ClientId `
    --append `
    --display-name $localCredentialDisplayName `
    --years 1 `
    --output json | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Creating the local-development credential failed with exit code $LASTEXITCODE."
}
$credential = $credentialJson | ConvertFrom-Json
if (-not $credential.password) {
    throw 'Microsoft Entra did not return the new local-development credential value.'
}

[ordered]@{
    AzureAd = [ordered]@{
        Instance     = $entraInstance
        TenantId     = $account.tenantId
        ClientId     = $entraApplication.ClientId
        ClientSecret = $credential.password
        CallbackPath = '/signin-oidc'
    }
} | ConvertTo-Json -Depth 3