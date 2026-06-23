// App Service Plan — Free tier (F1), Linux, shared across PoWatch resources in PoWatch-Shared-RG.
// Name follows asp-<app>-<os>-<sku>-<env>-<region>-001.

param location string
param environment string

var planName = 'asp-powatch-linux-f1-${environment}-wus2-001'

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: {
    name: 'F1'
    tier: 'Free'
  }
  kind: 'linux'
  properties: {
    reserved: true   // required for Linux
  }
}

output planId string = plan.id
