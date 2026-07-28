$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$RuntimeDir = Join-Path $Root "runtime\mediamtx"
$PidFile = Join-Path $RuntimeDir "mediamtx.pid"
$ServiceName = "CableGuardMediaMTX"
$ManageScript = Join-Path $PSScriptRoot "manage_mediamtx_service.ps1"

# When the service owns MediaMTX, killing the process would only trigger a recovery restart.
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    & $ManageScript -Action stop
    exit $LASTEXITCODE
}

function Stop-MediaMtxPid([int]$managedPid) {
    $proc = Get-Process -Id $managedPid -ErrorAction SilentlyContinue
    if (-not $proc) {
        Write-Host "PID $managedPid is not running." -ForegroundColor Yellow
        return
    }
    if ($proc.ProcessName -notmatch "mediamtx") {
        Write-Host "PID $managedPid is '$($proc.ProcessName)', not MediaMTX. Refusing to stop." -ForegroundColor Red
        exit 1
    }
    Stop-Process -Id $managedPid -Force
    Write-Host "Managed MediaMTX stopped (PID $managedPid)." -ForegroundColor Green
}

$stopped = $false
if (Test-Path $PidFile) {
    $managedPid = [int](Get-Content $PidFile)
    $proc = Get-Process -Id $managedPid -ErrorAction SilentlyContinue
    if ($proc) {
        Stop-MediaMtxPid $managedPid
        $stopped = $true
    } else {
        Write-Host "Stale PID file removed." -ForegroundColor Yellow
    }
    Remove-Item $PidFile -ErrorAction SilentlyContinue
}

if (-not $stopped) {
    $live = Get-Process -Name "mediamtx" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($live) {
        Stop-MediaMtxPid $live.Id
        $stopped = $true
    }
}

if (-not $stopped) {
    Write-Host "No MediaMTX process found. Nothing to stop." -ForegroundColor Yellow
}
