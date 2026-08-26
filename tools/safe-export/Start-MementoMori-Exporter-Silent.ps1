& {
    $ErrorActionPreference = "Stop"

    ${MainScript} = Join-Path ${PSScriptRoot} "Export-MementoMori-Account.ps1"
    ${RepoRoot} = (Resolve-Path (Join-Path ${PSScriptRoot} "..\..")).Path
    ${WorkingRoot} = Split-Path ${RepoRoot} -Parent
    ${RuntimeDll} = Join-Path ${WorkingRoot} "safe-export-runtime\MementoMori.WebUI.dll"
    ${WebUiDir} = Join-Path ${RepoRoot} "MementoMori.WebUI"
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
        ${NeedRebuild} = -not (Test-Path ${RuntimeDll})

        if (-not ${NeedRebuild}) {
            ${RuntimeTime} = (Get-Item ${RuntimeDll}).LastWriteTimeUtc
            ${NewestSource} = Get-ChildItem ${WebUiDir} -File -Recurse -ErrorAction Stop |
                Where-Object { $_.Extension -in @(".cs", ".csproj") } |
                Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -First 1

            if ($null -ne ${NewestSource} -and ${NewestSource}.LastWriteTimeUtc -gt ${RuntimeTime}) {
                ${NeedRebuild} = $true
            }
        }

        if (${NeedRebuild}) {
            "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Runtime missing or older than source. Publishing once..." | Out-File ${LauncherLog} -Encoding utf8
            & ${MainScript} -Interactive -RestartOriginal *>> ${LauncherLog}
        }
        else {
            try {
                "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Using cached runtime..." | Out-File ${LauncherLog} -Encoding utf8
                & ${MainScript} -Interactive -SkipPublish -RestartOriginal *>> ${LauncherLog}
            }
            catch {
                "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Cached runtime failed. Rebuilding once..." | Out-File ${LauncherLog} -Append -Encoding utf8
                "Cached attempt: $($_.Exception.Message)" | Out-File ${LauncherLog} -Append -Encoding utf8
                & ${MainScript} -Interactive -RestartOriginal *>> ${LauncherLog}
            }
        }
    }
    catch {
        "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] FAILED: $($_.Exception)" | Out-File ${LauncherLog} -Append -Encoding utf8
        Show-LauncherError "账号导出器启动失败。`n`n请把这个日志发给 ChatGPT：`n${LauncherLog}"
        exit 1
    }
}
