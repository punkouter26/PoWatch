// PoWatch Azure Infrastructure
// Subscription: Punkouter26 (bbb8dfbe-9169-432f-9b7a-fbf861b51037)
//
// Resource layout:
//   rg-platform-shared-prod-eus2 — Shared platform services
//   rg-powatch-prod-wus2         — App Service, Table Storage, Blob Storage, Key Vault
//
// All secrets accessed via Managed Identity — no connection strings in app config.
// Run: az deployment sub create --location westus2 --template-file infra/main.bicep

targetScope = 'subscription'

@description('Environment name (dev, staging, prod)')
param environment string = 'prod'

@description('Azure region for all resources')
param location string = 'westus2'

// -------------------------------------------------------
// Resource Groups
// -------------------------------------------------------
resource poSharedRg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-platform-shared-prod-eus2'
  location: 'eastus2'
}

resource poWatchRg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-powatch-prod-wus2'
  location: location
}

// -------------------------------------------------------
// PoWatch shared services: Log Analytics + App Insights
// -------------------------------------------------------
module sharedObservability 'modules/shared-observability.bicep' = {
  name: 'powatch-shared-observability'
  scope: poSharedRg
  params: {
    location: location
    environment: environment
  }
}

// -------------------------------------------------------
// PoWatch shared services: App Service Plan (free tier)
// -------------------------------------------------------
module appServicePlan 'modules/app-service-plan.bicep' = {
  name: 'powatch-app-service-plan'
  scope: poSharedRg
  params: {
    location: location
    environment: environment
  }
}

// -------------------------------------------------------
// PoWatch app resource group: Storage (Table + Blob), Key Vault, Web App
// -------------------------------------------------------
module poWatchStorage 'modules/storage.bicep' = {
  name: 'powatch-storage'
  scope: poWatchRg
  params: {
    location: location
    environment: environment
  }
}

module poWatchKeyVault 'modules/key-vault.bicep' = {
  name: 'powatch-keyvault'
  scope: poWatchRg
  params: {
    location: location
    environment: environment
  }
}

module poWatchWebApp 'modules/web-app.bicep' = {
  name: 'powatch-webapp'
  scope: poWatchRg
  params: {
    location: location
    environment: environment
    appServicePlanId: appServicePlan.outputs.planId
    appInsightsConnectionString: sharedObservability.outputs.appInsightsConnectionString
    tableStorageUri: poWatchStorage.outputs.tableStorageUri
    blobStorageUri: poWatchStorage.outputs.blobStorageUri
    keyVaultUri: poWatchKeyVault.outputs.keyVaultUri
  }
}

// -------------------------------------------------------
// Outputs
// -------------------------------------------------------
output webAppDefaultHostname string = poWatchWebApp.outputs.defaultHostname
output keyVaultUri string = poWatchKeyVault.outputs.keyVaultUri
output tableStorageUri string = poWatchStorage.outputs.tableStorageUri
