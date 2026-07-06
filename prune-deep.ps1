Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

# Deeper inspection of the 3 NEEDS-DEEPER-LOOK suspects.

$checks = @(
    @{
        Name='FluentValidation (in PoWatch.Api)'
        SearchPath='src/PoWatch.Api'
        Patterns=@(
            'using FluentValidation',
            'IValidator<',
            'AbstractValidator<',
            'RuleFor(',
            'FluentValidation\.',
            'ValidateAsync'
        )
    },
    @{
        Name='Microsoft.AspNetCore.Components.WebAssembly.DevServer (in PoWatch.Client)'
        SearchPath='src/PoWatch.Client'
        Patterns=@(
            'WebAssemblyHostBuilder',
            'Microsoft\.AspNetCore\.Components\.WebAssembly\.DevServer',
            'launchSettings',
            'UseBlazorFrameworkFiles'
        )
    },
    @{
        Name='OpenTelemetry.Exporter.OpenTelemetryProtocol (in PoWatch.Api)'
        SearchPath='src/PoWatch.Api'
        Patterns=@(
            'OpenTelemetry\.Exporter',
            'OtlpExporter',
            'UseOtlpExporter',
            'AddOtlpExporter',
            'OtlpExporterOptions'
        )
    }
)

foreach ($c in $checks) {
    Write-Host "=== $($c.Name) ==="
    foreach ($pat in $c.Patterns) {
        $hits = Get-ChildItem -Recurse -Filter *.cs -Path $c.SearchPath `
            | Select-String -Pattern $pat -ErrorAction SilentlyContinue `
            | ForEach-Object { "$($_.Path.Substring($_.Path.LastIndexOf('\')+1)): $($_.Line.Trim())" }
        $cnt = ($hits | Measure-Object).Count
        if ($cnt -gt 0) {
            Write-Host ("  [HIT x{0}] pattern: {1}" -f $cnt, $pat)
            $hits | Select-Object -First 3 | ForEach-Object { Write-Host "    $_" }
        } else {
            Write-Host ("  [   0] pattern: {0}" -f $pat)
        }
    }
    Write-Host ''
}

# Also dump OTLP usage in any config file
Write-Host "=== OTLP exporter usage in any config / cs file (whole repo) ==="
Get-ChildItem -Recurse -Include *.json,*.cs,*.csproj -Path src,tests `
    | Select-String -Pattern 'OpenTelemetryProtocol|otlp|OTLP|Otlp' `
    | ForEach-Object { "$($_.Path.Substring($_.Path.LastIndexOf('\')+1)): $($_.Line.Trim())" } `
    | Select-Object -First 30