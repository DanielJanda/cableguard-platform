#Requires -Version 5.1
<#
.SYNOPSIS
  Print safe local startup checklist for CableGuard demo (no secrets).

.DESCRIPTION
  Does NOT start long-running processes automatically.
  Opens nothing with credentials. Use printed commands in separate terminals.
#>
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$MonitorRoot = Join-Path (Split-Path -Parent $Root) "cableguard-monitor"

Write-Host "=== CableGuard local integration demo ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "1) Event Core (terminal A):" -ForegroundColor Yellow
Write-Host "   cd `"$Root\backend`""
Write-Host "   `$env:PYTHONPATH = `"$Root\backend`""
Write-Host "   ..\.venv\Scripts\alembic.exe upgrade head"
Write-Host "   cd `"$Root`""
Write-Host "   .\.venv\Scripts\uvicorn.exe app.main:app --app-dir backend --host 127.0.0.1 --port 8000"
Write-Host ""
Write-Host "2) MediaMTX (terminal B, optional video):" -ForegroundColor Yellow
Write-Host "   cd `"$Root`""
Write-Host "   .\scripts\start_mediamtx.ps1"
Write-Host ""
Write-Host "3) Monitor (terminal C):" -ForegroundColor Yellow
Write-Host "   cd `"$MonitorRoot`""
Write-Host "   npm run dev"
Write-Host "   -> http://127.0.0.1:5173/kiosk/zahradky/horni-stanice"
Write-Host ""
Write-Host "4) Simulator (terminal D, after Event Core is up):" -ForegroundColor Yellow
Write-Host "   cd `"$Root`""
Write-Host "   `$env:PYTHONPATH = `"$Root\backend`""
Write-Host "   python scripts\simulate_system.py --scenario demo"
Write-Host ""
Write-Host "Docs: docs/local-monitor-integration.md" -ForegroundColor Green

# Preflight (no secret output)
if (-not (Test-Path (Join-Path $Root ".venv\Scripts\uvicorn.exe"))) {
  Write-Host "WARN: Python venv not found at .venv" -ForegroundColor Red
}
if (-not (Test-Path (Join-Path $Root ".env"))) {
  Write-Host "WARN: platform .env missing (API keys required for simulator)" -ForegroundColor Red
}
if (-not (Test-Path (Join-Path $MonitorRoot ".env.local"))) {
  Write-Host "WARN: monitor .env.local missing (real mode + kiosk key for BFF)" -ForegroundColor Red
}
