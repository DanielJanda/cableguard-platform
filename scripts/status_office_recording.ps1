<#
.SYNOPSIS
  Report MediaMTX rolling recording health for office-test-camera (no secrets).
#>
[CmdletBinding()]
param(
    [string]$PathName = "office-test-camera",
    [string]$RecordRoot = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $RecordRoot) { $RecordRoot = Join-Path $Root "runtime\recordings" }
$Api = "http://127.0.0.1:9997"
$dir = Join-Path $RecordRoot $PathName

$cfg = Invoke-RestMethod "$Api/v3/config/paths/get/$PathName"
$live = (Invoke-RestMethod "$Api/v3/paths/list").items | Where-Object { $_.name -eq $PathName } | Select-Object -First 1
$g = Invoke-RestMethod "$Api/v3/config/global/get"
$files = @()
if (Test-Path $dir) {
    $files = @(Get-ChildItem $dir -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)
}
$completed = @($files | Where-Object { $_.Length -gt 0 })
$size = ($files | Measure-Object Length -Sum).Sum
$free = [math]::Round((Get-PSDrive (Get-Item $RecordRoot).PSDrive.Name).Free / 1GB, 2)

$mtx = Get-Process mediamtx -ErrorAction SilentlyContinue | Select-Object -First 1

[pscustomobject]@{
    recording_enabled     = [bool]$cfg.record
    recording_path        = $cfg.recordPath
    cache_retention       = $cfg.recordDeleteAfter
    segment_duration      = $cfg.recordSegmentDuration
    part_duration         = $cfg.recordPartDuration
    record_format         = $cfg.recordFormat
    playback_enabled      = [bool]$g.playback
    playback_address      = $g.playbackAddress
    path_ready            = [bool]$live.ready
    segment_count         = $files.Count
    cache_size_bytes      = [int64]$size
    oldest_segment        = if ($files.Count) { $files[0].Name } else { $null }
    newest_segment        = if ($files.Count) { $files[-1].Name } else { $null }
    last_completed_segment= if ($completed.Count) { $completed[-1].Name } else { $null }
    free_disk_gb          = $free
    mediamtx_rss_mb       = if ($mtx) { [math]::Round($mtx.WorkingSet64 / 1MB, 1) } else { $null }
    last_recording_error  = $null
} | Format-List
