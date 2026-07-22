#Requires -Version 5.1
<#
.SYNOPSIS
  Verify dual comparison MediaMTX paths (WHEP-first, production path untouched).
#>
$ErrorActionPreference = "Stop"

$LanHost = "10.6.1.40"
$Origin = "http://${LanHost}:8080"
$Streams = @(
    @{ Name = "zahradky-horni-stanice"; Label = "production" }
    @{ Name = "zahradky-horni-stanice-92"; Label = "compare-92" }
    @{ Name = "zahradky-horni-stanice-90"; Label = "compare-90" }
)

function Test-WhepPathReady {
    param(
        [string]$StreamName,
        [int]$MaxAttempts = 10,
        [int]$DelaySec = 1
    )

    $whepUrl = "http://${LanHost}:8889/${StreamName}/whep"
    $pageUrl = "http://${LanHost}:8889/${StreamName}/"

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            $options = Invoke-WebRequest -Uri $whepUrl -Method Options -Headers @{ Origin = $Origin } `
                -UseBasicParsing -TimeoutSec 4
            if ($options.StatusCode -eq 204) {
                return @{ Ready = $true; Attempt = $attempt; Via = "whep" }
            }
        } catch {
            # retry
        }

        try {
            $page = Invoke-WebRequest -Uri $pageUrl -UseBasicParsing -TimeoutSec 4
            if ($page.StatusCode -eq 200) {
                return @{ Ready = $true; Attempt = $attempt; Via = "page" }
            }
        } catch {
            # retry
        }

        if ($attempt -lt $MaxAttempts) { Start-Sleep -Seconds $DelaySec }
    }

    return @{ Ready = $false }
}

function Test-ApiPathReady {
    param([string]$StreamName)

    try {
        $api = Invoke-RestMethod -Uri "http://127.0.0.1:9997/v3/paths/get/$StreamName" -TimeoutSec 4
        return @{ Available = $true; Ready = [bool]$api.ready; SourceType = $api.source.type }
    } catch {
        return @{ Available = $false }
    }
}

Write-Host "=== Dual camera path verification ===" -ForegroundColor Cyan
$failed = 0

foreach ($stream in $Streams) {
    $name = $stream.Name
    Write-Host ""
    Write-Host "[$($stream.Label)] $name" -ForegroundColor Yellow

    $whep = Test-WhepPathReady -StreamName $name
    if ($whep.Ready) {
        Write-Host "  WHEP/path: READY (attempt $($whep.Attempt), $($whep.Via))" -ForegroundColor Green
    } else {
        Write-Host "  WHEP/path: NOT READY" -ForegroundColor Red
        $failed++
    }

    $api = Test-ApiPathReady -StreamName $name
    if ($api.Available) {
        Write-Host "  API ready=$($api.Ready) source=$($api.SourceType)" -ForegroundColor $(if ($api.Ready) { "Green" } else { "Yellow" })
        if ($api.Ready -ne $true) { $failed++ }
    } else {
        Write-Host "  API: optional/unavailable" -ForegroundColor DarkGray
    }
}

if ($failed -gt 0) {
    Write-Host ""
    Write-Host "FAIL: $failed check(s) failed." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "PASS: all comparison paths ready." -ForegroundColor Green
