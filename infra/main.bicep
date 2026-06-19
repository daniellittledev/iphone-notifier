// Infrastructure for the iPhone push-notification relay.
// Deploys: Storage (Functions + Tables), Log Analytics + App Insights,
// Consumption Function App (.NET 8 isolated), Notification Hub (APNs token auth),
// and Key Vault holding secrets consumed via the Function's managed identity.

@description('Base name used to derive resource names. Lowercase letters/numbers.')
@minLength(3)
@maxLength(11)
param namePrefix string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Apple app bundle id (audience for Sign in with Apple tokens).')
param appleBundleId string

@description('Apple APNs auth key (.p8 contents) for the Notification Hub.')
@secure()
param apnsKey string

@description('Apple APNs Key ID (10 chars).')
param apnsKeyId string

@description('Apple Developer Team ID (10 chars).')
param apnsTeamId string

@description('APNs environment: Sandbox for dev builds, Production for TestFlight/App Store.')
@allowed([ 'Sandbox', 'Production' ])
param apnsEnvironment string = 'Sandbox'

@description('Secret used to sign our own session JWTs (32+ random chars).')
@secure()
param jwtSigningKey string

var suffix = uniqueString(resourceGroup().id, namePrefix)
var storageName = toLower('${namePrefix}st${substring(suffix, 0, 6)}')
var functionAppName = '${namePrefix}-func-${substring(suffix, 0, 5)}'
var planName = '${namePrefix}-plan'
var insightsName = '${namePrefix}-ai'
var logName = '${namePrefix}-logs'
var keyVaultName = toLower('${namePrefix}kv${substring(suffix, 0, 6)}')
var nhNamespaceName = '${namePrefix}-nhns-${substring(suffix, 0, 5)}'
var nhName = '${namePrefix}-hub'

var apnsEndpoint = apnsEnvironment == 'Production'
  ? 'https://api.push.apple.com:443/3/device'
  : 'https://api.development.push.apple.com:443/3/device'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logName
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: insightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource nhNamespace 'Microsoft.NotificationHubs/namespaces@2023-09-01' = {
  name: nhNamespaceName
  location: location
  sku: { name: 'Free' }
  properties: {}
}

resource notificationHub 'Microsoft.NotificationHubs/namespaces/notificationHubs@2023-09-01' = {
  parent: nhNamespace
  name: nhName
  location: location
  properties: {
    apnsCredential: {
      properties: {
        token: apnsKey
        keyId: apnsKeyId
        appName: appleBundleId
        appId: apnsTeamId
        endpoint: apnsEndpoint
      }
    }
  }
}

// Full-access rule used by the relay to send notifications.
resource nhAuthRule 'Microsoft.NotificationHubs/namespaces/notificationHubs/authorizationRules@2023-09-01' existing = {
  parent: notificationHub
  name: 'DefaultFullSharedAccessSignature'
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
  }
}

resource jwtSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'JwtSigningKey'
  properties: { value: jwtSigningKey }
}

resource nhConnSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'NotificationHubConnectionString'
  properties: {
    value: nhAuthRule.listKeys().primaryConnectionString
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: { name: 'Y1', tier: 'Dynamic' }
  properties: { reserved: true } // Linux
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage', value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}' }
        { name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING', value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}' }
        { name: 'WEBSITE_CONTENTSHARE', value: toLower(functionAppName) }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'TableStorageConnectionString', value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}' }
        { name: 'NotificationHubName', value: nhName }
        { name: 'AppleBundleId', value: appleBundleId }
        { name: 'JwtIssuer', value: 'iphone-notifier' }
        { name: 'JwtAudience', value: 'iphone-notifier-app' }
        { name: 'JwtLifetimeDays', value: '30' }
        { name: 'JwtSigningKey', value: '@Microsoft.KeyVault(SecretUri=${jwtSecret.properties.secretUri})' }
        { name: 'NotificationHubConnectionString', value: '@Microsoft.KeyVault(SecretUri=${nhConnSecret.properties.secretUri})' }
      ]
    }
  }
}

// Grant the Function's managed identity read access to Key Vault secrets.
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
resource kvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionApp.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output functionAppName string = functionApp.name
output functionBaseUrl string = 'https://${functionApp.properties.defaultHostName}/api'
output notificationHubName string = nhName
output keyVaultName string = keyVault.name
