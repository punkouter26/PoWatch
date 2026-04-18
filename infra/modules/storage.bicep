// Azure Storage Account (Table + Blob) for PoWatch.
// Placed in PoWatch-App-RG (app-specific, not PoWatch-Shared-RG).
// Access via Managed Identity — no connection strings.

param location string
param environment string

@description('Allowed browser origins for direct Blob SAS upload/download requests')
param blobCorsAllowedOrigins array = [
  'https://app-powatch.azurewebsites.net'
  'http://localhost:5000'
  'http://localhost:7000'
  'http://localhost:7099'
]

var storageName = 'powatch${environment}sa'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowSharedKeyAccess: false   // Managed Identity only — no storage keys
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  name: '${storageAccount.name}/default'
  properties: {
    cors: {
      corsRules: [
        {
          allowedOrigins: blobCorsAllowedOrigins
          allowedMethods: [
            'GET'
            'PUT'
            'HEAD'
            'OPTIONS'
          ]
          allowedHeaders: [
            '*'
          ]
          exposedHeaders: [
            'ETag'
            'x-ms-*'
          ]
          maxAgeInSeconds: 86400
        }
      ]
    }
  }
}

// Blob container for significant-event images
resource imageContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${storageAccount.name}/default/powatch-images'
  properties: {
    publicAccess: 'None'
  }
}

// Table service (implicit — no separate resource needed for Table Storage)

output tableStorageUri string = storageAccount.properties.primaryEndpoints.table
output blobStorageUri string = storageAccount.properties.primaryEndpoints.blob
output storageAccountId string = storageAccount.id
