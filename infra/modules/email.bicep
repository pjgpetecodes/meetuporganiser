@description('Base name for the communication resources.')
param namePrefix string

@description('Data residency location for Communication Services (e.g. Europe, UnitedStates).')
param dataLocation string = 'Europe'

@description('Optional custom sender domain (e.g. example.com). Created customer-managed; DNS records must be added and verified in Azure before it can be linked.')
param customDomain string = ''

@description('Set true once the custom domain is verified in Azure, to link it and send from it.')
param customDomainVerified bool = false

@description('Local part of the sender address on the custom domain.')
param senderUsername string = 'noreply'

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

resource customEmailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = if (!empty(customDomain)) {
  parent: emailService
  name: customDomain
  location: 'global'
  properties: {
    domainManagement: 'CustomerManaged'
  }
}

resource customSender 'Microsoft.Communication/emailServices/domains/senderUsernames@2023-04-01' = if (!empty(customDomain)) {
  parent: customEmailDomain
  name: senderUsername
  properties: {
    username: senderUsername
    displayName: 'Meetup Organiser'
  }
}

var useCustomSender = !empty(customDomain) && customDomainVerified

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: communicationServiceName
  location: 'global'
  properties: {
    dataLocation: dataLocation
    linkedDomains: useCustomSender ? [
      emailDomain.id
      customEmailDomain.id
    ] : [
      emailDomain.id
    ]
  }
}

output endpoint string = 'https://${communicationService.properties.hostName}'
output senderAddress string = useCustomSender ? '${senderUsername}@${customDomain}' : 'DoNotReply@${emailDomain.properties.mailFromSenderDomain}'
output communicationServiceName string = communicationService.name
