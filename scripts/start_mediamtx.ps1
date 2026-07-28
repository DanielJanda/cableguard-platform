$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$RuntimeDir = Join-Path $Root "runtime\mediamtx"
$DeployDir = Join-Path $Root "deploy\mediamtx"
$Exe = Join-Path $RuntimeDir "mediamtx.exe"
$Config = Join-Path $DeployDir "mediamtx.local.yml"
$PidFile = Join-Path $RuntimeDir "mediamtx.pid"
$OutLog = Join-Path $RuntimeDir "mediamtx.out.log"
$ErrLog = Join-Path $RuntimeDir "mediamtx.err.log"

function Write-Urls {
    Write-Host "Browser: http://127.0.0.1:8889/zahradky-horni-stanice" -ForegroundColor Cyan
    Write-Host "WHEP:    http://127.0.0.1:8889/zahradky-horni-stanice/whep" -ForegroundColor Cyan
}

function Adopt-Existing([int]$pidToAdopt, [string]$reason) {
    New-Item -ItemType Directory -Force -Path $RuntimeDir | Out-Null
    $pidToAdopt | Set-Content $PidFile
    Write-Host ("MediaMTX already running (PID {0}) - {1}. Adopted into PID file." -f $pidToAdopt, $reason) -ForegroundColor Yellow
    Write-Urls
    exit 0
}

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

# Never silently accept two MediaMTX instances.
$allLive = @(Get-Process -Name "mediamtx" -ErrorAction SilentlyContinue)
if ($allLive.Count -gt 1) {
    $pids = ($allLive | ForEach-Object { $_.Id }) -join ", "
    Write-Host ("Multiple MediaMTX processes found (PIDs {0}). Refusing adopt/start." -f $pids) -ForegroundColor Red
    exit 1
}

if (Test-Path $PidFile) {
    $oldPid = Get-Content $PidFile -ErrorAction SilentlyContinue
    if ($oldPid -and (Get-Process -Id $oldPid -ErrorAction SilentlyContinue)) {
        Write-Host ("Managed MediaMTX already running (PID {0})." -f $oldPid) -ForegroundColor Yellow
        Write-Urls
        exit 0
    }
}

if ($allLive.Count -eq 1) {
    Adopt-Existing -pidToAdopt $allLive[0].Id -reason "process found (heal/orphan adopt)"
}

$ports = @(8554, 8888, 8889, 9997)
foreach ($port in $ports) {
    $listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($listener) {
        $ownerPid = [int]$listener.OwningProcess
        $owner = Get-Process -Id $ownerPid -ErrorAction SilentlyContinue
        if ($owner -and $owner.ProcessName -match "mediamtx") {
            Adopt-Existing -pidToAdopt $ownerPid -reason ("port {0} already bound by MediaMTX" -f $port)
        }
        $ownerName = if ($owner) { $owner.ProcessName } else { "?" }
        Write-Host ("Port {0} already in use (PID {1} / {2}). Refusing duplicate start." -f $port, $ownerPid, $ownerName) -ForegroundColor Red
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

Write-Host ("Managed MediaMTX started (PID {0})." -f $proc.Id) -ForegroundColor Green
Write-Urls
