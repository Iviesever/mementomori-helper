@echo off
setlocal DisableDelayedExpansion
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0app\Start-Exporter.ps1" %*
set "EXPORTER_EXIT_CODE=%ERRORLEVEL%"
if not "%EXPORTER_EXIT_CODE%"=="0" if not defined CI pause
endlocal & exit /b %EXPORTER_EXIT_CODE%
