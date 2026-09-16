Get-Process dotnet -ErrorAction SilentlyContinue | ForEach-Object {
    $id = $_.Id
    $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$id" -ErrorAction SilentlyContinue).CommandLine
    if ($cmd) { $label = $cmd.Substring(0, [Math]::Min(180, $cmd.Length)) } else { $label = '<no command line>' }
    Write-Host "$id :: $label"
}
Write-Host "---"
Write-Host "total: $((Get-Process dotnet -ErrorAction SilentlyContinue).Count)"