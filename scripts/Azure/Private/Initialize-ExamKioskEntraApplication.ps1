function Initialize-ExamKioskEntraApplication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$DisplayName,

        [string[]]$RedirectUris = @()
    )

    Write-Verbose "Looking for an exact app registration named '$DisplayName'."
    $matchingApplicationsJson = & az ad app list `
        --display-name $DisplayName `
        --query '[].{id:id,appId:appId,displayName:displayName,signInAudience:signInAudience,redirectUris:web.redirectUris}' `
        --output json | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Looking up the Entra app registration failed with exit code $LASTEXITCODE."
    }
    $matchingApplications = @($matchingApplicationsJson | ConvertFrom-Json) |
        Where-Object displayName -CEQ $DisplayName

    if ($matchingApplications.Count -gt 1) {
        throw "More than one app registration is named '$DisplayName'. Resolve the duplicate registrations before continuing."
    }

    if ($matchingApplications.Count -eq 0) {
        Write-Verbose "Creating single-tenant web app registration '$DisplayName'."
        $createArguments = @(
            'ad', 'app', 'create',
            '--display-name', $DisplayName,
            '--sign-in-audience', 'AzureADMyOrg',
            '--output', 'json'
        )
        if ($RedirectUris.Count -gt 0) {
            $createArguments += '--web-redirect-uris'
            $createArguments += $RedirectUris
        }

        $applicationJson = & az @createArguments | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "Creating the Entra app registration failed with exit code $LASTEXITCODE."
        }
        $application = $applicationJson | ConvertFrom-Json
    }
    else {
        $application = $matchingApplications[0]
        if ($application.signInAudience -ne 'AzureADMyOrg') {
            throw "App registration '$DisplayName' is not single-tenant. Expected signInAudience 'AzureADMyOrg'."
        }

        $configuredRedirectUris = @($application.redirectUris)
        $missingRedirectUris = @($RedirectUris | Where-Object { $_ -notin $configuredRedirectUris })
        if ($missingRedirectUris.Count -gt 0) {
            $configuredRedirectUris += $missingRedirectUris
            Write-Verbose "Adding missing callbacks to app registration '$DisplayName' while preserving existing callbacks."
            & az ad app update `
                --id $application.id `
                --web-redirect-uris @configuredRedirectUris `
                --output none
            if ($LASTEXITCODE -ne 0) {
                throw "Adding callbacks to the Entra app registration failed with exit code $LASTEXITCODE."
            }
        }
    }

    Write-Verbose "Looking for the service principal for client ID '$($application.appId)'."
    $servicePrincipalsJson = & az ad sp list `
        --filter "appId eq '$($application.appId)'" `
        --query '[].{id:id,appId:appId}' `
        --output json | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Looking up the Entra service principal failed with exit code $LASTEXITCODE."
    }
    $servicePrincipals = @($servicePrincipalsJson | ConvertFrom-Json)

    if ($servicePrincipals.Count -gt 1) {
        throw "Entra returned multiple service principals for the unique client ID '$($application.appId)'."
    }

    if ($servicePrincipals.Count -eq 0) {
        Write-Verbose "Creating the service principal for client ID '$($application.appId)'."
        & az ad sp create `
            --id $application.appId `
            --output none
        if ($LASTEXITCODE -ne 0) {
            throw "Creating the Entra service principal failed with exit code $LASTEXITCODE."
        }
    }
    else {
        Write-Verbose 'The service principal already exists.'
    }

    [pscustomobject]@{
        ClientId = $application.appId
    }
}
