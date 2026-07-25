@echo off
REM build.bat - Double-click launcher for build.ps1
REM Runs PowerShell with execution policy bypassed for this process only.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
echo.
pause
