#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$')]
    [string]$Environment
)

$ErrorActionPreference = 'Stop'
$VerbosePreference = 'Continue'
$agentRoleId = '33d7058f-8d53-49bb-b7a8-94f23c20cb18'
$agentRoleValue = 'ExamDevice.Agent'
$webDisplayName = "Exam Kiosk Web - $Environment"
$agentDisplayName = "Exam Kiosk Device Agent - $Environment"
$certificateDisplayName = 'Exam Kiosk Device Agent PoC'
$configurationRoot = Join-Path $env:ProgramData 'ExamKiosk'
$configurationPath = Join-Path $configurationRoot 'deployment.settings.json'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

. (Join-Path $repositoryRoot 'scripts\Azure\Private\Initialize-ExamKioskEntraApplication.ps1')

$deploymentConfiguration = $null
if (Test-Path -LiteralPath $configurationPath -PathType Leaf) {
    $deploymentConfiguration = Get-Content `
        -LiteralPath $configurationPath `
        -Raw |
        ConvertFrom-Json
}

Write-Verbose 'Checking Azure CLI authentication.'
$accountJson = & az account show `
    --query '{tenantId:tenantId}' `
    --output json | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Checking Azure CLI authentication failed with exit code $LASTEXITCODE."
}
$account = $accountJson | ConvertFrom-Json

Write-Verbose "Reading Web app registration '$webDisplayName'."
$webApplicationsJson = & az ad app list `
    --display-name $webDisplayName `
    --query '[].{id:id,appId:appId,displayName:displayName,identifierUris:identifierUris,appRoles:appRoles}' `
    --output json | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Reading the Web app registration failed with exit code $LASTEXITCODE."
}
$returnedWebApplications = @($webApplicationsJson | ConvertFrom-Json)
$webApplications = @(
    $returnedWebApplications |
        Where-Object displayName -CEQ $webDisplayName
)
if ($webApplications.Count -ne 1) {
    $returnedNames = @(
        $returnedWebApplications |
            ForEach-Object { "'$($_.displayName)'" }
    ) -join ', '
    throw (
        "Expected exactly one app registration named '$webDisplayName', " +
        "but found $($webApplications.Count) exact matches. " +
        "Azure returned: $returnedNames.")
}
$webApplication = $webApplications[0]
$apiIdentifier = "api://$($webApplication.appId)"
$identifierUris = @($webApplication.identifierUris)
if ($apiIdentifier -notin $identifierUris) {
    $identifierUris += $apiIdentifier
}
$appRoles = @($webApplication.appRoles)
$matchingRole = @($appRoles | Where-Object value -CEQ $agentRoleValue)
if ($matchingRole.Count -gt 1) {
    throw "The Web app registration contains duplicate '$agentRoleValue' roles."
}
if ($matchingRole.Count -eq 0) {
    $appRoles += [ordered]@{
        allowedMemberTypes = @('Application')
        description = 'Activate and complete the exam session bound to an Exam Kiosk device.'
        displayName = 'Exam Kiosk Device Agent'
        id = $agentRoleId
        isEnabled = $true
        value = $agentRoleValue
    }
}
elseif ($matchingRole[0].id -ne $agentRoleId) {
    throw "The existing '$agentRoleValue' role has an unexpected immutable ID."
}

Write-Verbose 'Ensuring the Web API identifier and Device Agent app role exist.'
$webPatch = [ordered]@{
    identifierUris = $identifierUris
    appRoles = $appRoles
} | ConvertTo-Json -Depth 20 -Compress
& az rest `
    --method PATCH `
    --uri "https://graph.microsoft.com/v1.0/applications/$($webApplication.id)" `
    --headers 'Content-Type=application/json' `
    --body $webPatch `
    --output none
if ($LASTEXITCODE -ne 0) {
    throw "Updating the Web API registration failed with exit code $LASTEXITCODE."
}

