Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

# Scan each suspect package by searching source for its root namespace(s).
# Returns a verdict: USED / UNUSED / TRANSITIVE_ONLY.

$suspects = @(
    # Project, Package, Root namespaces to grep
    @{ Project='src/PoWatch.Api'; Pkg='OpenTelemetry.Exporter.OpenTelemetryProtocol'; Ns=@('OpenTelemetry.Exporter','OpenTelemetry.Protocol') },
    @{ Project='src/PoWatch.Api'; Pkg='FluentValidation'; Ns=@('FluentValidation') },
    @{ Project='src/PoWatch.Application'; Pkg='Microsoft.Extensions.DependencyInjection.Abstractions'; Ns=@('Microsoft.Extensions.DependencyInjection.Abstractions') },
    @{ Project='src/PoWatch.Application'; Pkg='Microsoft.Extensions.Logging.Abstractions'; Ns=@('Microsoft.Extensions.Logging.Abstractions') },
    @{ Project='src/PoWatch.Client'; Pkg='Microsoft.AspNetCore.Components.WebAssembly.DevServer'; Ns=@() }, # config-only
    @{ Project='src/PoWatch.Client'; Pkg='Radzen.Blazor'; Ns=@('Radzen','Radzen.Blazor') }
)

$out = foreach ($s in $suspects) {
    $projectPath = Resolve-Path $s.Project -ErrorAction SilentlyContinue
    if (-not $projectPath) { continue }
    $totalHits = 0
    foreach ($ns in $s.Ns) {
        $hits = Get-ChildItem -Recurse -Filter *.cs -Path $s.Project `
            | Select-String -Pattern ("using\s+" + [regex]::Escape($ns) + "[.;]") -ErrorAction SilentlyContinue `
            | Measure-Object
        $totalHits += $hits.Count
    }
    # Also dump declared transitive dependents so we can judge TRANSITIVE_ONLY
    $projFile = Join-Path $s.Project ($s.Project.Split('/')[-1] + '.csproj')
    $transitiveHint = (Select-String -Path $projFile -Pattern ('Include="' + $s.Pkg + '"') -ErrorAction SilentlyContinue | Measure-Object).Count
    [pscustomobject]@{
        Project = $s.Project
        Package = $s.Pkg
        DirectHits = $totalHits
        InCsproj = if ($transitiveHint -gt 0) { 'YES' } else { 'NO' }
        Verdict  = if ($totalHits -gt 0) { 'USED' }
                   elseif ($s.Pkg -match 'DevServer|Radzen.Blazor|FluentValidation') { 'NEEDS-DEEPER-LOOK' }
                   else { 'TRANSITIVE_ONLY' }
    }
}

$out | Format-Table -AutoSize -Wrap | Out-String -Width 200