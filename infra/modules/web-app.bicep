// Windows App Service hosts the API and WASM client on one origin.
param location string
param environment string
param webAppName string
param appServicePlanId string
@secure()
param appInsightsConnectionString string
param tableStorageUri string
param keyVaultUri string

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      use32BitWorkerProcess: false
      healthCheckPath: '/health'
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environment == 'dev' ? 'Development' : environment == 'staging' ? 'Staging' : 'Production'
        }
        {
          name: 'KeyVault__Uri'
          value: keyVaultUri
        }
        {
          name: 'ApplicationInsights__ConnectionString'
          value: appInsightsConnectionString
        }
        {
          name: 'AzureStorage__ServiceUri'
          value: tableStorageUri
        }
        {
          name: 'FeatureFlags__UseMockAi'
          value: 'false'
        }
      ]
    }
  }
}

output defaultHostname string = webApp.properties.defaultHostName
output principalId string = webApp.identity.principalId
