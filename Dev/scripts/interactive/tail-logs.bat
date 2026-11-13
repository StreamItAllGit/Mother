@echo off
setlocal
powershell -NoProfile -Command "$logDir = Join-Path '%~dp0..\..' 'data\logs'; if (Test-Path '%~dp0..\..\logs') { $logDir = '%~dp0..\..\logs' }; $latest = Get-ChildItem $logDir -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if ($latest) { Get-Content $latest.FullName -Wait -Tail 40 } else { Write-Host 'No log files found yet.' }"
pause
