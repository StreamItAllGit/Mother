@echo off
setlocal
dotnet build "%~dp0..\Mother.slnx" -c Release /warnaserror %*
exit /b %ERRORLEVEL%
