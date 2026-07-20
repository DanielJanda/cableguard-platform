$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$RuntimeDir = Join-Path $Root "runtime\mediamtx"
$PidFile = Join-Path $RuntimeDir "mediamtx.pid"
$Stream = "zahradky-horni-stanice"

Write-Host "=== CableGuard managed MediaMTX status ==="

if (Test-Path $PidFile) {
    $managedPid = Get-Content $PidFile
    $proc = Get-Process -Id $managedPid -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Host "Process: running PID $managedPid"
    } else {
        Write-Host "Process: PID file present but process not running"
    }
} else {
    Write-Host "Process: not running (no PID file)"
}

foreach ($port in @(8554, 8888, 8889, 9997, 8189)) {
    $conn = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($conn) { Write-Host "Port $port : LISTEN (PID $($conn.OwningProcess))" }
    else { Write-Host "Port $port : closed" }
}

try {
    $resp = Invoke-WebRequest -Uri "http://127.0.0.1:8889/$Stream/" -UseBasicParsing -TimeoutSec 5
    Write-Host "Browser page /$Stream/ : HTTP $($resp.StatusCode)"
} catch {
    Write-Host "Browser page /$Stream/ : unavailable ($($_.Exception.Message))"
}

try {
    $whep = Invoke-WebRequest -Uri "http://127.0.0.1:8889/$Stream/whep" -Method Options -UseBasicParsing -TimeoutSec 5
    Write-Host "WHEP /$Stream/whep : HTTP $($whep.StatusCode)"
} catch {
    Write-Host "WHEP /$Stream/whep : unavailable ($($_.Exception.Message))"
}

try {
    $api = Invoke-RestMethod -Uri "http://127.0.0.1:9997/v3/paths/list" -TimeoutSec 5
    $item = $api.items | Where-Object { $_.name -eq $Stream }
    if ($item) {
        Write-Host "Path $Stream : ready=$($item.ready) readers=$($item.readers.Count) source.type=$($item.source.type)"
    } else {
        Write-Host "Path $Stream : not listed in API"
    }
} catch {
    Write-Host "API paths/list : unavailable ($($_.Exception.Message))"
}
