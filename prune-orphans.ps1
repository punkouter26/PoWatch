Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

# === Orphan file scan ===
# A source file is "orphaned" if no other .cs file in the repo references its
# defining type. We approximate by searching for the filename stem.

Write-Host "=== Orphan file scan ==="
$orphans = New-Object System.Collections.Generic.List[object]
Get-ChildItem -Recurse -Filter *.cs -Path src |
    Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' } |
    Where-Object { $_.FullName -notmatch '\.g\.cs$' } | # exclude source-gen
    ForEach-Object {
        $path = $_.FullName
        $name = $_.BaseName
        $dir  = $_.DirectoryName
        # Skip top-level Program.cs / Startup / AssemblyInfo / global usings
        if ($name -in @('Program','AssemblyInfo','GlobalUsings','GlobalUsingsExtensions','ModuleInitializer')) { return }
        # Skip files where the name is itself a partial keyword (unlikely)
        if ($name -match '^[\d]') { return }

        # Build stem: "ArchivesService" -> "ArchivesService", "MapArchivesFeature" -> "ArchivesFeature" (drop leading "Map")
        # Use both raw stem and partial stems
        $stems = @($name)
        if ($name -match '^Map(.+)$') { $stems += $Matches[1] }

        $totalHits = 0
        foreach ($stem in $stems) {
            $hits = Get-ChildItem -Recurse -Filter *.cs -Path src `
                | Where-Object { $_.FullName -ne $path } `
                | Select-String -Pattern ("\b" + [regex]::Escape($stem) + "\b") -ErrorAction SilentlyContinue `
                | Measure-Object
            $totalHits += $hits.Count
        }
        # Same-project `partial` references on different files count too.
        # Plus DI registration searches ("AddSingleton<ArchivesService>" etc.)
        if ($totalHits -eq 0) {
            # Also check for namespace registration: walk all cs files for fully-qualified inclusion
            $namespaceHits = Get-ChildItem -Recurse -Filter *.cs -Path src `
                | Where-Object { $_.FullName -ne $path } `
                | Select-String -Pattern ([regex]::Escape($name)) -ErrorAction SilentlyContinue `
                | Measure-Object
            if ($namespaceHits.Count -gt 0) { $totalHits += $namespaceHits.Count }
        }
        if ($totalHits -eq 0) {
            $orphans.Add([pscustomobject]@{ Path = $path.Substring($path.IndexOf('src\')); Stems = ($stems -join ', ') })
        }
    }

if ($orphans.Count -eq 0) {
    Write-Host "(no orphans)"
} else {
    $orphans | Format-Table -AutoSize -Wrap | Out-String -Width 200
}