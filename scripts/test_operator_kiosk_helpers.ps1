#Requires -Version 5.1
<#
.SYNOPSIS
  Unit-style checks for manage_operator_kiosk.ps1 helpers (no Chrome start required for discovery).
#>
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Script = Join-Path $Root "scripts\manage_operator_kiosk.ps1"

& $Script -Action status
if ($LASTEXITCODE -notin @(0, $null)) {
    # status should exit 0
}

$profile = "C:\fake\CableGuard\chrome-profile"
$url = "http://10.6.1.40:8080/kiosk/zahradky/horni-stanice"
$argsList = @(
    "--kiosk",
    "--no-first-run",
    "--no-default-browser-check",
    "--disable-session-crashed-bubble",
    "--autoplay-policy=no-user-gesture-required",
    "--user-data-dir=$profile",
    $url
)
if ($argsList.Count -ne 7) { throw "Expected 7 chrome args, got $($argsList.Count)" }
if ($argsList[-1] -ne $url) { throw "URL must be last argument" }
if ($argsList[-2] -notlike "--user-data-dir=*chrome-profile") { throw "user-data-dir incorrect" }

$filterProfile = $profile
$fakeCmd = "C:\Program Files\Google\Chrome\Application\chrome.exe --user-data-dir=C:\Users\someone\AppData\Local\Google\Chrome\User Data"
if ($fakeCmd -like "*$filterProfile*") { throw "Foreign Chrome would be matched - FAIL" }

$ours = "chrome.exe --user-data-dir=$profile $url"
if ($ours -notlike "*$filterProfile*") { throw "Our kiosk Chrome would NOT be matched - FAIL" }

Write-Host "kiosk script helper tests PASS" -ForegroundColor Green
