$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$RuntimeDir = Join-Path $Root "runtime\mediamtx"
$PidFile = Join-Path $RuntimeDir "mediamtx.pid"
$Stream = "zahradky-horni-stanice"

. (Join-Path $PSScriptRoot "mediamtx_health.ps1")

Write-Host "=== CableGuard managed MediaMTX status ==="

$procState = Test-MediaMtxProcessRunning -PidFile $PidFile
if ($procState.State -eq "running") {
    Write-Host "Process: RUNNING PID $($procState.Pid)"
} elseif ($procState.Note -eq "stale_pid_file") {
    Write-Host "Process: STOPPED (stale PID file for $($procState.Pid))"
} else {
    Write-Host "Process: STOPPED"
}

foreach ($port in @(8554, 8888, 8889, 9997)) {
    $portState = Test-MediaMtxPortListening -Port $port
    if ($portState.State -eq "listening") {
        Write-Host "Port $port : LISTEN (PID $($portState.Pid))"
    } else {
        Write-Host "Port $port : closed"
    }
}

$whep = Test-MediaMtxWhepReady -StreamName $Stream -MaxAttempts 3 -DelaySec 1
if ($whep.Ready) {
    $detail = if ($whep.WhepStatus) { "WHEP HTTP $($whep.WhepStatus)" } else { "browser page HTTP $($whep.PageStatus)" }
    Write-Host "Video health: READY ($detail)"
} else {
    Write-Host "Video health: NOT READY (WHEP/path probe failed)"
}

$api = Test-MediaMtxApiPaths -StreamName $Stream
if ($api.Available) {
    Write-Host "API 9997: optional ready=$($api.Ready) source.type=$($api.SourceType)"
} else {
    Write-Host "API 9997: optional/unavailable (not required for LAN video health)"
}
