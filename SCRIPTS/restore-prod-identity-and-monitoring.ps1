# Restores production after the 2026-07-06 500.30 outage and sets up availability monitoring.
# Root cause: AzureStorage:ServiceUri was set in App Service settings (switching storage auth to
# managed identity) but the app had no managed identity, so AzureStorageInitializer.StartAsync
# crashed the host on every recycle. See docs/technical/deployment-guidelines.md.
#
# Run once, signed in as an owner of the subscription:  ./scripts/restore-prod-identity-and-monitoring.ps1

$ErrorActionPreference = 'Stop'
$app = 'app-powatch-win'
$rg  = 'PoWatch'
$sa  = 'powatchsa'
$alertEmail = 'punkouter26@gmail.com'

Write-Host '1/6 Enabling system-assigned managed identity...'
az webapp identity assign -n $app -g $rg --output none
$principal = az webapp identity show -n $app -g $rg --query principalId -o tsv
Write-Host "    principalId: $principal"

Write-Host '2/6 Granting storage data-plane roles (tables + blobs)...'
$saId = az storage account show -n $sa -g $rg --query id -o tsv
az role assignment create --assignee-object-id $principal --assignee-principal-type ServicePrincipal `
    --role 'Storage Table Data Contributor' --scope $saId --output none
az role assignment create --assignee-object-id $principal --assignee-principal-type ServicePrincipal `
    --role 'Storage Blob Data Contributor' --scope $saId --output none

Write-Host '3/6 Enabling App Service Health check on /health...'
az webapp config set -n $app -g $rg --generic-configurations '{\"healthCheckPath\": \"/health\"}' --output none

Write-Host '4/6 Creating action group (email alert)...'
az monitor action-group create -n ag-powatch-email -g $rg --short-name powatch `
    --action email admin $alertEmail --output none

Write-Host '5/6 Creating availability alert (Http5xx and HealthCheckStatus)...'
$appId = az webapp show -n $app -g $rg --query id -o tsv
az monitor metrics alert create -n 'powatch-http5xx' -g $rg --scopes $appId `
    --condition 'total Http5xx > 5' --window-size 5m --evaluation-frequency 1m --severity 1 `
    --action ag-powatch-email `
    --description 'PoWatch is returning 5xx responses (possible 500.30 startup failure).' --output none
az monitor metrics alert create -n 'powatch-healthcheck' -g $rg --scopes $appId `
    --condition 'avg HealthCheckStatus < 100' --window-size 5m --evaluation-frequency 1m --severity 1 `
    --action ag-powatch-email `
    --description 'PoWatch /health check is failing on one or more instances.' --output none

Write-Host '6/6 Restarting app and verifying /health (RBAC can take a few minutes to propagate)...'
az webapp restart -n $app -g $rg --output none
$deadline = (Get-Date).AddMinutes(8)
do {
    Start-Sleep -Seconds 20
    try { $status = (Invoke-WebRequest "https://$app.azurewebsites.net/health" -TimeoutSec 30 -SkipHttpErrorCheck).StatusCode }
    catch { $status = 0 }
    Write-Host "    /health -> $status"
} while ($status -ne 200 -and (Get-Date) -lt $deadline)

if ($status -eq 200) { Write-Host 'Production restored and healthy.' }
else { Write-Warning 'Still unhealthy after 8 minutes. Re-download logs: az webapp log download -n app-powatch-win -g PoWatch --log-file logs.zip and read LogFiles/eventlog.xml.' }