Write-Verbose "Ensuring Device Agent app registration '$agentDisplayName' exists."
$agentApplication = Initialize-ExamKioskEntraApplication `
    -DisplayName $agentDisplayName

$certificate = Get-ChildItem Cert:\LocalMachine\My |
    Where-Object {
        $_.Subject -ceq "CN=$agentDisplayName" -and
        $_.HasPrivateKey -and
        $_.NotAfter -gt [DateTime]::UtcNow.AddDays(30)
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1
if (-not $certificate) {
    Write-Verbose 'Creating a non-exportable LocalMachine Device Agent certificate.'
    $certificate = New-SelfSignedCertificate `
        -Subject "CN=$agentDisplayName" `
        -CertStoreLocation 'Cert:\LocalMachine\My' `
        -KeyAlgorithm RSA `
        -KeyLength 3072 `
        -KeyExportPolicy NonExportable `
        -KeyUsage DigitalSignature `
        -HashAlgorithm SHA256 `
        -NotAfter ([DateTime]::UtcNow.AddYears(1))
}

$temporaryCertificatePath = Join-Path `
    $env:TEMP `
    "ExamKiosk-Agent-$([guid]::NewGuid()).pem"
try {
    $base64Certificate = [Convert]::ToBase64String(
        $certificate.RawData,
        [Base64FormattingOptions]::InsertLineBreaks)
    @(
        '-----BEGIN CERTIFICATE-----'
        $base64Certificate
        '-----END CERTIFICATE-----'
    ) | Set-Content -LiteralPath $temporaryCertificatePath -Encoding ascii

    Write-Verbose 'Removing prior PoC certificate credentials from the Agent app.'
    $credentialsJson = & az ad app credential list `
        --id $agentApplication.ClientId `
        --query '[].{keyId:keyId,displayName:displayName}' `
        --output json | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Listing Agent credentials failed with exit code $LASTEXITCODE."
    }
    foreach ($credential in @($credentialsJson | ConvertFrom-Json) |
            Where-Object displayName -CEQ $certificateDisplayName) {
        & az ad app credential delete `
            --id $agentApplication.ClientId `
            --key-id $credential.keyId `
            --output none
        if ($LASTEXITCODE -ne 0) {
            throw "Deleting an old Agent credential failed with exit code $LASTEXITCODE."
        }
    }

    Write-Verbose 'Uploading the public Device Agent certificate.'
    & az ad app credential reset `
        --id $agentApplication.ClientId `
        --append `
        --cert $temporaryCertificatePath `
        --display-name $certificateDisplayName `
        --years 1 `
        --output none
    if ($LASTEXITCODE -ne 0) {
        throw "Uploading the Agent certificate failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryCertificatePath -PathType Leaf) {
        Remove-Item -LiteralPath $temporaryCertificatePath -Force
    }
}

Write-Verbose 'Resolving service principals for app-role assignment.'
$webServicePrincipalId = & az ad sp list `
    --filter "appId eq '$($webApplication.appId)'" `
    --query '[0].id' `
    --output tsv
$agentServicePrincipalId = & az ad sp list `
    --filter "appId eq '$($agentApplication.ClientId)'" `
    --query '[0].id' `
    --output tsv
if ($LASTEXITCODE -ne 0 -or
    -not $webServicePrincipalId -or
    -not $agentServicePrincipalId) {
    throw 'Resolving the Web or Agent service principal failed.'
}

$assignmentsJson = & az rest `
    --method GET `
    --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$agentServicePrincipalId/appRoleAssignments" `
    --output json | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Reading Agent app-role assignments failed with exit code $LASTEXITCODE."
}
$assignments = @((($assignmentsJson | ConvertFrom-Json).value))
$hasAssignment = $assignments |
    Where-Object {
        $_.resourceId -eq $webServicePrincipalId -and
        $_.appRoleId -eq $agentRoleId
    } |
    Select-Object -First 1
if (-not $hasAssignment) {
    Write-Verbose "Assigning '$agentRoleValue' to the Device Agent application."
    $assignment = [ordered]@{
        principalId = $agentServicePrincipalId
        resourceId = $webServicePrincipalId
        appRoleId = $agentRoleId
    } | ConvertTo-Json -Compress
    & az rest `
        --method POST `
        --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$agentServicePrincipalId/appRoleAssignments" `
        --headers 'Content-Type=application/json' `
        --body $assignment `
        --output none
    if ($LASTEXITCODE -ne 0) {
        throw "Assigning the Agent app role failed with exit code $LASTEXITCODE."
    }
}

$updatedConfiguration = [ordered]@{}
if ($deploymentConfiguration) {
    foreach ($property in $deploymentConfiguration.PSObject.Properties) {
        $updatedConfiguration[$property.Name] = $property.Value
    }
}
$updatedConfiguration['tenantId'] = $account.tenantId
$updatedConfiguration['webAppClientId'] = $webApplication.appId
$updatedConfiguration['agentClientId'] = $agentApplication.ClientId
$updatedConfiguration['agentCertificateThumbprint'] = $certificate.Thumbprint
New-Item -ItemType Directory -Path $configurationRoot -Force | Out-Null
$updatedConfiguration |
    ConvertTo-Json |
    Set-Content -LiteralPath $configurationPath -Encoding utf8

Write-Output "Device Agent identity configured with certificate $($certificate.Thumbprint)."
Write-Output "Deployment settings written to $configurationPath."
