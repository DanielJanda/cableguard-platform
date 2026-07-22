$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$RuntimeDir = Join-Path $Root "runtime\mediamtx"
$DeployDir = Join-Path $Root "deploy\mediamtx"
$Exe = Join-Path $RuntimeDir "mediamtx.exe"
$Config = Join-Path $DeployDir "mediamtx.local.yml"
$PidFile = Join-Path $RuntimeDir "mediamtx.pid"
$OutLog = Join-Path $RuntimeDir "mediamtx.out.log"
$ErrLog = Join-Path $RuntimeDir "mediamtx.err.log"

if (-not (Test-Path $Exe)) {
    Write-Host "Missing mediamtx.exe at $Exe" -ForegroundColor Red
    Write-Host "Download Windows amd64 binary from bluenviron/mediamtx releases into runtime/mediamtx/" -ForegroundColor Yellow
    exit 1
}
if (-not (Test-Path $Config)) {
    Write-Host "Missing local config: $Config" -ForegroundColor Red
    Write-Host "Run scripts/setup_mediamtx_local_config.ps1 or copy mediamtx.example.yml" -ForegroundColor Yellow
    exit 1
}

if (Test-Path $PidFile) {
    $oldPid = Get-Content $PidFile -ErrorAction SilentlyContinue
    if ($oldPid -and (Get-Process -Id $oldPid -ErrorAction SilentlyContinue)) {
        Write-Host "Managed MediaMTX already running (PID $oldPid)." -ForegroundColor Yellow
        Write-Host "Browser: http://127.0.0.1:8889/zahradky-horni-stanice" -ForegroundColor Cyan
        Write-Host "WHEP:    http://127.0.0.1:8889/zahradky-horni-stanice/whep" -ForegroundColor Cyan
        exit 0
    }
}

$ports = @(8554, 8888, 8889, 9997)
foreach ($port in $ports) {
    $listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($listener) {
        Write-Host "Port $port already in use (PID $($listener.OwningProcess)). Refusing duplicate start." -ForegroundColor Red
        exit 1
    }
}

New-Item -ItemType Directory -Force -Path $RuntimeDir | Out-Null
$proc = Start-Process -FilePath $Exe -ArgumentList $Config -WorkingDirectory $RuntimeDir -PassThru -WindowStyle Hidden -RedirectStandardOutput $OutLog -RedirectStandardError $ErrLog
$proc.Id | Set-Content $PidFile
Start-Sleep -Seconds 2

if ($proc.HasExited) {
    Write-Host "MediaMTX exited immediately. See $OutLog and $ErrLog" -ForegroundColor Red
    Remove-Item $PidFile -ErrorAction SilentlyContinue
    exit 1
}

Write-Host "Managed MediaMTX started (PID $($proc.Id))." -ForegroundColor Green
Write-Host "Browser: http://127.0.0.1:8889/zahradky-horni-stanice" -ForegroundColor Cyan
Write-Host "WHEP:    http://127.0.0.1:8889/zahradky-horni-stanice/whep" -ForegroundColor Cyan

. (Join-Path $PSScriptRoot "mediamtx_health.ps1")
$whep = Test-MediaMtxWhepReady -MaxAttempts 15 -DelaySec 1
if ($whep.Ready) {
    Write-Host "WHEP/path ready (attempt $($whep.Attempt))." -ForegroundColor Green
} else {
    Write-Host "MediaMTX process is up; WHEP/path not ready yet (will retry on reconnect)." -ForegroundColor Yellow
}
