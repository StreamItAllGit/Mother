@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\package_installer.ps1" %*
exit /b %ERRORLEVEL%
