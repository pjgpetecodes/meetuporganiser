@description('Communication Service name.')
param communicationServiceName string

@description('Principal ID of the identity that needs to send email.')
param principalId string

// Contributor on the Communication Service lets the identity send email via Entra auth
var contributorRoleId = 'b24988ac-6180-42a0-ab88-20f7382dd24c'

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' existing = {
  name: communicationServiceName
}

resource sendRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: communicationService
  name: guid(communicationService.id, principalId, contributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', contributorRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
