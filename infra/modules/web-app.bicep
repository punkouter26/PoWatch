// Azure App Service (Web App) — hosts PoWatch API + Blazor WASM.
// Placed in PoWatch-App-RG. Uses free App Service Plan from PoWatch-Shared-RG.
// System-assigned Managed Identity used for all Azure resource access.

param location string
param environment string
param appServicePlanId string
param appInsightsConnectionString string
param tableStorageUri string
param blobStorageUri string
param keyVaultUri string

var webAppName = 'app-powatch'

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  identity: {
    type: 'SystemAssigned'   // Managed Identity — no stored credentials
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environment == 'dev' ? 'Development' : 'Production'
        }
        {
          name: 'KeyVaultUri'
          value: keyVaultUri
        }
        {
          name: 'ApplicationInsights__ConnectionString'
          value: appInsightsConnectionString
        }
        {
          name: 'TableStorage__ServiceUri'
          value: tableStorageUri
        }
        {
          name: 'BlobStorage__ServiceUri'
          value: blobStorageUri
        }
        {
          name: 'FeatureFlags__UseMockAi'
          value: 'false'
        }
      ]
    }
  }
}

// Grant Managed Identity "Storage Table Data Contributor" on the storage account
// (storage RBAC assignment done in storage module or post-deployment script)

output defaultHostname string = webApp.properties.defaultHostName
output principalId string = webApp.identity.principalId
