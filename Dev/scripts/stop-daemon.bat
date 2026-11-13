@echo off
setlocal
powershell -NoProfile -Command "try { Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5100/api/v1/suite/jobs/stop-all' | ConvertTo-Json -Compress } catch { Write-Host 'Mother daemon offline or unreachable.' }"
