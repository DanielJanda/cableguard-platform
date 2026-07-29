#Requires -Version 5.1
<#
.SYNOPSIS
  Patch gitignored mediamtx.local.yml with internal LAN WHEP browser origin.
#>
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Config = Join-Path $Root "deploy\mediamtx\mediamtx.local.yml"
$LanOrigin = if ($env:CABLEGUARD_PUBLIC_ORIGIN) { $env:CABLEGUARD_PUBLIC_ORIGIN.TrimEnd('/') } else { "http://10.6.1.40:8080" }

if (-not (Test-Path $Config)) {
    Write-Host "Missing $Config - run setup_mediamtx_local_config.ps1 first." -ForegroundColor Red
    exit 1
}

$content = Get-Content $Config -Raw

if ($content -match 'webrtcAllowOrigins:') {
    $content = [regex]::Replace(
        $content,
        '(?ms)^webrtcAllowOrigins:.*?(\r?\n(?![ \t-]))',
        "webrtcAllowOrigin: $LanOrigin`r`n"
    )
} elseif ($content -match 'webrtcAllowOrigin:') {
    $content = [regex]::Replace($content, 'webrtcAllowOrigin:.*', "webrtcAllowOrigin: $LanOrigin")
} else {
    Write-Host "Could not locate webrtcAllowOrigin in config." -ForegroundColor Red
    exit 1
}

Set-Content -Path $Config -Value $content -Encoding UTF8
Write-Host "Updated webrtcAllowOrigin to $LanOrigin in mediamtx.local.yml." -ForegroundColor Green
