$ports = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -in 5000,5001,7000,7001,8080 }
if ($ports) {
    $ports | Select-Object LocalPort, OwningProcess | Format-Table -AutoSize
} else {
    Write-Host "no api listening on dev ports"
}