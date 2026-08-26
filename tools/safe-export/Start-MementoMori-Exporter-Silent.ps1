& {
    $ErrorActionPreference = "Stop"

    ${MainScript} = Join-Path ${PSScriptRoot} "Export-MementoMori-Account.ps1"
    ${RepoRoot} = (Resolve-Path (Join-Path ${PSScriptRoot} "..\..")).Path
    ${WorkingRoot} = Split-Path ${RepoRoot} -Parent
    ${LauncherLog} = Join-Path ${WorkingRoot} "memento-exporter-launch.log"

    function Show-LauncherError([string]${Message}) {
        try {
            Add-Type -AssemblyName PresentationFramework -ErrorAction Stop
            [System.Windows.MessageBox]::Show(
                ${Message},
                "MementoMori 账号导出器",
                [System.Windows.MessageBoxButton]::OK,
                [System.Windows.MessageBoxImage]::Error
            ) | Out-Null
        }
        catch {
            # Last-resort fallback: no console is expected in this launcher mode.
        }
    }

    if (-not (Test-Path ${MainScript})) {
        Show-LauncherError "找不到导出器脚本：`n${MainScript}"
        exit 1
    }

    Remove-Item ${LauncherLog} -Force -ErrorAction SilentlyContinue

    try {
        try {
            "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Trying cached runtime..." | Out-File ${LauncherLog} -Encoding utf8
            & ${MainScript} -Interactive -SkipPublish -RestartOriginal *>> ${LauncherLog}
        }
        catch {
            "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Cached runtime unavailable or stale. Rebuilding once..." | Out-File ${LauncherLog} -Append -Encoding utf8
            "First attempt: $($_.Exception.Message)" | Out-File ${LauncherLog} -Append -Encoding utf8
            & ${MainScript} -Interactive -RestartOriginal *>> ${LauncherLog}
        }
    }
    catch {
        "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] FAILED: $($_.Exception)" | Out-File ${LauncherLog} -Append -Encoding utf8
        Show-LauncherError "账号导出器启动失败。`n`n请把这个日志发给 ChatGPT：`n${LauncherLog}"
        exit 1
    }
}
