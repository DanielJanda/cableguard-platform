$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$db = Join-Path $Root "data\cableguard.sqlite3"
$wal = "$db-wal"
$shm = "$db-shm"
foreach ($f in @($db, $wal, $shm)) {
    if (Test-Path $f) {
        Remove-Item -Force $f
        Write-Host "Removed $f"
    }
}
$Py = Join-Path $Root ".venv\Scripts\python.exe"
$env:PYTHONPATH = Join-Path $Root "backend"
Set-Location (Join-Path $Root "backend")
& $Py -m alembic upgrade head
Write-Host "Database reset and migrated."
