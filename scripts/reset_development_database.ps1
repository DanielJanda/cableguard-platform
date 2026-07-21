# Safe reset of the local development SQLite database only.
# Usage: .\scripts\reset_development_database.ps1 -ConfirmReset
param(
    [switch]$ConfirmReset
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ExpectedDbRelative = "data\cableguard.sqlite3"
$DbPath = Join-Path $Root $ExpectedDbRelative
$DbPathResolved = [System.IO.Path]::GetFullPath($DbPath)

if (-not $ConfirmReset) {
    Write-Host "No changes made. Pass -ConfirmReset to reset the local development database."
    exit 0
}

# Refuse non-local or unexpected database paths.
if ($DbPathResolved -notlike "$Root*") {
    Write-Error "Refusing reset: database path is outside the project root."
    exit 1
}
if ($DbPathResolved -match '^\\\\') {
    Write-Error "Refusing reset: network database paths are not allowed."
    exit 1
}

$listening = Get-NetTCPConnection -LocalPort 8000 -State Listen -ErrorAction SilentlyContinue
if ($listening) {
    Write-Warning "Event Core appears to be listening on port 8000 (PID $($listening.OwningProcess))."
    Write-Warning "Stop uvicorn before resetting the database to avoid corruption."
    exit 1
}

$backupDir = Join-Path $Root "runtime\backups"
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupBase = Join-Path $backupDir "cableguard-$timestamp"

if (Test-Path $DbPathResolved) {
    Copy-Item -Path $DbPathResolved -Destination "$backupBase.sqlite3" -Force
    Write-Host "Backup created: runtime\backups\cableguard-$timestamp.sqlite3"
}

foreach ($suffix in @("", "-wal", "-shm")) {
    $file = "$DbPathResolved$suffix"
    if (Test-Path $file) {
        Remove-Item -Force $file
        Write-Host "Removed $ExpectedDbRelative$suffix"
    }
}

$Py = Join-Path $Root ".venv\Scripts\python.exe"
if (-not (Test-Path $Py)) {
    Write-Error "Python venv not found at .venv. Run pip install -e '.[dev]' first."
    exit 1
}

$env:PYTHONPATH = Join-Path $Root "backend"
Push-Location (Join-Path $Root "backend")
try {
    & $Py -m alembic upgrade head
    if ($LASTEXITCODE -ne 0) {
        throw "Alembic upgrade failed with exit code $LASTEXITCODE"
    }
} finally {
    Pop-Location
}

Write-Host "Development database reset complete. No test events were seeded."
