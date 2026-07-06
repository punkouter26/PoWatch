Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

Write-Host "=== Razor @page directives (rule §1.4: Router auto-discovers) ==="
Get-ChildItem -Recurse -Filter *.razor -Path src\PoWatch.Client\Pages |
    ForEach-Object { $pages = Select-String -Path $_.FullName -Pattern '@page' | ForEach-Object { $_.Line.Trim() }; "$($_.Name): $($pages -join ' | ')" }

Write-Host ""
Write-Host "=== ObserverHub component usage ==="
Get-ChildItem -Recurse -Filter *.razor -Path src\PoWatch.Client |
    Select-String -Pattern 'ObserverHub' |
    ForEach-Object { "$($_.Path | Split-Path -Leaf): $($_.Line.Trim())" } |
    Select-Object -First 6

Write-Host ""
Write-Host "=== MonitoringLoopService registration / usage ==="
Get-ChildItem -Recurse -Filter *.cs -Path src |
    Select-String -Pattern 'MonitoringLoopService' |
    ForEach-Object { "$($_.Path | Split-Path -Leaf): $($_.Line.Trim())" }

Write-Host ""
Write-Host "=== ObservationServiceLog usage ==="
Get-ChildItem -Recurse -Filter *.cs -Path src |
    Select-String -Pattern 'ObservationServiceLog' |
    ForEach-Object { "$($_.Path | Split-Path -Leaf): $($_.Line.Trim())" }