$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path $repoRoot 'src'

foreach ($projectDir in Get-ChildItem -LiteralPath $sourceRoot -Directory) {
    if ($projectDir.Name -cnotmatch '^PoWatch\.[A-Z][A-Za-z]+$') {
        throw "Invalid project name: $($projectDir.Name)"
    }
    foreach ($source in Get-ChildItem -LiteralPath $projectDir.FullName -File -Recurse) {
        $relativePath = [IO.Path]::GetRelativePath($projectDir.FullName, $source.FullName)
        $parts = $relativePath -split '[\\/]'
        if ($parts[0] -in 'bin', 'obj', 'wwwroot') { continue }
        if ($parts.Count -gt 3) { throw "Source exceeds two directory levels: $relativePath" }
        if ($source.Extension -eq '.cs' -and $source.BaseName -cnotmatch '^[A-Z][A-Za-z0-9.]*$') {
            throw "Use PascalCase source filenames: $relativePath"
        }
    }
    $projectPath = Join-Path $projectDir.FullName "$($projectDir.Name).csproj"
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    if ($project.SelectNodes('//PackageReference[@Version]').Count -gt 0) {
        throw "Package versions must live in Directory.Packages.props: $projectPath"
    }
}

if (@(Get-ChildItem -LiteralPath $repoRoot -Filter '*.ps1' -File).Count -gt 0) {
    throw 'Developer scripts belong in SCRIPTS.'
}
foreach ($script in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File) {
    if ($script.BaseName -cnotmatch '^[a-z]+(-[a-z]+)*$') { throw "Use lowercase script names: $($script.Name)" }
}

$indexPath = Join-Path $sourceRoot 'PoWatch.Client/wwwroot/index.html'
$index = Get-Content -LiteralPath $indexPath -Raw
foreach ($match in [regex]::Matches($index, '(?:href|src)="((?:js|css|lib|media)/[^"]+)"')) {
    $assetPath = Join-Path (Split-Path $indexPath -Parent) $match.Groups[1].Value
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) { throw "Missing shell asset: $assetPath" }
}
# The app is a stats webcam, not a care monitor: no caregiver-era vocabulary in code, pages or tests.
# Vendored libraries (wwwroot/lib) and build output are exempt; "shift" and "acknowledge" are only
# banned in their caregiver sense, since both are ordinary words elsewhere (bit shifts, batch acks).
$banned = '(?i)caregiver|clinical|handoff|nurse|patient|shift ?(window|clock|report)|mid-shift|acknowledg(e)?ment'
$scanned = '.cs', '.razor', '.js', '.css', '.json', '.html', '.http'
$hits = foreach ($root in 'src', 'tests') {
    Get-ChildItem -LiteralPath (Join-Path $repoRoot $root) -File -Recurse |
        Where-Object { $_.Extension -in $scanned -and $_.FullName -notmatch '[\\/](bin|obj|lib)[\\/]' } |
        Select-String -Pattern $banned
}
if ($hits) {
    $hits | ForEach-Object { Write-Host "$([IO.Path]::GetRelativePath($repoRoot, $_.Path)):$($_.LineNumber): $($_.Line.Trim())" }
    throw "Caregiver-era vocabulary found ($(@($hits).Count) lines)."
}

Write-Host 'Directory structure, naming, package versions, shell assets, and vocabulary passed.'
