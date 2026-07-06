Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

Write-Host "=== ObservationServiceLog extension-method call sites ==="
$members = 'PollStart','IngestIgnoredLoopDisabled','PollDropped','RejectedBySanitizer','ClinicalOutlier'
foreach ($m in $members) {
    $hits = Get-ChildItem -Recurse -Filter *.cs -Path src |
        Select-String -Pattern ("\.$m\(") -ErrorAction SilentlyContinue
    $cnt = ($hits | Measure-Object).Count
    Write-Host "  $m : $cnt call sites"
    $hits | Select-Object -First 3 | ForEach-Object {
        Write-Host "    $($_.Path | Split-Path -Leaf): $($_.Line.Trim())"
    }
}