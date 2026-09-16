param storageAccountName string
param principalId string

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

// The application's storage data-plane permissions, declared instead of repaired manually.
var roles = [
  '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3' // Storage Table Data Contributor
  'ba92f5b4-2d11-453d-a403-e96b0029c9fe' // Storage Blob Data Contributor
]

resource access 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for role in roles: {
  name: guid(storage.id, principalId, role)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]
