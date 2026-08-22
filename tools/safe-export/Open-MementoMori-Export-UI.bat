@echo off
setlocal EnableExtensions
chcp 65001 >nul

title MementoMori Selective Export UI

set "SCRIPT=%~dp0Export-MementoMori-Account.ps1"
set "MEMENTOMORI_SAFE_EXPORT_EARLY_START=1"

if not exist "%SCRIPT%" (
    echo [ERROR] Cannot find:
    echo %SCRIPT%
    echo.
    pause
    exit /b 1
)

where pwsh.exe >nul 2>&1
if %errorlevel%==0 (
    set "PS_EXE=pwsh.exe"
) else (
    set "PS_EXE=powershell.exe"
)

echo.
echo ============================================================
echo   MementoMori Selective Export UI
echo ============================================================
echo.
echo Fast mode first. The browser UI opens as soon as the local
echo server is listening, while account initialization continues
echo in the background. Export waits automatically if needed.
echo.

"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -Interactive -SkipPublish -RestartOriginal
set "RC=%ERRORLEVEL%"

if "%RC%"=="0" goto SUCCESS

echo.
echo Fast UI mode failed with exit code %RC%.
echo Retrying once with a full publish...
echo.

"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -Interactive -RestartOriginal
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" goto FAILED

:SUCCESS
echo.
echo ============================================================
echo UI SESSION COMPLETE
echo ============================================================
echo.
pause
exit /b 0

:FAILED
echo.
echo ============================================================
echo UI SESSION FAILED
echo Exit code: %RC%
echo ============================================================
echo.
pause
exit /b %RC%
