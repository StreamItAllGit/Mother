@echo off
setlocal
dotnet test "%~dp0..\Mother.slnx" -c Release %*
exit /b %ERRORLEVEL%
