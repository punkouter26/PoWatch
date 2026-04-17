// Azure Key Vault for PoWatch app-specific secrets.
// Placed in PoWatch-RG. Access via Managed Identity (RBAC model).
// Secret naming convention: app-specific secrets prefixed with "powatch-".
// Shared secrets (e.g. shared App Insights key) have no prefix.

param location string
param environment string

var kvName = 'powatch-kv-${environment}'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true    // RBAC model — no access policies
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enabledForDeployment: false
    enabledForTemplateDeployment: false
    enabledForDiskEncryption: false
    publicNetworkAccess: 'Enabled'
  }
}

output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultId string = keyVault.id
