[CmdletBinding()]
param(
    [string]$SubscriptionId,

    [string]$Location = 'canadacentral'
)

$ErrorActionPreference = 'Stop'
$VerbosePreference = 'Continue'
$deploymentName = "examkiosk-dev-infra-$(Get-Date -Format 'yyyyMMddHHmmss')"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$templateFile = Join-Path $repositoryRoot 'infra\main.bicep'
$resolvedParameterFile = Join-Path $repositoryRoot 'infra\main.dev.bicepparam'
$webProject = Join-Path $repositoryRoot 'src\ExamKiosk.Web\ExamKiosk.Web.csproj'
$webTests = Join-Path $repositoryRoot 'tests\ExamKiosk.Web.Tests\ExamKiosk.Web.Tests.csproj'
$stagingRoot = Join-Path $env:TEMP "ExamKioskWeb-$([guid]::NewGuid())"
$publishDirectory = Join-Path $stagingRoot 'publish'
$packagePath = Join-Path $stagingRoot 'ExamKiosk.Web.zip'

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

    Write-Verbose "Deploying Azure infrastructure as '$deploymentName' in '$Location'."
    $deploymentJson = & az deployment sub create `
        --name $deploymentName `
        --location $Location `
        --template-file $templateFile `
        --parameters $resolvedParameterFile `
        --output json | Out-String

    Write-Verbose 'Reading resource names and URL from the infrastructure deployment outputs.'
    $deployment = $deploymentJson | ConvertFrom-Json
    $resourceGroupName = $deployment.properties.outputs.resourceGroupName.value
    $webAppName = $deployment.properties.outputs.webAppName.value
    $webAppUrl = $deployment.properties.outputs.webAppUrl.value

    if (-not $resourceGroupName -or -not $webAppName -or -not $webAppUrl) {
        throw 'The infrastructure deployment did not return the expected outputs.'
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
    if (Test-Path -LiteralPath $stagingRoot) {
        Write-Verbose "Removing temporary deployment files from '$stagingRoot'."
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}