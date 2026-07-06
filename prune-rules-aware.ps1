Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

# === Re-test the 21 orphans against NET_RULES-aware patterns ===
#
# NET_RULES context for false-positive reduction:
#   - §4.4: explicit routes /auth/login/{microsoft,fake}, /auth/logout, /auth/me — wired
#   - §3.3: feature slices registered via IEndpointRouteBuilder extension methods
#     (MapArchivesFeature, MapObserverFeature, etc.). These EXTENSION method names
#     live in *Endpoints.cs files, so the Endpoints.cs file itself IS the registration
#     point and shouldn't be flagged.
#   - §2.1: VSA — DTOs in PoWatch.Shared.Models are consumed cross-process, so the
#     filename DOES NOT appear verbatim in the consumer (the type name does; but
#     this is .NET reflection / JsonSerializer so static greps can't see it).
#   - Razor component pages are routed by the <Router> assembly scan in
#     PoWatch.Client (rule §1.4) — same caveat.
#   - Shared.Models has source-gen JSON contexts (rule §1.5 strongly-typed).
#
# For each "orphan", confirm it is in fact wired via one of:
#   1. An extension-method registration (Map<Feature>Feature)
#   2. A DI AddSingleton<>/AddScoped<>/AddHostedService<> registration
#   3. A Razor @page directive
#   4. A type name in PoWatchJsonContext [JsonSerializable]
#   5. A type name in any IServiceCollection.Add...<> generic
#   6. A type name in a *_Map<...>() pattern that shows up in DI bootstrap code

$orphans = @(
    'src\PoWatch.Api\Features\Archives\ArchivesEndpoints.cs'
    'src\PoWatch.Api\Features\Diagnostics\DiagnosticsEndpoints.cs'
    'src\PoWatch.Api\Features\Fhir\FhirEndpoints.cs'
    'src\PoWatch.Api\Features\Identity\IdentityEndpoints.cs'
    'src\PoWatch.Api\Features\Observer\ObserverEndpoints.cs'
    'src\PoWatch.Application\Services\ObservationServiceLog.cs'
    'src\PoWatch.Client\Pages\ObserverHub.razor.cs'
    'src\PoWatch.Client\Pages\ObserverHub.State.razor.cs'
    'src\PoWatch.Client\Pages\ObserverHub.Subjects.razor.cs'
    'src\PoWatch.Client\Services\MonitoringLoopService.cs'
    'src\PoWatch.Shared\Models\ArchivesDtos.cs'
    'src\PoWatch.Shared\Models\BaselineDtos.cs'
    'src\PoWatch.Shared\Models\BlobDtos.cs'
    'src\PoWatch.Shared\Models\DiagnosticsDtos.cs'
    'src\PoWatch.Shared\Models\DriftRadarDtos.cs'
    'src\PoWatch.Shared\Models\HandoffCoachDtos.cs'
    'src\PoWatch.Shared\Models\IdentityDtos.cs'
    'src\PoWatch.Shared\Models\LiveDashboardDtos.cs'
    'src\PoWatch.Shared\Models\ObserverDtos.cs'
    'src\PoWatch.Shared\Models\ReportDtos.cs'
    'src\PoWatch.Shared\Models\StorageResetDtos.cs'
)

# Test patterns per orphan category
foreach ($o in $orphans) {
    $base = Split-Path $o -LeafBase
    Write-Host "=== $base ==="
    $typeNames = switch -Wildcard ($base) {
        '*Endpoints' { @($base) }
        default { @($base) }
    }
    $allFound = @()
    foreach ($t in $typeNames) {
        # DI registration
        $diHits = Get-ChildItem -Recurse -Filter *.cs -Path src |
            Select-String -Pattern ("(AddSingleton|AddScoped|AddHostedService|AddTransient)<" + [regex]::Escape($t) + ">") -ErrorAction SilentlyContinue
        # Map*() extension-method registration on endpoints
        $mapHits = Get-ChildItem -Recurse -Filter *.cs -Path src |
            Select-String -Pattern ("Map" + $t.Replace('Endpoints','Feature') + "\(") -ErrorAction SilentlyContinue
        # Razor @page directive (if it's a .razor.cs partial)
        $pageHits = Get-ChildItem -Recurse -Filter *.razor -Path src |
            Select-String -Pattern ("@page ") -ErrorAction SilentlyContinue
        # JSON context
        $jsonHits = Get-ChildItem -Recurse -Filter *.cs -Path src |
            Select-String -Pattern ("typeof\(" + [regex]::Escape($t) + ")") -ErrorAction SilentlyContinue
        # Generic type instantiation (List<DTO>, IReadOnlyList<DTO>, etc.) and method refs
        $refHits = Get-ChildItem -Recurse -Filter *.cs -Path src |
            Where-Object { $_.FullName -notmatch ([regex]::Escape($o)) } |
            Select-String -Pattern ("\b" + [regex]::Escape($t) + "\b") -ErrorAction SilentlyContinue
        $allFound += $diHits
        $allFound += $mapHits
        $allFound += $jsonHits
        $allFound += $refHits
    }
    $allFound = $allFound | Sort-Object -Property Path -Unique
    $cnt = ($allFound | Measure-Object).Count
    if ($cnt -eq 0) {
        Write-Host "  TRULY ORPHAN (no DI / Map / [JsonSerializable] / type ref)"
    } else {
        Write-Host "  [$cnt hits] Live via:"
        $allFound | Select-Object -First 4 | ForEach-Object {
            Write-Host ("    {0}: {1}" -f ($_.Path.Substring($_.Path.LastIndexOf('\')+1)), $_.Line.Trim())
        }
    }
}