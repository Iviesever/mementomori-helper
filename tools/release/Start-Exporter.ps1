param(
    [string]$ConfigPath,
    [ValidateRange(1024, 65535)][int]$Port = 5001,
    [switch]$OfflineCheck,
    [switch]$NoBrowser
)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'MementoMori.WebUI.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { $exe = Join-Path $PSScriptRoot 'app/MementoMori.WebUI.exe' }
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Extract the complete Windows ZIP before starting.' }
$privateRoot = $env:MEMENTOMORI_EXPORTER_DATA_DIR
if ([string]::IsNullOrWhiteSpace($privateRoot)) { $privateRoot = Join-Path $env:LOCALAPPDATA 'MementoMoriExporter' }
$sessionRoot = Join-Path $privateRoot ('session-' + [Guid]::NewGuid().ToString('N'))
$process = $null
$started = $false
$stdoutTask = $null; $stderrTask = $null
New-Item -ItemType Directory -Path $sessionRoot -Force | Out-Null
try {
    if (-not $OfflineCheck) {
        if ([string]::IsNullOrWhiteSpace($ConfigPath)) { $ConfigPath = Join-Path $privateRoot 'appsettings.user.json' }
        if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { throw 'A private desktop login configuration is required. Do not send it to chat or GitHub.' }
        if ((Get-Item -LiteralPath $ConfigPath).Length -gt 524288) { throw 'Private login configuration exceeds 512 KB.' }
        $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
        if ($null -eq $config.AuthOption) { throw 'Private configuration must contain AuthOption.' }
        # The portable host additionally enforces the credential whitelist and disables all jobs.
        $safe = @{ AuthOption = $config.AuthOption; GameConfig = @{ AutoJob = @{ DisableAll = $true }; ReportBattleLog = $false; RecordBattleLog = $false } }
        $json = $safe | ConvertTo-Json -Depth 100
        [IO.File]::WriteAllText((Join-Path $sessionRoot 'appsettings.user.json'), $json, [Text.UTF8Encoding]::new($false))
        $json = $null; $safe = $null; $config = $null
    }
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
    try { $probe.Start() } finally { $probe.Stop() }
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $exe
    $info.WorkingDirectory = $sessionRoot
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.EnvironmentVariables['MEMENTOMORI_EXPORTER_DATA_DIR'] = $sessionRoot
    $info.EnvironmentVariables['MEMENTOMORI_EXPORTER_PORT'] = [string]$Port
    if ($OfflineCheck) {
        $info.Arguments = '--exporter-offline-check'
        $info.EnvironmentVariables['MEMENTOMORI_OFFLINE_CHECK_PORT'] = [string]$Port
    } else {
        $info.Arguments = '--exporter-portable=true --contentRoot "' + (Split-Path $exe -Parent) + '"'
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $started = $process.Start()
    if (-not $started) { throw 'Could not start the exporter process.' }
    if ($OfflineCheck) {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
    } else {
        $process.BeginOutputReadLine()
        $process.BeginErrorReadLine()
    }
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
        if ($started -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        if ($OfflineCheck -and $started) {
            foreach ($task in @($stdoutTask, $stderrTask)) {
                if ($null -ne $task) {
                    $text = $task.GetAwaiter().GetResult()
                    if ($text.Length -gt 8192) { $text = $text.Substring($text.Length - 8192) }
                    Write-Host $text
                }
            }
        }
        $process.Dispose()
    }
    if (Test-Path -LiteralPath $sessionRoot) { Remove-Item -LiteralPath $sessionRoot -Recurse -Force }
}
