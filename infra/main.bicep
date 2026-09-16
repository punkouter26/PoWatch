// Application resources in PoWatch; shared platform resources are referenced, never recreated.
// Production names are shared with CI through deployment.json.
targetScope = 'subscription'

param deployment object = loadJsonContent('deployment.json')

@allowed(['dev', 'staging', 'prod'])
param environment string = 'prod'
param location string = deployment.location
param solutionResourceGroupName string = deployment.solutionResourceGroupName
param sharedResourceGroupName string = deployment.sharedResourceGroupName
param webAppName string = environment == 'prod' ? deployment.webAppName : '${deployment.webAppName}-${environment}'
param planName string = environment == 'prod' ? deployment.planName : '${deployment.planName}-${environment}'
param storageAccountName string = environment == 'prod' ? deployment.storageAccountName : 'powatch${environment}${uniqueString(subscription().subscriptionId, solutionResourceGroupName)}'

resource solutionRg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: solutionResourceGroupName
  location: location
}

resource sharedRg 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: sharedResourceGroupName
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  scope: sharedRg
  name: deployment.appInsightsName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  scope: sharedRg
  name: deployment.keyVaultName
}

module plan 'modules/app-service-plan.bicep' = {
  name: 'powatch-plan-${environment}'
  scope: solutionRg
  params: {
    location: location
    planName: planName
  }
}

module storage 'modules/storage.bicep' = {
  name: 'powatch-storage-${environment}'
  scope: solutionRg
  params: {
    location: location
    storageAccountName: storageAccountName
    blobCorsAllowedOrigins: environment == 'prod' ? [
      'https://${webAppName}.azurewebsites.net'
    ] : [
      'https://${webAppName}.azurewebsites.net'
      'http://localhost:5000'
      'https://localhost:5001'
    ]
  }
}

module webApp 'modules/web-app.bicep' = {
  name: 'powatch-webapp-${environment}'
  scope: solutionRg
  params: {
    location: location
    environment: environment
    webAppName: webAppName
    appServicePlanId: plan.outputs.planId
    appInsightsConnectionString: appInsights.properties.ConnectionString
    tableStorageUri: storage.outputs.tableStorageUri
    keyVaultUri: keyVault.properties.vaultUri
  }
}

module storageAccess 'modules/storage-access.bicep' = {
  name: 'powatch-storage-access-${environment}'
  scope: solutionRg
  params: {
    storageAccountName: storageAccountName
    principalId: webApp.outputs.principalId
  }
}

output webAppDefaultHostname string = webApp.outputs.defaultHostname
output tableStorageUri string = storage.outputs.tableStorageUri
