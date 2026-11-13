@echo off
setlocal
powershell -NoProfile -Command "try { Invoke-RestMethod -Uri 'http://127.0.0.1:5100/api/v1/suite/health' | ConvertTo-Json -Depth 4 } catch { Write-Error $_; exit 1 }"
