param(
    [string]${ReleaseDir},
    [int]${Port} = 5001,
    [switch]${SkipPublish},
    [switch]${KeepHistory},
    [switch]${RestartOriginal},
    [switch]${Interactive}
)

& {
    $ErrorActionPreference = "Stop"

    ${SourceDir} = (Resolve-Path (Join-Path ${PSScriptRoot} "..\..")).Path
    ${WorkingRoot} = Split-Path ${SourceDir} -Parent

    if ([string]::IsNullOrWhiteSpace(${ReleaseDir})) {
        ${ReleaseDir} = Join-Path ${WorkingRoot} "MementoMoriHelper-v1.14.2\app\publish-win-x64"
    }

    ${ProjectPath} = Join-Path ${SourceDir} "MementoMori.WebUI\MementoMori.WebUI.csproj"
    ${ReleaseExe} = Join-Path ${ReleaseDir} "MementoMori.WebUI.exe"
    ${ReleaseConfig} = Join-Path ${ReleaseDir} "appsettings.user.json"
    ${ReleaseLauncher} = Join-Path ${ReleaseDir} "Start-MementoMori-ReadOnly.ps1"

    ${RuntimeDir} = Join-Path ${WorkingRoot} "safe-export-runtime"
    ${RuntimeDll} = Join-Path ${RuntimeDir} "MementoMori.WebUI.dll"
    ${RuntimeConfig} = Join-Path ${RuntimeDir} "appsettings.user.json"

    ${PublishLog} = Join-Path ${WorkingRoot} "memento-safe-export-publish.log"
    ${ServerOutLog} = Join-Path ${WorkingRoot} "memento-safe-export-server.out.log"
    ${ServerErrLog} = Join-Path ${WorkingRoot} "memento-safe-export-server.err.log"
    ${ExportPath} = Join-Path ${WorkingRoot} "mementomori-account.json"

    ${RootUrl} = "http://127.0.0.1:${Port}/"
    ${ExportUrl} = "http://127.0.0.1:${Port}/safe-export"
    ${UiUrl} = "http://127.0.0.1:${Port}/safe-export-ui"

    ${ServerProcess} = $null
    ${OriginalWasRunning} = $false
    ${OldAspNetUrls} = ${env:ASPNETCORE_URLS}

    try {
        Write-Host ""
        Write-Host "========== 1. SOURCE CHECK ==========" -ForegroundColor Cyan

        if (-not (Test-Path ${ProjectPath})) {
            throw "Cannot find project: ${ProjectPath}"
        }

        ${SafeExportPath} = Join-Path ${SourceDir} "MementoMori.WebUI\SafeExport.cs"
        if (-not (Test-Path ${SafeExportPath})) {
            throw "SafeExport.cs is missing."
        }

        Set-Location ${SourceDir}
        ${Head} = (git rev-parse HEAD).Trim()
        ${Branch} = (git branch --show-current).Trim()

        Write-Host "Branch = ${Branch}"
        Write-Host "HEAD   = ${Head}"
        Write-Host "Mode   = $(if (${Interactive}) { 'Interactive UI' } else { 'Automatic full JSON' })"

        Write-Host ""
        Write-Host "========== 2. CONFIG SAFETY CHECK ==========" -ForegroundColor Cyan

        if (-not (Test-Path ${ReleaseConfig})) {
            throw "Cannot find local login config: ${ReleaseConfig}"
        }

        ${Config} = Get-Content ${ReleaseConfig} -Raw | ConvertFrom-Json

        if ($null -eq ${Config}.GameConfig -or $null -eq ${Config}.GameConfig.AutoJob) {
            throw "GameConfig.AutoJob is missing from appsettings.user.json."
        }

        if (${Config}.GameConfig.AutoJob.DisableAll -ne $true) {
            throw "Safety check failed: GameConfig.AutoJob.DisableAll must be true."
        }

        ${ExplicitlyEnabledJobs} = @(
            ${Config}.GameConfig.AutoJob.PSObject.Properties |
            Where-Object {
                $_.Name -ne "DisableAll" -and $_.Value -eq $true
            }
        )

        if (${ExplicitlyEnabledJobs}.Count -gt 0) {
            Write-Host "Explicitly enabled AutoJob entries:" -ForegroundColor Red
            ${ExplicitlyEnabledJobs} | Select-Object Name, Value | Format-Table
            throw "Stop: one or more AutoJob entries are explicitly true."
        }

        Write-Host "DisableAll = True" -ForegroundColor Green
        Write-Host "Credentials will not be printed." -ForegroundColor Green

        Write-Host ""
        Write-Host "========== 3. PUBLISH ==========" -ForegroundColor Cyan

        if (${SkipPublish}) {
            if (-not (Test-Path ${RuntimeDll})) {
                throw "-SkipPublish was specified, but runtime does not exist: ${RuntimeDll}"
            }
            Write-Host "Using existing runtime." -ForegroundColor Green
        }
        else {
            if (Test-Path ${RuntimeDir}) {
                Remove-Item ${RuntimeDir} -Recurse -Force
            }

            New-Item -ItemType Directory -Path ${RuntimeDir} -Force | Out-Null
            Remove-Item ${PublishLog} -Force -ErrorAction SilentlyContinue

            & dotnet publish `
                ${ProjectPath} `
                --configuration Release `
                --no-restore `
                --output ${RuntimeDir} `
                *> ${PublishLog}

            if ($LASTEXITCODE -ne 0) {
                if (Test-Path ${PublishLog}) {
                    Get-Content ${PublishLog} -Tail 100
                }
                throw "dotnet publish failed."
            }

            if (-not (Test-Path ${RuntimeDll})) {
                throw "Publish completed, but MementoMori.WebUI.dll is missing."
            }

            Write-Host "Publish: SUCCESS" -ForegroundColor Green
        }

        Write-Host ""
        Write-Host "========== 4. PREPARE RUNTIME ==========" -ForegroundColor Cyan

        Copy-Item ${ReleaseConfig} ${RuntimeConfig} -Force
        ${RuntimeConfigCheck} = Get-Content ${RuntimeConfig} -Raw | ConvertFrom-Json

        if (${RuntimeConfigCheck}.GameConfig.AutoJob.DisableAll -ne $true) {
            Remove-Item ${RuntimeConfig} -Force -ErrorAction SilentlyContinue
            throw "Runtime config DisableAll is not true."
        }

        Write-Host "Temporary login config copied without printing it." -ForegroundColor Green

        Write-Host ""
        Write-Host "========== 5. STOP ORIGINAL INSTANCE ==========" -ForegroundColor Cyan

        ${OriginalProcesses} = @(
            Get-Process -Name "MementoMori.WebUI" -ErrorAction SilentlyContinue |
            Where-Object {
                try { $_.Path -eq ${ReleaseExe} } catch { $false }
            }
        )

        if (${OriginalProcesses}.Count -gt 0) {
            ${OriginalWasRunning} = $true
            foreach (${Process} in ${OriginalProcesses}) {
                Write-Host "Stopping original PID $(${Process}.Id)..." -ForegroundColor Yellow
                Stop-Process -Id ${Process}.Id -Force
            }
            Start-Sleep -Seconds 2
        }
        else {
            Write-Host "Original Helper was not running."
        }

        Write-Host ""
        Write-Host "========== 6. START LOCAL EXPORT SERVER ==========" -ForegroundColor Cyan

        Remove-Item ${ServerOutLog} -Force -ErrorAction SilentlyContinue
        Remove-Item ${ServerErrLog} -Force -ErrorAction SilentlyContinue

        ${env:ASPNETCORE_URLS} = "http://127.0.0.1:${Port}"

        ${ServerProcess} = Start-Process `
            -FilePath "dotnet" `
            -ArgumentList @("`"${RuntimeDll}`"") `
            -WorkingDirectory ${RuntimeDir} `
            -RedirectStandardOutput ${ServerOutLog} `
            -RedirectStandardError ${ServerErrLog} `
            -PassThru

        ${env:ASPNETCORE_URLS} = ${OldAspNetUrls}

        Write-Host "PID: $(${ServerProcess}.Id)"
        Write-Host "Binding: 127.0.0.1:${Port} only" -ForegroundColor Green

        ${Ready} = $false
        for (${I} = 1; ${I} -le 120; ${I}++) {
            ${ServerProcess}.Refresh()
            if (${ServerProcess}.HasExited) { break }

            try {
                ${Probe} = Invoke-WebRequest -Uri ${RootUrl} -Method Get -TimeoutSec 2
                if (${Probe}.StatusCode -ge 200 -and ${Probe}.StatusCode -lt 500) {
                    ${Ready} = $true
                    break
                }
            }
            catch {
            }

            Start-Sleep -Seconds 1
        }

        if (-not ${Ready}) {
            if (Test-Path ${ServerOutLog}) {
                Write-Host "--- stdout ---" -ForegroundColor Red
                Get-Content ${ServerOutLog} -Tail 100
            }
            if (Test-Path ${ServerErrLog}) {
                Write-Host "--- stderr ---" -ForegroundColor Red
                Get-Content ${ServerErrLog} -Tail 100
            }
            throw "Safe export server did not become ready."
        }

        Write-Host "Server ready." -ForegroundColor Green

        if (${Interactive}) {
            Write-Host ""
            Write-Host "========== 7. OPEN EXPORT UI ==========" -ForegroundColor Cyan

            try {
                ${UiProbe} = Invoke-WebRequest -Uri ${UiUrl} -Method Get -TimeoutSec 5
                if (${UiProbe}.StatusCode -ne 200) {
                    throw "UI endpoint returned HTTP $(${UiProbe}.StatusCode)."
                }
            }
            catch {
                throw "Interactive UI is unavailable in this runtime. Run again without -SkipPublish. $($_.Exception.Message)"
            }

            Write-Host "Opening:" -ForegroundColor Green
            Write-Host ${UiUrl} -ForegroundColor Yellow
            Write-Host ""
            Write-Host "Choose sections in the browser and click Export." -ForegroundColor Green
            Write-Host "The default UI option closes the temporary server after download." -ForegroundColor Green
            Write-Host "If you close the browser first, return here and press Ctrl+C to stop." -ForegroundColor Yellow

            Start-Process ${UiUrl}

            while ($true) {
                ${ServerProcess}.Refresh()
                if (${ServerProcess}.HasExited) {
                    break
                }
                Start-Sleep -Seconds 1
            }

            Write-Host ""
            Write-Host "UI export server has stopped." -ForegroundColor Green
            return
        }

        Write-Host ""
        Write-Host "========== 7. EXPORT FULL JSON ==========" -ForegroundColor Cyan

        ${Response} = Invoke-WebRequest -Uri ${ExportUrl} -Method Get -TimeoutSec 180

        if (${Response}.StatusCode -ne 200) {
            throw "/safe-export returned HTTP $(${Response}.StatusCode)."
        }

        [System.IO.File]::WriteAllText(
            ${ExportPath},
            ${Response}.Content,
            [System.Text.UTF8Encoding]::new($false)
        )

        Write-Host ""
        Write-Host "========== 8. VALIDATE ==========" -ForegroundColor Cyan

        ${RawJson} = Get-Content ${ExportPath} -Raw
        ${Json} = ${RawJson} | ConvertFrom-Json

        if (${Json}.schema -ne "mementomori-safe-account-export-v3.1") {
            throw "Unexpected schema: $(${Json}.schema)"
        }

        ${ForbiddenPropertyPattern} = '(?i)"(?:clientkey|password|authtoken|token|session|credential|guid|playerid|userid)"\s*:'
        if (${RawJson} -match ${ForbiddenPropertyPattern}) {
            throw "Sensitive property name detected in export JSON."
        }

        Write-Host "Schema: OK" -ForegroundColor Green
        Write-Host "Sensitive-field scan: PASS" -ForegroundColor Green

        if (${KeepHistory}) {
            ${HistoryDir} = Join-Path ${WorkingRoot} "exports"
            New-Item -ItemType Directory -Path ${HistoryDir} -Force | Out-Null
            ${Stamp} = Get-Date -Format "yyyyMMdd-HHmmss"
            ${HistoryPath} = Join-Path ${HistoryDir} "mementomori-account-${Stamp}.json"
            Copy-Item ${ExportPath} ${HistoryPath} -Force
            Write-Host "History: ${HistoryPath}" -ForegroundColor Green
        }

        Write-Host ""
        Write-Host "========== SUMMARY ==========" -ForegroundColor Cyan
        Write-Host "Player      : $(${Json}.player.name)"
        Write-Host "Rank        : $(${Json}.player.rank)"
        Write-Host "Quest       : $(${Json}.progress.bossClearQuestMemo)"
        Write-Host "Characters  : $(@(${Json}.characters).Count)"
        Write-Host "Equipment   : $(@(${Json}.equipment).Count) character loadouts"
        Write-Host "Decks       : $(@(${Json}.decks).Count)"
        Write-Host "Items       : $(@(${Json}.items).Count)"
        Write-Host "Gacha cases : $(@(${Json}.gacha.cases).Count)"
        Write-Host ""
        Write-Host "EXPORT SUCCESS: ${ExportPath}" -ForegroundColor Green
    }
    finally {
        Write-Host ""
        Write-Host "========== CLEANUP ==========" -ForegroundColor Cyan

        ${env:ASPNETCORE_URLS} = ${OldAspNetUrls}

        if ($null -ne ${ServerProcess} -and -not ${ServerProcess}.HasExited) {
            Stop-Process -Id ${ServerProcess}.Id -Force -ErrorAction SilentlyContinue
        }

        if (Test-Path ${RuntimeConfig}) {
            Remove-Item ${RuntimeConfig} -Force
            Write-Host "Temporary runtime credential config removed." -ForegroundColor Green
        }

        if (${RestartOriginal} -and ${OriginalWasRunning} -and (Test-Path ${ReleaseLauncher})) {
            ${PowerShellExe} = if (Get-Command pwsh.exe -ErrorAction SilentlyContinue) { "pwsh.exe" } else { "powershell.exe" }
            Start-Process `
                -FilePath ${PowerShellExe} `
                -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"${ReleaseLauncher}`"") `
                -WorkingDirectory ${ReleaseDir}
            Write-Host "Original Helper restart requested." -ForegroundColor Green
        }
    }
}
