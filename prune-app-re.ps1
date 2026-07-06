Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

Write-Host "=== DI / Logging namespace usage in PoWatch.Application ==="
$pattern = '(IServiceCollection|IServiceProvider|ILogger|ILoggerFactory|IMetrics|OptionsCreate|Configure<|GetRequiredService|GetService)'
Get-ChildItem -Recurse -Filter *.cs -Path src\PoWatch.Application |
    Select-String -Pattern $pattern -ErrorAction SilentlyContinue |
    ForEach-Object { "$($_.Path | Split-Path -Leaf): $($_.Line.Trim())" } |
    Select-Object -First 25

Write-Host ""
Write-Host "=== Re-exposure in public API (public method signatures) ==="
Get-ChildItem -Recurse -Filter *.cs -Path src\PoWatch.Application |
    Select-String -Pattern '^public\s+.*(IServiceCollection|ILogger|IOptions)' -ErrorAction SilentlyContinue |
    Select-Object -First 10 |
    ForEach-Object { "$($_.Path | Split-Path -Leaf): $($_.Line.Trim())" }