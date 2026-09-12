param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Directory')][string]$PackageDirectory,
    [Parameter(Mandatory = $true, ParameterSetName = 'Archive')][string]$Package
)
$ErrorActionPreference = 'Stop'
if ($PSCmdlet.ParameterSetName -eq 'Archive') {
    $zip = (Resolve-Path -LiteralPath $Package).Path
    $unpacked = Join-Path ([IO.Path]::GetTempPath()) ('mm archive with spaces ' + [guid]::NewGuid().ToString('N'))
    try {
        Expand-Archive -LiteralPath $zip -DestinationPath $unpacked
        $manifest = Join-Path $unpacked 'FILE-SHA256SUMS.txt'
        if (-not (Test-Path -LiteralPath $manifest)) { $manifest = Join-Path $unpacked 'SHA256SUMS.txt' }
        foreach ($line in Get-Content -LiteralPath $manifest) {
            if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Invalid package checksum manifest.' }
            $expected = $Matches[1]
            $file = [IO.Path]::GetFullPath((Join-Path $unpacked $Matches[2]))
            if (-not $file.StartsWith($unpacked + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Checksum path escapes the package.' }
            if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $expected) { throw 'Package checksum mismatch.' }
        }
        & $PSCommandPath -PackageDirectory $unpacked
        Write-Host 'PASS: archive checksums and extracted launcher in a path containing spaces.'
    } finally { if (Test-Path -LiteralPath $unpacked) { Remove-Item -LiteralPath $unpacked -Recurse -Force } }
    return
}
$packagePath = (Resolve-Path -LiteralPath $PackageDirectory).Path
if (Get-ChildItem -LiteralPath $packagePath -Recurse -File | Where-Object { $_.Name -in @('appsettings.user.json','appsettings.other.json') -or $_.Extension -in @('.keystore','.jks','.pfx') }) {
    throw 'Private files must not enter the distribution.'
}
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
# Exercise the user-visible CMD, including its relative app/ path and exit code.
$runner = Join-Path $packagePath 'START-EXPORTER.cmd'
foreach ($required in @('START-EXPORTER.cmd','app/Start-Exporter.ps1','app/MementoMori.WebUI.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $packagePath $required) -PathType Leaf)) { throw "Missing outer-launcher package file: $required" }
}
foreach ($misplaced in @('Start-Exporter.ps1','MementoMori.WebUI.exe','app/START-EXPORTER.cmd')) {
    if (Test-Path -LiteralPath (Join-Path $packagePath $misplaced)) { throw "Launcher/runtime file is in the wrong layer: $misplaced" }
}
$logs = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/build-logs'))
New-Item -ItemType Directory $logs -Force | Out-Null
$outLog = Join-Path $logs 'offline-launcher.out.log'
$errLog = Join-Path $logs 'offline-launcher.err.log'
$work = Join-Path ([IO.Path]::GetTempPath()) ('mm-offline-data-' + [Guid]::NewGuid().ToString('N'))
$oldData = $env:MEMENTOMORI_EXPORTER_DATA_DIR
$env:MEMENTOMORI_EXPORTER_DATA_DIR = $work
$process = $null
try {
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    # CMD /s /c needs an enclosing quote pair in addition to the quoted script path.
    $arguments = '/d /s /c ""' + $runner + '" -OfflineCheck -NoBrowser -Port ' + $port + '"'
    $process = Start-Process -FilePath $env:ComSpec -ArgumentList $arguments -WorkingDirectory $work -PassThru -WindowStyle Hidden -RedirectStandardOutput $outLog -RedirectStandardError $errLog
    $base = "http://127.0.0.1:$port"
    $ready = $false
    for ($i = 0; $i -lt 80; $i++) {
        if ($process.HasExited) { throw 'Packaged launcher exited early.' }
        try {
            $response = Invoke-WebRequest "$base/safe-export-ui" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200 -and $response.Headers['X-MementoMori-Validation'] -eq 'offline-no-account' -and $response.Content.Contains('levelLink')) { $ready = $true; break }
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
    if (Test-Path -LiteralPath $work) {
        if (Get-ChildItem -LiteralPath $work -Filter appsettings.user.json -Recurse -File) { throw 'Offline check unexpectedly wrote an account configuration.' }
    }
    Write-Host 'PASS: outer CMD launcher, nested app runtime, unrelated working directory, real export UI, offline refusal, graceful shutdown and no account config.'
} finally {
    if ($null -ne $process) {
        if (-not $process.HasExited) { & taskkill.exe /PID $process.Id /T /F | Out-Null }
        $process.Dispose()
    }
    $env:MEMENTOMORI_EXPORTER_DATA_DIR = $oldData
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    foreach ($log in @($outLog, $errLog)) {
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 80 | Write-Host }
    }
}
