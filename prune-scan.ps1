Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$results = New-Object System.Collections.Generic.List[object]

Get-ChildItem -Recurse src,tests -Filter *.csproj | ForEach-Object {
    $proj = $_
    $projDir = Split-Path $proj.FullName -Parent
    $refs = Select-String -Path $proj.FullName -Pattern '<PackageReference Include="([^"]+)"' `
        | ForEach-Object { $_.Matches[0].Groups[1].Value }
    foreach ($pkg in $refs) {
        $asm = ($pkg -split '\.')[-1]
        $hits = (Get-ChildItem -Recurse -Filter *.cs -Path $projDir `
            | Select-String -Pattern ([regex]::Escape($asm)) -ErrorAction SilentlyContinue `
            | Measure-Object).Count
        $results.Add([pscustomobject]@{
            Project = $proj.Name
            Package = $pkg
            Hits    = $hits
            Marker  = if ($hits -eq 0) { 'SUSPECT' } else { '' }
        })
    }
}

$results | Export-Csv -NoTypeInformation -Path "$PSScriptRoot\prune-scan-pkgs.csv"
Write-Host "=== ALL ROWS ==="
$results | Format-Table -AutoSize | Out-String -Width 220
Write-Host "=== SUSPECTS ==="
$results | Where-Object { $_.Marker -eq 'SUSPECT' } | Format-Table -AutoSize | Out-String -Width 220