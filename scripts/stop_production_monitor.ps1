#Requires -Version 5.1
<#
.SYNOPSIS
  Stop production CableGuard monitor runtime (Event Core + Nitro UI).
#>
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$PidDir = Join-Path $Root "runtime\production-monitor"

foreach ($name in @("event-core.pid", "nitro-ui.pid")) {
    $file = Join-Path $PidDir $name
    if (-not (Test-Path $file)) { continue }
    $pidVal = Get-Content $file -ErrorAction SilentlyContinue
    if ($pidVal -and (Get-Process -Id $pidVal -ErrorAction SilentlyContinue)) {
        Stop-Process -Id $pidVal -Force -ErrorAction SilentlyContinue
        Write-Host "Stopped PID $pidVal ($name)" -ForegroundColor Green
    }
    Remove-Item $file -Force -ErrorAction SilentlyContinue
}
Write-Host "Production monitor stop complete." -ForegroundColor Cyan
