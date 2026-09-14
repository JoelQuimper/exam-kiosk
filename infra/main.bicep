targetScope = 'subscription'

metadata name = 'Exam Kiosk web application'
metadata description = 'Creates the resource group, Linux App Service, and Key Vault resources for Exam Kiosk.'

@description('Azure region for the resource group and App Service resources.')
param location string

@description('Short workload name used with CAF resource abbreviations.')
param workloadName string

@description('Deployment environment name.')
param environment string

@description('Client ID of the Exam Kiosk Web Microsoft Entra app registration.')
param entraClientId string

var resourceGroupName = 'rg-${workloadName}-${environment}'
var appServicePlanName = 'asp-${workloadName}-${environment}'
var webAppName = 'app-${workloadName}-${environment}'
var keyVaultName = 'kv-${workloadName}-${environment}'
var clientSecretName = 'entra-client-secret'

module resourceGroup 'br/public:avm/res/resources/resource-group:0.4.4' = {
  name: 'deploy-${resourceGroupName}'
  params: {
    name: resourceGroupName
    location: location
  }
}

module appServicePlan 'br/public:avm/res/web/serverfarm:0.7.0' = {
  name: 'deploy-${appServicePlanName}'
  scope: az.resourceGroup(resourceGroupName)
  dependsOn: [
    resourceGroup
  ]
  params: {
    name: appServicePlanName
    location: location
    kind: 'linux'
    reserved: true
    skuName: 'B1'
    skuCapacity: 1
    zoneRedundant: false
  }
}

module webApp 'br/public:avm/res/web/site:0.24.0' = {
  name: 'deploy-${webAppName}'
  scope: az.resourceGroup(resourceGroupName)
  params: {
    name: webAppName
    location: location
    kind: 'app,linux'
    serverFarmResourceId: appServicePlan.outputs.resourceId
    httpsOnly: true
    clientAffinityEnabled: false
    publicNetworkAccess: 'Enabled'
    managedIdentities: {
      systemAssigned: true
    }
    siteConfig: {
      alwaysOn: true
      ftpsState: 'Disabled'
      http20Enabled: true
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
    }
  }
}

module keyVault 'br/public:avm/res/key-vault/vault:0.14.0' = {
  name: 'deploy-${keyVaultName}'
  scope: az.resourceGroup(resourceGroupName)
  params: {
    name: keyVaultName
    location: location
    sku: 'standard'
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    enableVaultForDeployment: false
    enableVaultForDiskEncryption: false
    enableVaultForTemplateDeployment: false
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
    roleAssignments: [
      {
        principalId: webApp.outputs.systemAssignedMIPrincipalId!
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: 'Key Vault Secrets User'
      }
      {
        principalId: deployer().objectId
        roleDefinitionIdOrName: 'Key Vault Secrets Officer'
      }
    ]
  }
}

module webAppSettings './modules/web-app-settings.bicep' = {
  name: 'configure-${webAppName}'
  scope: az.resourceGroup(resourceGroupName)
  params: {
    webAppName: webApp.outputs.name
    entraInstance: az.environment().authentication.loginEndpoint
    entraTenantId: subscription().tenantId
    entraClientId: entraClientId
    clientSecretUri: '${keyVault.outputs.uri}secrets/${clientSecretName}'
  }
}

output resourceGroupName string = resourceGroupName
output appServicePlanName string = appServicePlanName
output webAppName string = webAppName
output webAppUrl string = 'https://${webAppName}.azurewebsites.net'
output keyVaultName string = keyVaultName
output clientSecretName string = clientSecretName
