// Shared Log Analytics Workspace + Application Insights
// Placed in PoWatch-Shared-RG; connection string shared across PoWatch resources.

param location string
param environment string

var workspaceName = 'log-powatch-ops-${environment}-eus2-001'
var appInsightsName = 'appi-powatch-${environment}-eus2-001'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

output appInsightsConnectionString string = appInsights.properties.ConnectionString
output logAnalyticsWorkspaceId string = logAnalytics.id
