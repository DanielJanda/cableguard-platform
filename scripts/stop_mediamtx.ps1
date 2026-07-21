$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$RuntimeDir = Join-Path $Root "runtime\mediamtx"
$PidFile = Join-Path $RuntimeDir "mediamtx.pid"

if (-not (Test-Path $PidFile)) {
    Write-Host "No managed MediaMTX PID file found. Nothing to stop." -ForegroundColor Yellow
    exit 0
}

$managedPid = [int](Get-Content $PidFile)
$proc = Get-Process -Id $managedPid -ErrorAction SilentlyContinue
if (-not $proc) {
    Remove-Item $PidFile -ErrorAction SilentlyContinue
    Write-Host "Stale PID file removed." -ForegroundColor Yellow
    exit 0
}

if ($proc.Path -and ($proc.Path -notmatch "mediamtx")) {
    Write-Host "PID $managedPid is not a managed MediaMTX process. Refusing to stop." -ForegroundColor Red
    exit 1
}

Stop-Process -Id $managedPid -Force
Remove-Item $PidFile -ErrorAction SilentlyContinue
Write-Host "Managed MediaMTX stopped (PID $managedPid)." -ForegroundColor Green
