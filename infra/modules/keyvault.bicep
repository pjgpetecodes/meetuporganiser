@description('Location for Key Vault.')
param location string

@description('Key Vault name.')
param keyVaultName string

@description('Tenant ID for authentication.')
param tenantId string = tenant().tenantId

@description('Secrets to initialize in Key Vault.')
@secure()
param secrets object = {}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenantId
    enableRbacAuthorization: true
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

// Add any secrets passed in
resource keyVaultSecrets 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = [for item in items(secrets): {
  parent: keyVault
  name: item.key
  properties: {
    value: item.value
  }
}]

output keyVaultId string = keyVault.id
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
