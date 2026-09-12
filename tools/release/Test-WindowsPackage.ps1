param([Parameter(Mandatory = $true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
if (Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object { $_.Name -in @('appsettings.user.json','appsettings.other.json') -or $_.Extension -in @('.keystore','.jks','.pfx') }) {
    throw 'Private files must not enter the distribution.'
}
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$runner = Join-Path $package 'Start-Exporter.ps1'
$process = Start-Process powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $runner + '"'),'-OfflineCheck','-NoBrowser','-Port',$port) -PassThru -WindowStyle Hidden
try {
    $base = "http://127.0.0.1:$port"
    $ready = $false
    for ($i = 0; $i -lt 80; $i++) {
        if ($process.HasExited) { throw 'Packaged launcher exited early.' }
        try {
            $response = Invoke-WebRequest "$base/safe-export-ui" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200 -and $response.Headers['X-MementoMori-Validation'] -eq 'offline-no-account' -and $response.Content.Contains('MementoMori')) { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw 'Packaged UI was not reachable.' }
    try { Invoke-WebRequest "$base/safe-export" -UseBasicParsing -TimeoutSec 2 | Out-Null; throw 'Offline mode unexpectedly allowed export.' }
    catch {
        if ($null -eq $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 503) { throw }
    }
    Invoke-WebRequest "$base/safe-export-ui/shutdown" -Method Post -UseBasicParsing -TimeoutSec 5 | Out-Null
    if (-not $process.WaitForExit(15000)) { throw 'Packaged launcher did not shut down cleanly.' }
    if ($process.ExitCode -ne 0) { throw 'Packaged launcher returned failure.' }
    Write-Host 'PASS: self-contained apphost, PowerShell launcher, real export UI, offline refusal, graceful shutdown.'
} finally {
    if (-not $process.HasExited) { & taskkill.exe /PID $process.Id /T /F | Out-Null }
    $process.Dispose()
}
