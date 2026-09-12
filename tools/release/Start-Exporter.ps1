param([switch]$OfflineCheck, [switch]$NoBrowser, [ValidateRange(1024,65535)][int]$Port = 5001)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'app/MementoMori.WebUI.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Exporter executable is missing. Extract the complete ZIP first.' }
$data = $env:MEMENTOMORI_EXPORTER_DATA_DIR
if ([string]::IsNullOrWhiteSpace($data)) { $data = Join-Path $env:LOCALAPPDATA 'MementoMoriExporter' }
if (-not $OfflineCheck -and -not (Test-Path -LiteralPath (Join-Path $data 'appsettings.user.json'))) {
    throw 'Private login configuration is missing from %LOCALAPPDATA%\MementoMoriExporter\appsettings.user.json. Keep this file private; never put it in the distribution ZIP.'
}
$oldPort = $env:MEMENTOMORI_EXPORTER_PORT
$process = $null
try {
    $env:MEMENTOMORI_EXPORTER_PORT = [string]$Port
    # Explicit values are compatible with the ASP.NET host command-line parser.
    $arguments = @('--exporter-portable=true')
    if ($OfflineCheck) { $arguments = @('--exporter-offline-check','--exporter-portable') }
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $PSScriptRoot -NoNewWindow -PassThru
    $url = "http://127.0.0.1:$Port/safe-export-ui"
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        $process.Refresh()
        if ($process.HasExited) { throw 'Exporter startup failed. Check the private login configuration and whether the port is available.' }
        try {
            $response = Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200 -and $response.Content.Contains('MementoMori')) { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw 'The local exporter did not become ready.' }
    if (-not $NoBrowser) { Start-Process $url }
    Write-Host "Exporter listening at $url. Close this window or use the page shutdown button to stop."
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'The exporter exited with an error.' }
} finally {
    $env:MEMENTOMORI_EXPORTER_PORT = $oldPort
    if ($null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.Dispose()
    }
}
