@echo off
setlocal
powershell -NoProfile -Command "$candidates = @((Join-Path '%~dp0..\..' 'logs'), (Join-Path '%~dp0..\..' 'data\logs')) | Where-Object { Test-Path -LiteralPath $_ }; if ($candidates.Count -eq 0) { Write-Host 'No JSONL log files found yet.'; exit 0 }; $latest = $candidates | ForEach-Object { Get-ChildItem -LiteralPath $_ -Filter '*.jsonl' -File -ErrorAction SilentlyContinue } | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if ($latest) { Get-Content -LiteralPath $latest.FullName -Wait -Tail 40 } else { Write-Host 'No JSONL log files found yet.' }"
pause
