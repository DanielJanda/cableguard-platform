#Requires -Version 5.1
<#
.SYNOPSIS
  Start Event Core bound to 0.0.0.0:8000 for internal LAN access.
#>
$ErrorActionPreference = "Stop"

$Port = 8000
$Root = Split-Path -Parent $PSScriptRoot
$RuntimeDir = Join-Path $Root "runtime\event-core"
$PidFile = Join-Path $RuntimeDir "event-core.pid"
$OutLog = Join-Path $RuntimeDir "event-core.out.log"
$ErrLog = Join-Path $RuntimeDir "event-core.err.log"
$Py = Join-Path $Root ".venv\Scripts\python.exe"
$Uvicorn = Join-Path $Root ".venv\Scripts\uvicorn.exe"

Set-Location $Root

if (-not (Test-Path $Py)) {
    Write-Host "Create .venv first: py -3.10 -m venv .venv" -ForegroundColor Red
    exit 1
}

$InternalCors = "http://10.6.1.40:8080,http://localhost:8080,http://127.0.0.1:8080"

if (Test-Path $PidFile) {
    $oldPid = Get-Content $PidFile -ErrorAction SilentlyContinue
    if ($oldPid -and (Get-Process -Id $oldPid -ErrorAction SilentlyContinue)) {
        Write-Host "Event Core already running (PID $oldPid)." -ForegroundColor Yellow
        Write-Host "http://10.6.1.40:${Port}/api/v1/health" -ForegroundColor Cyan
        exit 0
    }
    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}

$listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) {
    if ($listener.LocalAddress -eq "0.0.0.0") {
        Write-Host "Event Core already listening on 0.0.0.0:${Port} (PID $($listener.OwningProcess))." -ForegroundColor Yellow
        Write-Host "http://10.6.1.40:${Port}/api/v1/health" -ForegroundColor Cyan
        exit 0
    }
    Write-Host "Port $Port in use on $($listener.LocalAddress) (PID $($listener.OwningProcess))." -ForegroundColor Red
    Write-Host "Stop the process or restart with scripts/start_internal_event_core.ps1 after freeing the port." -ForegroundColor Yellow
    exit 1
}

New-Item -ItemType Directory -Force -Path $RuntimeDir | Out-Null

$env:PYTHONPATH = Join-Path $Root "backend"
$env:CABLEGUARD_HOST = "0.0.0.0"
if (-not $env:CABLEGUARD_CORS_ORIGINS) {
    $env:CABLEGUARD_CORS_ORIGINS = $InternalCors
}

$proc = Start-Process -FilePath $Uvicorn `
    -ArgumentList @("app.main:app", "--app-dir", "backend", "--host", "0.0.0.0", "--port", "$Port") `
    -WorkingDirectory $Root `
    -PassThru `
    -WindowStyle Hidden `
    -RedirectStandardOutput $OutLog `
    -RedirectStandardError $ErrLog

$proc.Id | Set-Content $PidFile
Start-Sleep -Seconds 3

if ($proc.HasExited) {
    Write-Host "Event Core exited immediately. See $OutLog and $ErrLog" -ForegroundColor Red
    Remove-Item $PidFile -ErrorAction SilentlyContinue
    exit 1
}

try {
    $health = Invoke-RestMethod -Uri "http://127.0.0.1:${Port}/api/v1/health" -TimeoutSec 10
    Write-Host "Event Core health: $($health.status)" -ForegroundColor Green
} catch {
    Write-Host "Event Core started (PID $($proc.Id)) but health probe failed. Check logs." -ForegroundColor Yellow
}

Write-Host "Event Core (internal LAN) started (PID $($proc.Id))." -ForegroundColor Green
Write-Host "http://10.6.1.40:${Port}" -ForegroundColor Cyan
