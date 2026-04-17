// App Service Plan — Free tier (F1), shared across Po* apps in PoShared RG.

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
  properties: {
    reserved: false
  }
}

output planId string = plan.id
