#Requires -Version 5.1
<#
.SYNOPSIS
  Start or verify CableGuard internal LAN stack on 10.6.1.40.

.DESCRIPTION
  1. Event Core (0.0.0.0:8000)
  2. MediaMTX (WHEP :8889, WebRTC UDP :8189)
  3. cableguard-monitor (0.0.0.0:8080)
  Skips duplicate processes. Does not print secrets.
#>
$ErrorActionPreference = "Stop"

$LanHost = "10.6.1.40"
$Stream = "zahradky-horni-stanice"
$Root = Split-Path -Parent $PSScriptRoot
$MonitorRoot = Join-Path (Split-Path -Parent $Root) "cableguard-monitor"

Write-Host "=== CableGuard internal LAN runtime ===" -ForegroundColor Cyan
Write-Host ""

# Firewall (Domain + Private only; no Public profile)
& (Join-Path $PSScriptRoot "ensure_internal_firewall.ps1")
Write-Host ""

# MediaMTX LAN browser origins in gitignored config
& (Join-Path $PSScriptRoot "patch_mediamtx_internal_lan.ps1")

Write-Host "1) Event Core..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot "start_internal_event_core.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "2) MediaMTX..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot "start_mediamtx.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "3) Monitor..." -ForegroundColor Yellow
$monitorScript = Join-Path $MonitorRoot "scripts\start_internal_monitor.ps1"
if (-not (Test-Path $monitorScript)) {
    Write-Host "Missing monitor script: $monitorScript" -ForegroundColor Red
    exit 1
}
& $monitorScript
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "=== Health / status ===" -ForegroundColor Cyan

try {
    $health = Invoke-RestMethod -Uri "http://${LanHost}:8000/api/v1/health" -TimeoutSec 8
    Write-Host "Event Core: $($health.status)" -ForegroundColor Green
} catch {
    Write-Host "Event Core: UNREACHABLE ($($_.Exception.Message))" -ForegroundColor Red
}

& (Join-Path $PSScriptRoot "status_mediamtx.ps1")

try {
    $mon = Invoke-WebRequest -Uri "http://${LanHost}:8080/" -UseBasicParsing -TimeoutSec 8
    Write-Host "Monitor HTTP: $($mon.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "Monitor HTTP: UNREACHABLE ($($_.Exception.Message))" -ForegroundColor Red
}

$origin = "http://${LanHost}:8080"
try {
    $opts = Invoke-WebRequest -Uri "http://${LanHost}:8889/${Stream}/whep" -Method Options -Headers @{ Origin = $origin } -UseBasicParsing -TimeoutSec 8
    Write-Host "WHEP OPTIONS (Origin $origin): HTTP $($opts.StatusCode)" -ForegroundColor Green
} catch {
    if ($_.Exception.Response) {
        Write-Host "WHEP OPTIONS: HTTP $([int]$_.Exception.Response.StatusCode)" -ForegroundColor Yellow
    } else {
        Write-Host "WHEP OPTIONS: failed ($($_.Exception.Message))" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== URLs (internal LAN) ===" -ForegroundColor Cyan
Write-Host "CableGuard dashboard:"
Write-Host "http://${LanHost}:8080/dashboard"
Write-Host ""
Write-Host "Historie:"
Write-Host "http://${LanHost}:8080/events"
Write-Host ""
Write-Host "Horni kiosk:"
Write-Host "http://${LanHost}:8080/kiosk/zahradky/horni-stanice"
Write-Host ""
Write-Host "Primy stream:"
Write-Host "http://${LanHost}:8889/${Stream}/"
