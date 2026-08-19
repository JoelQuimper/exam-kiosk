# Exam Kiosk Azure infrastructure

This subscription-scope deployment creates a CAF-named resource group and uses
Azure Verified Modules to create a Linux App Service plan and web app.

The development resource names follow the project convention:

```text
rg-examkiosk.dev
asp-examkiosk-dev
app-examkiosk-<unique-suffix>-dev
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

## Deploy

The deployment script restores and lints the Bicep modules, tests and publishes
the web app, deploys the infrastructure and ZIP package, and verifies `/health`:

```powershell
& .\scripts\Deploy-ExamKioskWeb.ps1
```

To select a subscription explicitly:

```powershell
& .\scripts\Deploy-ExamKioskWeb.ps1 `
  -SubscriptionId '<subscription-id>'
```

The script asks for confirmation before deployment. Use `-Force` for a
non-interactive deployment.

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