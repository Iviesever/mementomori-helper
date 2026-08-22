@echo off
setlocal EnableExtensions
chcp 65001 >nul

title MementoMori Account Export

set "SCRIPT=%~dp0Export-MementoMori-Account.ps1"

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
echo   MementoMori Safe Account Export v2
echo ============================================================
echo.
echo Fast mode first; full publish is used automatically if needed.
echo.

"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -SkipPublish -RestartOriginal
set "RC=%ERRORLEVEL%"

if "%RC%"=="0" goto SUCCESS

echo.
echo Fast mode failed with exit code %RC%.
echo Retrying once with a full publish...
echo.

"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -RestartOriginal
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" goto FAILED

:SUCCESS
echo.
echo ============================================================
echo EXPORT COMPLETE
echo ============================================================
echo.
pause
exit /b 0

:FAILED
echo.
echo ============================================================
echo EXPORT FAILED
echo Exit code: %RC%
echo ============================================================
echo.
pause
exit /b %RC%
