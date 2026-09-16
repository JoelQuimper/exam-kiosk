# Exam Kiosk Azure infrastructure

This subscription-scope deployment creates a CAF-named resource group and uses
Azure Verified Modules to create a Linux App Service plan and web app.

The development resource names follow the project convention:

```text
rg-examkiosk-dev
asp-examkiosk-dev
app-examkiosk-dev
kv-examkiosk-dev
```

## Validate

Run these commands from the repository root after signing in to the intended
Azure subscription:

```powershell
az bicep restore --file .\infra\main.bicep
az bicep lint --file .\infra\main.bicep
```

The deployment location stores subscription-deployment metadata. The resources
are created in the `location` supplied by the parameter file.

## Local development registration

Create or update the dedicated `Exam Kiosk Web - dev` app registration and
service principal:

```powershell
& .\scripts\Azure\Initialize-ExamKioskDevelopmentApplication.ps1
```

The command replaces only the credential named `Exam Kiosk Local Development`
and prints JSON that can be pasted into
`src\ExamKiosk.Web\appsettings.Development.json`. That file is ignored by Git.
Start the web project with its `https` launch profile so it loads the
development settings and uses the registered
`https://localhost:7136/signin-oidc` redirect URI:

```powershell
dotnet run --project .\src\ExamKiosk.Web\ExamKiosk.Web.csproj --launch-profile https
```

The web application uses the OpenID Connect authorization-code flow with PKCE.
The app registration does not require implicit-grant access tokens or ID tokens
to be enabled.

If the local credential has been disclosed, run the initialization script
again and replace `appsettings.Development.json` with its newly generated
output before starting the application.

## Deploy

The deployment script restores and lints the Bicep modules, tests and publishes
the web app, creates or reuses an environment-specific Entra app registration
and service principal, deploys the infrastructure and ZIP package, and verifies
`/health`:

```powershell
& .\scripts\Azure\Deploy-ExamKioskWeb.ps1 `
  -SubscriptionId '<subscription-id>' `
  -Environment 'dev'
```

After deployment, use the deployed `webAppUrl` output for the first Windows
prototype installation:

```powershell
& .\scripts\Windows\Install-ExamKioskPoc.ps1 -WebAppUrl '<web-app-url>'
```

The installer stores this machine-specific value under
`%ProgramData%\ExamKiosk\deployment.settings.json`. Later installs and resets
reuse it without requiring the argument.

The environment is used as the suffix for both Azure resource names and the
Entra app registration display name, such as `Exam Kiosk Web - test`:

```powershell
& .\scripts\Azure\Deploy-ExamKioskWeb.ps1 `
  -SubscriptionId '<subscription-id>' `
  -Environment 'test'
```

To deploy only the infrastructure manually:

```powershell
az deployment sub create `
  --name examkiosk-dev-infra `
  --location canadacentral `
  --template-file .\infra\main.bicep `
  --parameters .\infra\main.dev.bicepparam
```

The manual command creates infrastructure only. It does not publish the .NET
web application.