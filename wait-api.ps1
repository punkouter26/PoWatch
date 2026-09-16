$ProgressPreference = 'SilentlyContinue'
for ($i = 0; $i -lt 30; $i++) {
    try {
        $r = Invoke-WebRequest -Uri 'https://localhost:5001/health' -UseBasicParsing -TimeoutSec 2 -SkipHttpErrors
        if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500) {
            "up:$($r.StatusCode)"
            break
        }
    } catch {
        Start-Sleep -Seconds 2
    }
}