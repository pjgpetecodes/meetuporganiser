targetScope = 'resourceGroup'

@description('The Azure region for all resources.')
param location string = resourceGroup().location

@description('The environment name (e.g. dev, prod).')
param environment string = 'prod'

@description('Resource name prefix.')
@minLength(3)
@maxLength(11)
param resourcePrefix string = 'meetuporg'

@description('SQL administrator username.')
param sqlAdministratorLogin string = 'sqladmin'

@description('SQL administrator password.')
@secure()
param sqlAdministratorLoginPassword string

@description('Entra ID App Registration Client ID.')
param entraClientId string = ''

@description('Entra ID App Registration Client Secret.')
@secure()
param entraClientSecret string = ''

var uniqueSuffix = uniqueString(resourceGroup().id)
var storageAccountName = take('st${resourcePrefix}${uniqueSuffix}', 24)
var sqlServerName = 'sql-${resourcePrefix}-${uniqueSuffix}'
var keyVaultName = take('kv-${resourcePrefix}-${uniqueSuffix}', 24)
var appServicePlanName = 'asp-${resourcePrefix}-${environment}'
var webAppName = 'app-${resourcePrefix}-${uniqueSuffix}'

// 1. Application Insights & Log Analytics
module appInsights 'modules/appinsights.bicep' = {
  name: 'appInsightsDeployment'
  params: {
    location: location
    resourcePrefix: resourcePrefix
    environment: environment
  }
}

// 2. Storage Account & Blob Containers
module storage 'modules/storage.bicep' = {
  name: 'storageDeployment'
  params: {
    location: location
    storageAccountName: storageAccountName
  }
}

// 3. Azure SQL Database (Serverless with Auto-Pause)
module sql 'modules/sql.bicep' = {
  name: 'sqlDeployment'
  params: {
    location: location
    serverName: sqlServerName
    administratorLogin: sqlAdministratorLogin
    administratorLoginPassword: sqlAdministratorLoginPassword
  }
}

// 4. Azure Key Vault
module keyVault 'modules/keyvault.bicep' = {
  name: 'keyVaultDeployment'
  params: {
    location: location
    keyVaultName: keyVaultName
    secrets: {
      SqlConnectionString: sql.outputs.connectionString
      StorageConnectionString: storage.outputs.connectionString
      AppInsightsConnectionString: appInsights.outputs.appInsightsConnectionString
    }
  }
}

// 5. Azure App Service
module appService 'modules/appservice.bicep' = {
  name: 'appServiceDeployment'
  params: {
    location: location
    appServicePlanName: appServicePlanName
    webAppName: webAppName
    appInsightsConnectionString: appInsights.outputs.appInsightsConnectionString
    sqlConnectionString: sql.outputs.connectionString
    storageConnectionString: storage.outputs.connectionString
    entraClientId: entraClientId
    entraClientSecret: entraClientSecret
  }
}

// Outputs for CI/CD and deployment scripts
output webAppUrl string = appService.outputs.webAppUrl
output webAppName string = appService.outputs.webAppName
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output sqlDatabaseName string = sql.outputs.sqlDatabaseName
output storageAccountName string = storage.outputs.storageAccountName
output keyVaultName string = keyVault.outputs.keyVaultName
output appInsightsName string = appInsights.outputs.appInsightsName
output resourceGroupName string = resourceGroup().name
