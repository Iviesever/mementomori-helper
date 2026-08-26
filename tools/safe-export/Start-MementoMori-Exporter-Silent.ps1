& {
    $ErrorActionPreference = "Stop"

    ${MainScript} = Join-Path ${PSScriptRoot} "Export-MementoMori-Account.ps1"
    ${RepoRoot} = (Resolve-Path (Join-Path ${PSScriptRoot} "..\..")).Path
    ${WorkingRoot} = Split-Path ${RepoRoot} -Parent
    ${RuntimeDll} = Join-Path ${WorkingRoot} "safe-export-runtime\MementoMori.WebUI.dll"
    ${WebUiDir} = Join-Path ${RepoRoot} "MementoMori.WebUI"
    ${ProjectPath} = Join-Path ${WebUiDir} "MementoMori.WebUI.csproj"
    ${LauncherLog} = Join-Path ${WorkingRoot} "memento-exporter-launch.log"
    ${RestoreLog} = Join-Path ${WorkingRoot} "memento-exporter-restore.log"
    ${UiUrl} = "http://127.0.0.1:5001/safe-export-ui"

    function Show-Popup {
        param(
            [Parameter(Mandatory = $true)]
            [string]${Message},
            [int]${Seconds} = 0,
            [int]${Icon} = 64
        )

        try {
            ${Shell} = New-Object -ComObject WScript.Shell
            [void]${Shell}.Popup(
                ${Message},
                ${Seconds},
                "MementoMori Account Exporter",
                ${Icon}
            )
        }
        catch {
            # No console is expected in this launcher mode.
        }
    }

    function Show-LauncherError([string]${Message}) {
        Show-Popup -Message ${Message} -Seconds 0 -Icon 16
    }

    if (-not (Test-Path ${MainScript})) {
        Show-LauncherError "Exporter script was not found.`n`n${MainScript}"
        exit 1
    }

    # If an exporter session is already running, reopening it should be instant.
    try {
        ${Existing} = Invoke-WebRequest -Uri ${UiUrl} -Method Get -UseBasicParsing -TimeoutSec 2
        if (${Existing}.StatusCode -eq 200) {
            Start-Process ${UiUrl}
            exit 0
        }
    }
    catch {
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
            Show-Popup `
                -Message "The exporter is preparing its local runtime.`n`nThe first launch after an update can take 1-3 minutes. The browser will open automatically when ready." `
                -Seconds 4 `
                -Icon 64

            "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Runtime missing or older than source." | Out-File ${LauncherLog} -Encoding utf8

            if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
                throw ".NET SDK was not found in PATH."
            }

            # A freshly cloned repository may not have project.assets.json yet.
            # Restore explicitly before the main script uses dotnet publish --no-restore.
            Remove-Item ${RestoreLog} -Force -ErrorAction SilentlyContinue
            "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Restoring NuGet packages..." | Out-File ${LauncherLog} -Append -Encoding utf8
            & dotnet.exe restore ${ProjectPath} *> ${RestoreLog}
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet restore failed. See ${RestoreLog}"
            }

            "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Restore complete. Publishing runtime..." | Out-File ${LauncherLog} -Append -Encoding utf8
            & ${MainScript} -Interactive -RestartOriginal *>> ${LauncherLog}
        }
        else {
            Show-Popup `
                -Message "Starting MementoMori Account Exporter..." `
                -Seconds 2 `
                -Icon 64

            try {
                "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Using cached runtime..." | Out-File ${LauncherLog} -Encoding utf8
                & ${MainScript} -Interactive -SkipPublish -RestartOriginal *>> ${LauncherLog}
            }
            catch {
                "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Cached runtime failed. Rebuilding once..." | Out-File ${LauncherLog} -Append -Encoding utf8
                "Cached attempt: $($_.Exception.Message)" | Out-File ${LauncherLog} -Append -Encoding utf8

                Show-Popup `
                    -Message "The cached exporter runtime could not be used.`n`nRebuilding once; the browser will open automatically when ready." `
                    -Seconds 4 `
                    -Icon 48

                if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
                    throw ".NET SDK was not found in PATH."
                }

                Remove-Item ${RestoreLog} -Force -ErrorAction SilentlyContinue
                & dotnet.exe restore ${ProjectPath} *> ${RestoreLog}
                if ($LASTEXITCODE -ne 0) {
                    throw "dotnet restore failed. See ${RestoreLog}"
                }

                & ${MainScript} -Interactive -RestartOriginal *>> ${LauncherLog}
            }
        }
    }
    catch {
        "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] FAILED: $($_.Exception)" | Out-File ${LauncherLog} -Append -Encoding utf8
        Show-LauncherError "Account exporter failed to start.`n`nPlease send this log to ChatGPT:`n${LauncherLog}"
        exit 1
    }
}
