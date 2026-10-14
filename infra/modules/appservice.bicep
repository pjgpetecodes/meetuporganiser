@description('Location for App Service resources.')
param location string

@description('App Service Plan name.')
param appServicePlanName string

@description('Web App name.')
param webAppName string

@description('App Service Plan SKU.')
param skuName string = 'B1'

@description('App Service Plan Tier.')
param skuTier string = 'Basic'

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('SQL Server connection string.')
@secure()
param sqlConnectionString string

@description('Blob service endpoint of the storage account (accessed via managed identity).')
param storageAccountUrl string

@description('Entra ID Client ID.')
param entraClientId string = ''

@description('Entra ID Tenant ID.')
param entraTenantId string = tenant().tenantId

@description('Entra ID Client Secret.')
@secure()
param entraClientSecret string = ''

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    reserved: false // Windows App Service Plan
  }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v10.0'
      use32BitWorkerProcess: false
      alwaysOn: skuName != 'F1' && skuName != 'D1'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          name: 'ConnectionStrings__DefaultConnection'
          value: sqlConnectionString
        }
        {
          name: 'Storage__AccountUrl'
          value: storageAccountUrl
        }
        {
          name: 'Storage__ContainerName'
          value: 'templates'
        }
        {
          name: 'Storage__PhotosContainer'
          value: 'speaker-photos'
        }
        {
          name: 'Storage__AssetsContainer'
          value: 'generated-assets'
        }
        {
          name: 'AzureAd__Instance'
          value: environment().authentication.loginEndpoint
        }
        {
          name: 'AzureAd__TenantId'
          value: entraTenantId
        }
        {
          name: 'AzureAd__ClientId'
          value: entraClientId
        }
        {
          name: 'AzureAd__ClientSecret'
          value: entraClientSecret
        }
        {
          name: 'AzureAd__CallbackPath'
          value: '/signin-oidc'
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
      ]
    }
  }
}

output webAppId string = webApp.id
output webAppName string = webApp.name
output defaultHostName string = webApp.properties.defaultHostName
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output principalId string = webApp.identity.principalId
