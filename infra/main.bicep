targetScope = 'subscription'

metadata name = 'Exam Kiosk web application'
metadata description = 'Creates the resource group and Linux App Service resources for Exam Kiosk.'

@description('Azure region for the resource group and App Service resources.')
param location string

@description('Short workload name used with CAF resource abbreviations.')
param workloadName string

@description('Deployment environment name.')
param environment string

var resourceGroupName = 'rg-${workloadName}-${environment}'
var appServicePlanName = 'asp-${workloadName}-${environment}'
var webAppName = 'app-${workloadName}-${uniqueString(subscription().id, workloadName, environment)}-${environment}'

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

output resourceGroupName string = resourceGroupName
output appServicePlanName string = appServicePlanName
output webAppName string = webAppName
output webAppUrl string = 'https://${webAppName}.azurewebsites.net'
