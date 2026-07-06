Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

# Wave-3 only: list public types per project, then count cross-project references.
# Rule §2.1: types in feature slices are usually internal to that slice — flagging
# them as "unused" would be a false positive. We only flag truly cross-project
# surface.

Write-Host "=== Public types per project (informational; rarely pruned in VSA) ==="

$projects = @{
    'PoWatch.Shared'      = 'src\PoWatch.Shared'
    'PoWatch.Application' = 'src\PoWatch.Application'
    'PoWatch.Infrastructure' = 'src\PoWatch.Infrastructure'
    'PoWatch.Domain'      = 'src\PoWatch.Domain'
}

# Just count public types per project for situational awareness.
foreach ($k in $projects.Keys) {
    $publicTypes = Get-ChildItem -Recurse -Filter *.cs -Path $projects[$k] |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
        Select-String -Pattern '^\s*public\s+(sealed\s+|partial\s+)?(class|record|interface|enum|struct)\s+(\w+)' |
        ForEach-Object { $_.Matches[0].Groups[3].Value }
    Write-Host "  $k : $($publicTypes.Count) public types"
}

Write-Host ""
Write-Host "=== Application project DependencyInjection entry-points (sanity) ==="
Get-ChildItem -Recurse -Filter DependencyInjection.cs -Path src | ForEach-Object {
    Write-Host "  $($_.FullName.Substring($_.FullName.IndexOf('src')))"
}