<#
.SYNOPSIS
  Enable MediaMTX native rolling recording for office-test-camera only (v1.11.3).

.DESCRIPTION
  Patches gitignored deploy/mediamtx/mediamtx.local.yml and hot-applies via Control API.
  Does not enable recording on Zahrádky or other paths. Never prints RTSP credentials.
  Playback binds to 127.0.0.1:9996 only.

.NOTES
  MediaMTX v1.11.3 has no recordMaxPartSize — part size is bounded by recordPartDuration.
#>
[CmdletBinding()]
param(
    [string]$RecordRoot = "",
    [string]$SegmentDuration = "30s",
    [string]$PartDuration = "1s",
    [string]$DeleteAfter = "10m",
    [switch]$ApiOnly,
    [switch]$Disable
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Config = Join-Path $Root "deploy\mediamtx\mediamtx.local.yml"
$Api = "http://127.0.0.1:9997"
if (-not $RecordRoot) {
    $RecordRoot = Join-Path $Root "runtime\recordings"
}
$RecordPath = (($RecordRoot -replace '\\', '/') + "/%path/%Y-%m-%d_%H-%M-%S-%f")

function Write-RedactedConfigSnippet {
    Get-Content $Config | ForEach-Object {
        if ($_ -match 'source:|rtsp://') { '    <REDACTED>' } else { $_ }
    }
}

if (-not (Test-Path $Config)) {
    throw "Missing $Config — create mediamtx.local.yml first."
}

New-Item -ItemType Directory -Force -Path $RecordRoot | Out-Null
$backup = Join-Path $Root ("deploy\mediamtx\mediamtx.local.yml.bak-recording-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Copy-Item $Config $backup -Force
Write-Host "Backup: $(Split-Path $backup -Leaf)" -ForegroundColor DarkGray

if (-not $ApiOnly) {
    $lines = New-Object System.Collections.Generic.List[string]
    Get-Content $Config | ForEach-Object { $lines.Add($_) }

    # Ensure playback localhost
    $hasPlaybackAddr = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^playbackAddress:') { $lines[$i] = 'playbackAddress: 127.0.0.1:9996'; $hasPlaybackAddr = $true }
        if ($lines[$i] -match '^playback:\s*') {
            $lines[$i] = if ($Disable) { 'playback: no' } else { 'playback: yes' }
        }
    }
    if (-not $Disable -and -not $hasPlaybackAddr) {
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^playback:\s*yes') {
                $lines.Insert($i + 1, 'playbackAddress: 127.0.0.1:9996')
                break
            }
        }
    }

    # Strip any existing record:* under office-test-camera, then insert desired block
    $out = New-Object System.Collections.Generic.List[string]
    $inOffice = $false
    foreach ($line in $lines) {
        if ($line -match '^  office-test-camera:\s*$') {
            $inOffice = $true
            $out.Add($line)
            continue
        }
        if ($inOffice -and $line -match '^  [a-zA-Z]') {
            if (-not $Disable) {
                $out.Add('    record: yes')
                $out.Add("    recordPath: $RecordPath")
                $out.Add('    recordFormat: fmp4')
                $out.Add("    recordPartDuration: $PartDuration")
                $out.Add("    recordSegmentDuration: $SegmentDuration")
                $out.Add("    recordDeleteAfter: $DeleteAfter")
            } else {
                $out.Add('    record: no')
            }
            $inOffice = $false
            $out.Add($line)
            continue
        }
        if ($inOffice -and $line -match '^\s+record') { continue }
        $out.Add($line)
    }
    if ($inOffice) {
        if (-not $Disable) {
            $out.Add('    record: yes')
            $out.Add("    recordPath: $RecordPath")
            $out.Add('    recordFormat: fmp4')
            $out.Add("    recordPartDuration: $PartDuration")
            $out.Add("    recordSegmentDuration: $SegmentDuration")
            $out.Add("    recordDeleteAfter: $DeleteAfter")
        } else {
            $out.Add('    record: no')
        }
    }
    [IO.File]::WriteAllLines($Config, $out)
    Write-Host "Updated gitignored mediamtx.local.yml (office-test-camera only)." -ForegroundColor Green
}

# Hot-apply via API
if ($Disable) {
    Invoke-RestMethod -Method Patch -Uri "$Api/v3/config/global/patch" -ContentType 'application/json' -Body '{"playback":false}' | Out-Null
    Invoke-RestMethod -Method Patch -Uri "$Api/v3/config/paths/patch/office-test-camera" -ContentType 'application/json' -Body '{"record":false}' | Out-Null
} else {
    $g = '{"playback":true,"playbackAddress":"127.0.0.1:9996"}'
    Invoke-RestMethod -Method Patch -Uri "$Api/v3/config/global/patch" -ContentType 'application/json' -Body $g | Out-Null
    $p = @{
        record                 = $true
        recordPath             = $RecordPath
        recordFormat           = 'fmp4'
        recordPartDuration     = $PartDuration
        recordSegmentDuration  = $SegmentDuration
        recordDeleteAfter      = $DeleteAfter
    } | ConvertTo-Json
    Invoke-RestMethod -Method Patch -Uri "$Api/v3/config/paths/patch/office-test-camera" -ContentType 'application/json' -Body $p | Out-Null
}

Write-Host "API applied. Verifying other paths remain record=false..." -ForegroundColor Cyan
@('office-test-camera', 'zahradky-horni-stanice', 'zahradky-horni-stanice-90', 'zahradky-horni-stanice-92') | ForEach-Object {
    try {
        $c = Invoke-RestMethod "$Api/v3/config/paths/get/$_"
        Write-Host ("  {0,-32} record={1}" -f $_, $c.record)
    } catch {
        Write-Host ("  {0,-32} (path missing)" -f $_)
    }
}
