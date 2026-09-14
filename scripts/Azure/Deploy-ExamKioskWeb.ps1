[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SubscriptionId,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$')]
    [string]$Environment,

    [string]$Location = 'canadacentral'
)

$ErrorActionPreference = 'Stop'
$VerbosePreference = 'Continue'
$deploymentName = "examkiosk-$Environment-infra-$(Get-Date -Format 'yyyyMMddHHmmss')"
$entraDisplayName = "Exam Kiosk Web - $Environment"
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$templateFile = Join-Path $repositoryRoot 'infra\main.bicep'
$resolvedParameterFile = Join-Path $repositoryRoot 'infra\main.dev.bicepparam'
$webProject = Join-Path $repositoryRoot 'src\ExamKiosk.Web\ExamKiosk.Web.csproj'
$webTests = Join-Path $repositoryRoot 'tests\ExamKiosk.Web.Tests\ExamKiosk.Web.Tests.csproj'
$stagingRoot = Join-Path $env:TEMP "ExamKioskWeb-$([guid]::NewGuid())"
$publishDirectory = Join-Path $stagingRoot 'publish'
$packagePath = Join-Path $stagingRoot 'ExamKiosk.Web.zip'
$credentialValue = $null

. (Join-Path $PSScriptRoot 'Private\Initialize-ExamKioskEntraApplication.ps1')

try {
    Write-Verbose 'Checking Azure CLI authentication.'
    & az account show --output none

    if ($SubscriptionId) {
        Write-Verbose "Selecting Azure subscription '$SubscriptionId'."
        & az account set --subscription $SubscriptionId
    }

    Write-Verbose "Linting Bicep template '$templateFile'."
    & az bicep lint --file $templateFile

    Write-Verbose "Running web application tests from '$webTests'."
    & dotnet test $webTests --configuration Release

    Write-Verbose "Publishing web application to '$publishDirectory'."
    & dotnet publish $webProject `
        --configuration Release `
        --output $publishDirectory

    Write-Verbose "Creating deployment package '$packagePath'."
    Compress-Archive `
        -Path (Join-Path $publishDirectory '*') `
        -DestinationPath $packagePath

    Write-Verbose "Ensuring environment-specific Entra application '$entraDisplayName' exists."
    $entraApplication = Initialize-ExamKioskEntraApplication -DisplayName $entraDisplayName
    $entraClientId = $entraApplication.ClientId

    Write-Verbose "Deploying Azure infrastructure as '$deploymentName' in '$Location'."
    $deploymentJson = & az deployment sub create `
        --name $deploymentName `
        --location $Location `
        --template-file $templateFile `
        --parameters $resolvedParameterFile `
        --parameters "location=$Location" "environment=$Environment" "entraClientId=$entraClientId" `
        --output json | Out-String

    if ($LASTEXITCODE -ne 0) {
        throw "Infrastructure deployment failed with exit code $LASTEXITCODE."
    }

    Write-Verbose 'Reading resource names and URL from the infrastructure deployment outputs.'
    $deployment = $deploymentJson | ConvertFrom-Json
    $resourceGroupName = $deployment.properties.outputs.resourceGroupName.value
    $webAppName = $deployment.properties.outputs.webAppName.value
    $webAppUrl = $deployment.properties.outputs.webAppUrl.value
    $keyVaultName = $deployment.properties.outputs.keyVaultName.value
    $clientSecretName = $deployment.properties.outputs.clientSecretName.value

    if (-not $resourceGroupName -or -not $webAppName -or -not $webAppUrl -or -not $keyVaultName -or -not $clientSecretName) {
        throw 'The infrastructure deployment did not return the expected outputs.'
    }

    Write-Verbose "Adding the application URL as callback to client ID '$entraClientId' when needed."
    $applicationJson = & az ad app show `
        --id $entraClientId `
        --query '{appId:appId,redirectUris:web.redirectUris}' `
        --output json | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Reading the Entra app registration failed with exit code $LASTEXITCODE."
    }
    $application = $applicationJson | ConvertFrom-Json

    $appCallback = "$webAppUrl/signin-oidc"
    $redirectUris = @($application.redirectUris)
    if ($appCallback -notin $redirectUris) {
        $redirectUris += $appCallback
        & az ad app update `
            --id $entraClientId `
            --web-redirect-uris @redirectUris `
            --output none
        if ($LASTEXITCODE -ne 0) {
            throw "Updating the application callback failed with exit code $LASTEXITCODE."
        }
    }

    Write-Verbose "Checking for Key Vault secret '$clientSecretName'."
    $secretCount = & az keyvault secret list `
        --vault-name $keyVaultName `
        --query "[?name == '$clientSecretName'] | length(@)" `
        --output tsv
    if ($LASTEXITCODE -ne 0) {
        throw "Checking Key Vault secrets failed with exit code $LASTEXITCODE."
    }

    if ([int]$secretCount -eq 0) {
        Write-Verbose 'Creating the initial Entra client credential.'
        $credentialJson = & az ad app credential reset `
            --id $entraClientId `
            --append `
            --display-name 'Exam Kiosk App Service' `
            --years 1 `
            --output json | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "Creating the initial Entra client credential failed with exit code $LASTEXITCODE."
        }
        $credential = $credentialJson | ConvertFrom-Json

        $credentialValue = $credential.password
        if (-not $credentialValue) {
            throw 'Microsoft Entra did not return the new credential value.'
        }

        $secretWritten = $false
        foreach ($attempt in 1..6) {
            Write-Verbose "Writing the client credential to Key Vault (attempt $attempt of 6)."
            & az keyvault secret set `
                --vault-name $keyVaultName `
                --name $clientSecretName `
                --value $credentialValue `
                --output none
            if ($LASTEXITCODE -eq 0) {
                $secretWritten = $true
                break
            }

            if ($attempt -lt 6) {
                Start-Sleep -Seconds ([math]::Pow(2, $attempt))
            }
        }

        if (-not $secretWritten) {
            throw 'Writing the initial client credential to Key Vault failed after bounded retries.'
        }
    }
    else {
        Write-Verbose 'The Key Vault client credential already exists; credential creation is skipped.'
    }

    Write-Verbose "Deploying web application package to '$webAppName' in '$resourceGroupName'."
    & az webapp deploy `
        --resource-group $resourceGroupName `
        --name $webAppName `
        --src-path $packagePath `
        --type zip `
        --clean true `
        --restart true `
        --output none

    $healthUrl = "$webAppUrl/health"
    Write-Verbose "Verifying deployed application health at '$healthUrl'."
    $healthResponse = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 30
    if ($healthResponse.status -ne 'healthy') {
        throw "The deployed health endpoint returned an unexpected response from $healthUrl."
    }

    Write-Verbose "Exam Kiosk deployed successfully: $webAppUrl"
}
finally {
    $credentialValue = $null

    if (Test-Path -LiteralPath $stagingRoot) {
        Write-Verbose "Removing temporary deployment files from '$stagingRoot'."
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}