// Azure Storage Account (Table + Blob) for PoWatch.
// Placed in PoWatch, the application resource group.
// Access via Managed Identity — no connection strings.

param location string
param storageAccountName string

@description('Allowed application origins for evidence upload and download')
param blobCorsAllowedOrigins array

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
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
  parent: storageAccount
  name: 'default'
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
  parent: blobService
  name: 'significant-images'
  properties: {
    publicAccess: 'None'
  }
}

// Table service (implicit — no separate resource needed for Table Storage)

output tableStorageUri string = storageAccount.properties.primaryEndpoints.table
output blobStorageUri string = storageAccount.properties.primaryEndpoints.blob
output storageAccountId string = storageAccount.id
