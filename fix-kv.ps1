$secret = Get-Content C:\Users\punko\Downloads\PoWatch\.powatch-dev-secret.json -Raw | ConvertFrom-Json
az keyvault secret delete --vault-name kv-poshared --name "PoWatch--AzureAd--ClientId" --output none
az keyvault secret delete --vault-name kv-poshared --name "PoWatch--AzureAd--ClientSecret" --output none
az keyvault secret set --vault-name kv-poshared --name "AzureAd--ClientId" --value "a11f8a90-34c1-4d7f-bd8a-d3a323d0234e" --output none
az keyvault secret set --vault-name kv-poshared --name "AzureAd--ClientSecret" --value "$($secret.password)" --output none
Write-Host "OK"