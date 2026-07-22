#Requires -Version 5.1
<#
.SYNOPSIS
  Shared MediaMTX health helpers (WHEP-first, API optional).
#>
$ErrorActionPreference = "Stop"

function Test-MediaMtxProcessRunning {
    param(
        [Parameter(Mandatory = $true)][string]$PidFile
    )

    if (-not (Test-Path $PidFile)) {
        return @{ State = "stopped"; Pid = $null }
    }

    $pidValue = Get-Content $PidFile -ErrorAction SilentlyContinue
    if (-not $pidValue) {
        return @{ State = "stopped"; Pid = $null; Note = "empty_pid_file" }
    }

    $proc = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
    if (-not $proc) {
        return @{ State = "stopped"; Pid = [int]$pidValue; Note = "stale_pid_file" }
    }

    return @{ State = "running"; Pid = $proc.Id }
}

function Test-MediaMtxPortListening {
    param(
        [Parameter(Mandatory = $true)][int]$Port
    )

    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $listener) {
        return @{ State = "closed"; Pid = $null }
    }
    return @{ State = "listening"; Pid = $listener.OwningProcess }
}

function Test-MediaMtxWhepReady {
    param(
        [string]$StreamName = "zahradky-horni-stanice",
        [string]$HostName = "127.0.0.1",
        [string]$Origin = "http://10.6.1.40:8080",
        [int]$MaxAttempts = 15,
        [int]$DelaySec = 1
    )

    $whepUrl = "http://${HostName}:8889/${StreamName}/whep"
    $pageUrl = "http://${HostName}:8889/${StreamName}/"

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            $options = Invoke-WebRequest -Uri $whepUrl -Method Options -Headers @{ Origin = $Origin } `
                -UseBasicParsing -TimeoutSec 4
            if ($options.StatusCode -eq 204) {
                return @{
                    Ready = $true
                    Attempt = $attempt
                    WhepStatus = $options.StatusCode
                    PageStatus = $null
                }
            }
        } catch {
            # retry
        }

        try {
            $page = Invoke-WebRequest -Uri $pageUrl -UseBasicParsing -TimeoutSec 4
            if ($page.StatusCode -eq 200) {
                return @{
                    Ready = $true
                    Attempt = $attempt
                    WhepStatus = $null
                    PageStatus = $page.StatusCode
                    Via = "browser_page"
                }
            }
        } catch {
            # retry
        }

        if ($attempt -lt $MaxAttempts) {
            Start-Sleep -Seconds $DelaySec
        }
    }

    return @{
        Ready = $false
        Attempt = $MaxAttempts
        WhepStatus = $null
        PageStatus = $null
    }
}

function Test-MediaMtxApiPaths {
    param(
        [string]$StreamName = "zahradky-horni-stanice",
        [string]$ApiBase = "http://127.0.0.1:9997"
    )

    try {
        $api = Invoke-RestMethod -Uri "$ApiBase/v3/paths/get/$StreamName" -TimeoutSec 4
        return @{
            Available = $true
            Ready = [bool]$api.ready
            SourceType = $api.source.type
        }
    } catch {
        return @{
            Available = $false
            Error = $_.Exception.Message
        }
    }
}
