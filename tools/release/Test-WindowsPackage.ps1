param([string]$Package = 'artifacts/windows/MementoMori-Exporter-win-x64-preview.zip')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$zip = (Resolve-Path (Join-Path $root $Package)).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ('mm-exporter-smoke-' + [guid]::NewGuid().ToString('N'))
$oldData = $env:MEMENTOMORI_EXPORTER_DATA_DIR
$oldPort = $env:MEMENTOMORI_EXPORTER_PORT
$process = $null
$checks = @()
try {
    $unpacked = Join-Path $work 'package with spaces'
    Expand-Archive -LiteralPath $zip -DestinationPath $unpacked
    foreach ($line in Get-Content "$unpacked/SHA256SUMS.txt") {
        if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Invalid package manifest.' }
        $expected=$Matches[1]; $name=$Matches[2]
        if ((Get-FileHash (Join-Path $unpacked $name) -Algorithm SHA256).Hash -ne $expected) { throw 'Package checksum mismatch.' }
    }
    $checks += 'archive-checksums'
    $env:MEMENTOMORI_EXPORTER_DATA_DIR = Join-Path $work 'private-data'
    $port = Get-Random -Minimum 20000 -Maximum 40000
    $env:MEMENTOMORI_EXPORTER_PORT = [string]$port
    # Exercise the shipped launcher with a genuinely self-contained executable, not dotnet run.
    $process = Start-Process powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$unpacked/Start-Exporter.ps1`"",'-OfflineCheck','-NoBrowser','-Port',[string]$port) -PassThru
    $url="http://127.0.0.1:$port"
    $response=$null
    for ($i=0; $i -lt 90; $i++) {
        $process.Refresh()
        if ($process.HasExited) { throw 'Packaged launcher exited before becoming ready.' }
        try { $response=Invoke-WebRequest "$url/safe-export-ui" -UseBasicParsing -TimeoutSec 2; break } catch { Start-Sleep -Milliseconds 500 }
    }
    if ($null -eq $response -or $response.StatusCode -ne 200 -or -not $response.Content.Contains('levelLink')) { throw 'Real exporter UI was not served.' }
    $checks += 'packaged-launcher-and-real-ui'
    try { Invoke-WebRequest "$url/safe-export" -UseBasicParsing -TimeoutSec 5; throw 'Offline mode unexpectedly allowed an export.' }
    catch {
        if ($null -eq $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 503) { throw }
    }
    $checks += 'offline-mode-rejects-game-export'
    Invoke-WebRequest "$url/safe-export-ui/shutdown" -Method Post -UseBasicParsing -TimeoutSec 5 | Out-Null
    if (-not $process.WaitForExit(15000)) { throw 'Packaged launcher did not exit on shutdown.' }
    if ($process.ExitCode -ne 0) { throw 'Packaged launcher returned a failure exit code.' }
    $checks += 'clean-shutdown'
    if (Test-Path "$env:MEMENTOMORI_EXPORTER_DATA_DIR/appsettings.user.json") { throw 'Offline check unexpectedly wrote account configuration.' }
    $checks += 'no-account-configuration-created'
    New-Item -ItemType Directory "$root/artifacts/validation" -Force | Out-Null
    @{ passed=$true; platform='Windows Server 2022 CI'; checks=$checks; realGameLogin=$false } | ConvertTo-Json | Set-Content "$root/artifacts/validation/windows-smoke.json" -Encoding utf8
} finally {
    if ($null -ne $process) { $process.Refresh(); if (-not $process.HasExited) { & taskkill.exe /PID $process.Id /T /F | Out-Null }; $process.Dispose() }
    $env:MEMENTOMORI_EXPORTER_DATA_DIR=$oldData; $env:MEMENTOMORI_EXPORTER_PORT=$oldPort
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
}
