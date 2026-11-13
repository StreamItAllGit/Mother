@echo off
setlocal
set "PORT=%~1"
if "%PORT%"=="" set "PORT=5100"
curl -fsS -X POST "http://127.0.0.1:%PORT%/api/v1/suite/doctor/backup-dbs"
exit /b %ERRORLEVEL%
