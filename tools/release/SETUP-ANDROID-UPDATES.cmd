@echo off
setlocal DisableDelayedExpansion
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Initialize-AndroidSigning.ps1" -CreateNew -UploadToGitHub
set "SETUP_EXIT=%ERRORLEVEL%"
pause
endlocal & exit /b %SETUP_EXIT%
