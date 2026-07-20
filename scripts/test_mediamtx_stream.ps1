$ErrorActionPreference = "Stop"
$Stream = "zahradky-horni-stanice"

Write-Host "=== MediaMTX stream smoke test (no RTSP output) ==="
& (Join-Path (Split-Path -Parent $PSScriptRoot) "scripts\status_mediamtx.ps1")

$fail = 0
try {
    $api = Invoke-RestMethod -Uri "http://127.0.0.1:9997/v3/paths/get/$Stream" -TimeoutSec 5
    if ($api.ready) { Write-Host "PASS path ready" -ForegroundColor Green }
    else { Write-Host "FAIL path not ready" -ForegroundColor Red; $fail = 1 }
} catch {
    Write-Host "FAIL path API: $($_.Exception.Message)" -ForegroundColor Red
    $fail = 1
}

try {
    $page = Invoke-WebRequest -Uri "http://127.0.0.1:8889/$Stream/" -UseBasicParsing -TimeoutSec 5
    if ($page.StatusCode -eq 200) { Write-Host "PASS browser page" -ForegroundColor Green }
} catch {
    Write-Host "FAIL browser page: $($_.Exception.Message)" -ForegroundColor Red
    $fail = 1
}

exit $fail
