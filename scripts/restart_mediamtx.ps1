#Requires -Version 5.1
<#
.SYNOPSIS
  Restart managed MediaMTX and wait until WHEP/path health passes.

.NOTES
  Exit codes:
    0 = restart complete, WHEP/path ready
    1 = start/stop failure (missing binary, config, immediate exit)
    2 = port conflict / foreign process
    3 = process running but WHEP/path not ready after retries
#>
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "mediamtx_health.ps1")

& (Join-Path $PSScriptRoot "stop_mediamtx.ps1")
Start-Sleep -Seconds 1

$startOutput = & (Join-Path $PSScriptRoot "start_mediamtx.ps1") 2>&1
Write-Output $startOutput
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$whep = Test-MediaMtxWhepReady -MaxAttempts 20 -DelaySec 1
if ($whep.Ready) {
    $via = if ($whep.Via) { " via $($whep.Via)" } else { "" }
    Write-Host "MediaMTX WHEP ready after restart (attempt $($whep.Attempt))$via." -ForegroundColor Green
    exit 0
}

Write-Host "MediaMTX process restarted but WHEP/path not ready after retries." -ForegroundColor Yellow
Write-Host "Check runtime/mediamtx logs - API 9997 is optional; WHEP is the primary video health signal." -ForegroundColor Yellow
exit 3
