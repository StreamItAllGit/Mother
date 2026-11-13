@echo off
setlocal
powershell -NoProfile -Command "Invoke-RestMethod -Uri 'http://127.0.0.1:5100/api/v1/suite/apps' | ConvertTo-Json -Depth 5"
pause
