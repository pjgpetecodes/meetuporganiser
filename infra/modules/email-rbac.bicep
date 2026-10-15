@description('Communication Service name.')
param communicationServiceName string

@description('Principal ID of the identity that needs access.')
param principalId string

@description('Role definition GUID. Defaults to Contributor, which lets an identity send email via Entra auth.')
param roleDefinitionGuid string = 'b24988ac-6180-42a0-ab88-20f7382dd24c'

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' existing = {
  name: communicationServiceName
}

resource sendRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: communicationService
  name: guid(communicationService.id, principalId, roleDefinitionGuid)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleDefinitionGuid)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
