// App Service Plan — Free tier (F1), Linux, shared across PoWatch resources in PoWatch-Shared-RG.
// Note: Actual deployed plan name is 'asp-poshared-linux' in PoShared RG.

param location string
param environment string

var planName = 'powatch-plan-${environment}'

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
