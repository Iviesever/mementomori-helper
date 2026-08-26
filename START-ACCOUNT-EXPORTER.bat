@echo off
setlocal EnableExtensions
chcp 65001 >nul

set "LAUNCHER=%~dp0tools\safe-export\Start-MementoMori-Exporter-Silent.ps1"

if not exist "%LAUNCHER%" (
    echo [ERROR] Cannot find exporter launcher:
    echo %LAUNCHER%
    echo.
    pause
    exit /b 1
)

rem Start the exporter in a hidden PowerShell window. The browser UI will open
rem automatically when the local server is ready. This batch window exits at once.
start "" powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%LAUNCHER%"
exit /b 0
