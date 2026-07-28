#Requires -Version 5.1
<#
.SYNOPSIS
  Start production CableGuard monitor runtime (Event Core + Nitro UI) on the public origin port.

.DESCRIPTION
  Single LAN origin (default http://10.6.1.40:8080):
    - Nitro node-server on 127.0.0.1:18080 (SSR UI)
    - Event Core (uvicorn) on 0.0.0.0:<public-port> with BFF + reverse-proxy to Nitro

  Does NOT use Vite dev server. Requires a prior monitor production build
  (cableguard-monitor npm run build:production-lan).
#>
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$MonitorRoot = Join-Path (Split-Path -Parent $Root) "cableguard-monitor"
$RuntimeDir = Join-Path $Root "runtime\production-monitor"
$PidDir = $RuntimeDir
$UiPidFile = Join-Path $PidDir "nitro-ui.pid"
$CorePidFile = Join-Path $PidDir "event-core.pid"
$UiOut = Join-Path $PidDir "nitro-ui.out.log"
$UiErr = Join-Path $PidDir "nitro-ui.err.log"
$CoreOut = Join-Path $PidDir "event-core.out.log"
$CoreErr = Join-Path $PidDir "event-core.err.log"
$Py = Join-Path $Root ".venv\Scripts\python.exe"
$Uvicorn = Join-Path $Root ".venv\Scripts\uvicorn.exe"
$NitroEntry = Join-Path $MonitorRoot ".output\server\index.mjs"

$PublicOrigin = if ($env:CABLEGUARD_PUBLIC_ORIGIN) { $env:CABLEGUARD_PUBLIC_ORIGIN.TrimEnd('/') } else { "http://10.6.1.40:8080" }
if ($PublicOrigin -notmatch '^https?://([^/:]+)(?::(\d+))?') {
    throw "Invalid CABLEGUARD_PUBLIC_ORIGIN: $PublicOrigin"
}
$LanHost = $Matches[1]
$PublicPort = if ($Matches[2]) { [int]$Matches[2] } else { 80 }
$UiUpstreamPort = 18080
$UiUpstream = "http://127.0.0.1:$UiUpstreamPort"

function Test-AlivePid([string]$file) {
    if (-not (Test-Path $file)) { return $false }
    $pidVal = Get-Content $file -ErrorAction SilentlyContinue
    if (-not $pidVal) { return $false }
    return [bool](Get-Process -Id $pidVal -ErrorAction SilentlyContinue)
}

New-Item -ItemType Directory -Force -Path $PidDir | Out-Null
Set-Location $Root

if (-not (Test-Path $Py)) { throw "Create .venv first: py -3.10 -m venv .venv" }
if (-not (Test-Path $NitroEntry)) {
    throw "Missing $NitroEntry - run in cableguard-monitor: npm run build:production-lan"
}
if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw "System Node.js not found on PATH."
}

if ((Test-AlivePid $CorePidFile) -and (Test-AlivePid $UiPidFile)) {
    Write-Host "Production monitor already running." -ForegroundColor Yellow
    Write-Host $PublicOrigin -ForegroundColor Cyan
    exit 0
}

$listener = Get-NetTCPConnection -LocalPort $PublicPort -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) {
    try {
        $null = Invoke-WebRequest -Uri "http://127.0.0.1:${PublicPort}/api/v1/health" -UseBasicParsing -TimeoutSec 5
        Write-Host "Production Event Core already listening on :$PublicPort - treating as running." -ForegroundColor Yellow
        Write-Host $PublicOrigin -ForegroundColor Cyan
        exit 0
    } catch {
        throw "Port $PublicPort already in use (PID $($listener.OwningProcess)) but health probe failed. Stop conflicting process first."
    }
}

# --- Nitro UI (loopback only) ---
if (-not (Test-AlivePid $UiPidFile)) {
    $uiListener = Get-NetTCPConnection -LocalPort $UiUpstreamPort -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($uiListener) {
        throw "Port $UiUpstreamPort already in use (PID $($uiListener.OwningProcess))."
    }
    $env:HOST = "127.0.0.1"
    $env:PORT = "$UiUpstreamPort"
    $env:NITRO_PORT = "$UiUpstreamPort"
    $ui = Start-Process -FilePath "node" `
        -ArgumentList @($NitroEntry) `
        -WorkingDirectory (Join-Path $MonitorRoot ".output") `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $UiOut -RedirectStandardError $UiErr
    $ui.Id | Set-Content $UiPidFile
    Start-Sleep -Seconds 2
    if ($ui.HasExited) {
        Remove-Item $UiPidFile -ErrorAction SilentlyContinue
        throw "Nitro UI exited immediately. See $UiErr"
    }
}

# --- Event Core (public) ---
$env:PYTHONPATH = Join-Path $Root "backend"
$env:CABLEGUARD_HOST = "0.0.0.0"
$env:CABLEGUARD_PORT = "$PublicPort"
$env:CABLEGUARD_PUBLIC_ORIGIN = $PublicOrigin
$env:CABLEGUARD_MONITOR_UI_UPSTREAM = $UiUpstream
$env:CABLEGUARD_MONITOR_STATIC_DIR = ""
if (-not $env:CABLEGUARD_CORS_ORIGINS) {
    $env:CABLEGUARD_CORS_ORIGINS = "$PublicOrigin,http://localhost:$PublicPort,http://127.0.0.1:$PublicPort"
}

$core = Start-Process -FilePath $Uvicorn `
    -ArgumentList @("app.main:app", "--app-dir", "backend", "--host", "0.0.0.0", "--port", "$PublicPort") `
    -WorkingDirectory $Root `
    -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $CoreOut -RedirectStandardError $CoreErr
$core.Id | Set-Content $CorePidFile
Start-Sleep -Seconds 3

if ($core.HasExited) {
    Write-Host "Event Core exited immediately. See $CoreErr" -ForegroundColor Red
    if (Test-Path $UiPidFile) {
        $uipid = Get-Content $UiPidFile
        Stop-Process -Id $uipid -Force -ErrorAction SilentlyContinue
        Remove-Item $UiPidFile -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $CorePidFile -Force -ErrorAction SilentlyContinue
    exit 1
}

try {
    $null = Invoke-WebRequest -Uri "http://127.0.0.1:${PublicPort}/api/v1/health" -UseBasicParsing -TimeoutSec 10
    $null = Invoke-WebRequest -Uri "http://127.0.0.1:${PublicPort}/dashboard" -UseBasicParsing -TimeoutSec 10
    Write-Host "Production monitor runtime OK." -ForegroundColor Green
} catch {
    Write-Host "Started but probe failed: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host "Public origin: $PublicOrigin" -ForegroundColor Cyan
Write-Host "Dashboard:     $PublicOrigin/dashboard" -ForegroundColor Cyan
Write-Host "Kiosk:         $PublicOrigin/kiosk/zahradky/horni-stanice" -ForegroundColor Cyan
Write-Host "UI upstream:   $UiUpstream (loopback Nitro)" -ForegroundColor DarkGray
