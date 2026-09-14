metadata name = 'Exam Kiosk web application settings'
metadata description = 'Configures Microsoft Entra authentication settings on the Exam Kiosk App Service.'

@description('Name of the existing Exam Kiosk App Service.')
param webAppName string

@description('Microsoft Entra authority base URL for the current Azure cloud.')
param entraInstance string

@description('Microsoft Entra tenant ID.')
param entraTenantId string

@description('Client ID of the Exam Kiosk Web Microsoft Entra app registration.')
param entraClientId string

@description('Versionless URI of the Microsoft Entra client secret in Key Vault.')
param clientSecretUri string

resource webApp 'Microsoft.Web/sites@2024-04-01' existing = {
  name: webAppName
}

resource webAppSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  name: 'appsettings'
  parent: webApp
  properties: {
    AzureAd__Instance: entraInstance
    AzureAd__TenantId: entraTenantId
    AzureAd__ClientId: entraClientId
    AzureAd__CallbackPath: '/signin-oidc'
    AzureAd__ClientSecret: '@Microsoft.KeyVault(SecretUri=${clientSecretUri})'
  }
}