param(
    [string]$ConfigPath,
    [ValidateRange(1024, 65535)][int]$Port = 5001,
    [switch]$OfflineCheck,
    [switch]$NoBrowser
)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'MementoMori.WebUI.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Extract the complete Windows ZIP before starting.' }
$privateRoot = Join-Path $env:LOCALAPPDATA 'MementoMoriExporter'
$sessionRoot = Join-Path $privateRoot ('session-' + [Guid]::NewGuid().ToString('N'))
$process = $null
New-Item -ItemType Directory -Path $sessionRoot -Force | Out-Null
try {
    if (-not $OfflineCheck) {
        if ([string]::IsNullOrWhiteSpace($ConfigPath)) { $ConfigPath = Join-Path $privateRoot 'appsettings.user.json' }
        if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { throw 'A private desktop login configuration is required. Do not send it to chat or GitHub.' }
        $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
        if ($null -eq $config.AuthOption -or $config.GameConfig.AutoJob.DisableAll -ne $true) {
            throw 'Private configuration must contain AuthOption and GameConfig.AutoJob.DisableAll=true.'
        }
        # Do not import arbitrary server binding, reporting endpoints or automation settings.
        $safe = @{ AuthOption = $config.AuthOption; GameConfig = @{ AutoJob = @{ DisableAll = $true }; ReportBattleLog = $false; RecordBattleLog = $false } }
        $json = $safe | ConvertTo-Json -Depth 100
        [IO.File]::WriteAllText((Join-Path $sessionRoot 'appsettings.user.json'), $json, [Text.UTF8Encoding]::new($false))
        $json = $null; $safe = $null; $config = $null
    }
    # Refuse an occupied port rather than opening another process's page.
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
    try { $probe.Start() } finally { $probe.Stop() }
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $exe
    $info.WorkingDirectory = $sessionRoot
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    # Core error output can contain remote request bodies. Drain it; never persist or print it.
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    if ($OfflineCheck) {
        $info.Arguments = '--exporter-offline-check'
        $info.EnvironmentVariables['MEMENTOMORI_OFFLINE_CHECK_PORT'] = [string]$Port
    } else {
        $info.Arguments = '--contentRoot "' + $PSScriptRoot + '" --urls http://127.0.0.1:' + $Port
        $info.EnvironmentVariables['MEMENTOMORI_SAFE_EXPORT_EARLY_START'] = '1'
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    [void]$process.Start()
    $process.BeginOutputReadLine()
    $process.BeginErrorReadLine()
    $url = "http://127.0.0.1:$Port/safe-export-ui"
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        if ($process.HasExited) { throw 'Exporter exited before the UI was ready.' }
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw 'Local exporter UI did not start.' }
    if (-not $NoBrowser) { Start-Process $url }
    Write-Host 'Local exporter UI is ready. Close it with the shutdown button; this console owns the process.'
    if ($OfflineCheck) { Write-Host 'OFFLINE CHECK ONLY: no account login or export is performed.' }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'Exporter process failed.' }
} finally {
    if ($null -ne $process) {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
    # Contains only this launch's private configuration and caches, never the original config.
    if (Test-Path -LiteralPath $sessionRoot) { Remove-Item -LiteralPath $sessionRoot -Recurse -Force }
}
