$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$Py = Join-Path $Root ".venv\Scripts\python.exe"
if (-not (Test-Path $Py)) {
    Write-Host "Create .venv first: py -3.10 -m venv .venv" -ForegroundColor Red
    exit 1
}

$env:PYTHONPATH = Join-Path $Root "backend"
Set-Location (Join-Path $Root "backend")
& $Py -m uvicorn app.main:app --host 127.0.0.1 --port 8000 --reload
