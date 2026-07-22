#Requires -Version 5.1
<#
.SYNOPSIS
  Sample dual-camera MediaMTX path stats every 30s (no browser required).
  Usage: .\scripts\compare_cameras_benchmark.ps1 -Minutes 20
#>
param(
    [int]$Minutes = 20,
    [int]$IntervalSec = 30,
    [string]$OutFile = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $OutFile) {
    $OutFile = Join-Path $Root "runtime\compare-benchmark.json"
}

$LanHost = "10.6.1.40"
$Origin = "http://${LanHost}:8080"
$Streams = @(
    @{ Id = "camera92"; Ip = "10.2.4.92"; Name = "zahradky-horni-stanice-92" }
    @{ Id = "camera90"; Ip = "10.2.4.90"; Name = "zahradky-horni-stanice-90" }
)

function Test-WhepHandshake {
    param([string]$StreamName)
    try {
        $whep = "http://${LanHost}:8889/${StreamName}/whep"
        $opt = Invoke-WebRequest -Uri $whep -Method Options -Headers @{ Origin = $Origin } -UseBasicParsing -TimeoutSec 5
        return @{ Ok = ($opt.StatusCode -eq 204); Options = $opt.StatusCode }
    } catch {
        return @{ Ok = $false; Error = $_.Exception.Message }
    }
}

function Get-PathStats {
    param([string]$StreamName)
    try {
        $api = Invoke-RestMethod -Uri "http://127.0.0.1:9997/v3/paths/get/$StreamName" -TimeoutSec 5
        return @{
            Ready = [bool]$api.ready
            Readers = $api.readers.Count
            BytesReceived = $api.bytesReceived
            SourceType = $api.source.type
        }
    } catch {
        return @{ Ready = $false; Error = $_.Exception.Message }
    }
}

$startedAt = Get-Date
$samples = @()
$endAt = $startedAt.AddMinutes($Minutes)
$tick = 0

Write-Host "Benchmark $Minutes min, interval ${IntervalSec}s -> $OutFile" -ForegroundColor Cyan

while ((Get-Date) -lt $endAt) {
    $tick++
    $row = [ordered]@{
        tick = $tick
        at = (Get-Date).ToString("o")
        streams = @{}
    }
    foreach ($s in $Streams) {
        $row.streams[$s.Id] = @{
            ip = $s.Ip
            name = $s.Name
            whep = Test-WhepHandshake -StreamName $s.Name
            path = Get-PathStats -StreamName $s.Name
        }
    }
    $samples += [pscustomobject]$row
    $remaining = [math]::Max(0, [int]($endAt - (Get-Date)).TotalSeconds)
    Write-Host "[tick $tick] 92 ready=$($row.streams.camera92.path.Ready) 90 ready=$($row.streams.camera90.path.Ready) remaining=${remaining}s"
    if ((Get-Date) -lt $endAt) { Start-Sleep -Seconds $IntervalSec }
}

$mtx = Get-Process -Name mediamtx -ErrorAction SilentlyContinue | Select-Object -First 1
$report = [ordered]@{
    startedAt = $startedAt.ToString("o")
    finishedAt = (Get-Date).ToString("o")
    durationMin = $Minutes
    samples = $samples
    mediamtx = if ($mtx) { @{ pid = $mtx.Id; cpu = $mtx.CPU; workingSetMb = [math]::Round($mtx.WorkingSet64 / 1MB, 1) } } else { $null }
    note = "Use compare page for visual latency; path bytesReceived growth indicates active RTSP ingest."
}

New-Item -ItemType Directory -Force -Path (Split-Path $OutFile) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -Path $OutFile -Encoding UTF8
Write-Host "Wrote $OutFile" -ForegroundColor Green
