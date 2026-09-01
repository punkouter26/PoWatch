$ErrorActionPreference = 'Continue'
try {
    $resp = Invoke-WebRequest -Uri 'http://localhost:5000/health' -UseBasicParsing -TimeoutSec 15 -Headers @{ Accept = 'application/json' }
    Write-Output "STATUS: $($resp.StatusCode)"
    Write-Output "---"
    Write-Output $resp.Content
} catch {
    Write-Output "unreachable: $($_.Exception.Message)"
}
