@description('Base name for the communication resources.')
param namePrefix string

@description('Data residency location for Communication Services (e.g. Europe, UnitedStates).')
param dataLocation string = 'Europe'

var emailServiceName = 'email-${namePrefix}'
var communicationServiceName = 'acs-${namePrefix}'

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: emailServiceName
  location: 'global'
  properties: {
    dataLocation: dataLocation
  }
}

resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  properties: {
    domainManagement: 'AzureManaged'
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: communicationServiceName
  location: 'global'
  properties: {
    dataLocation: dataLocation
    linkedDomains: [
      emailDomain.id
    ]
  }
}

output endpoint string = 'https://${communicationService.properties.hostName}'
output senderAddress string = 'DoNotReply@${emailDomain.properties.mailFromSenderDomain}'
output communicationServiceName string = communicationService.name
