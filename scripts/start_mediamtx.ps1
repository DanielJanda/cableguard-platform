<#
.SYNOPSIS
    Starts MediaMTX. Uses the CableGuardMediaMTX Windows service whenever it is installed.

.DESCRIPTION
    The service is the production path (continuous ingest, survives logoff, restarts on
    failure). Starting mediamtx.exe directly is only allowed in explicit development mode
    and only while the service does not exist, so two instances can never fight over the
    RTSP/WHEP/API ports.

.PARAMETER Development
    Start a foreground-owned mediamtx.exe instead of the service. Refused when the
    service is installed.
#>
[CmdletBinding()]
param(
    [switch]$Development
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$RuntimeDir = Join-Path $Root "runtime\mediamtx"
$DeployDir = Join-Path $Root "deploy\mediamtx"
$Exe = Join-Path $RuntimeDir "mediamtx.exe"
$Config = Join-Path $DeployDir "mediamtx.local.yml"
$PidFile = Join-Path $RuntimeDir "mediamtx.pid"
$OutLog = Join-Path $RuntimeDir "mediamtx.out.log"
$ErrLog = Join-Path $RuntimeDir "mediamtx.err.log"
$ServiceName = "CableGuardMediaMTX"
$ManageScript = Join-Path $PSScriptRoot "manage_mediamtx_service.ps1"

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

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($service) {
    if ($Development) {
        Write-Host "Service $ServiceName is installed - refusing a second development instance." -ForegroundColor Red
        Write-Host "Use scripts/manage_mediamtx_service.ps1 -Action uninstall first." -ForegroundColor Yellow
        exit 1
    }
    & $ManageScript -Action start
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Urls
    exit 0
}

if (-not $Development) {
    Write-Host "MediaMTX service $ServiceName is not installed." -ForegroundColor Red
    Write-Host "Install the permanent service:  scripts/manage_mediamtx_service.ps1 -Action install" -ForegroundColor Yellow
    Write-Host "Or start a throwaway dev instance:  scripts/start_mediamtx.ps1 -Development" -ForegroundColor Yellow
    exit 1
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

Write-Host ("Development MediaMTX started (PID {0})." -f $proc.Id) -ForegroundColor Green
Write-Urls
