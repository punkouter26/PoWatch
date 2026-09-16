Get-Process dotnet -ErrorAction SilentlyContinue | ForEach-Object {
    $id = $_.Id
    $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$id" -ErrorAction SilentlyContinue).CommandLine
    $label = if ($cmd) { $cmd.Substring(0, [Math]::Min(160, $cmd.Length)) } else { '<no command line>' }
    Write-Host "$id :: $label"
}