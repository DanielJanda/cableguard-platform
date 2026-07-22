#Requires -Version 5.1
<#
.SYNOPSIS
  Add comparison MediaMTX paths for Zahrádky horní stanice (10.2.4.90 vs 10.2.4.92).

.DESCRIPTION
  Keeps production path zahradky-horni-stanice unchanged.
  Adds:
    zahradky-horni-stanice-92  (mirror of production source)
    zahradky-horni-stanice-90  (H.264 substream /Streaming/Channels/102)
  Never prints RTSP credentials.
#>
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Config = Join-Path $Root "deploy\mediamtx\mediamtx.local.yml"
$ProfilePath = "/Streaming/Channels/102"

if (-not (Test-Path $Config)) {
    Write-Host "Missing $Config" -ForegroundColor Red
    exit 1
}

$content = Get-Content $Config -Raw
$prodMatch = [regex]::Match($content, '(?ms)^\s*zahradky-horni-stanice:\s*\r?\n\s*source:\s*(.+)\r?\n')
if (-not $prodMatch.Success) {
    Write-Host "Production path zahradky-horni-stanice not found in local config." -ForegroundColor Red
    exit 1
}

$prodSource = $prodMatch.Groups[1].Value.Trim()
if (-not $prodSource.StartsWith("rtsp://")) {
    Write-Host "Production source is not an RTSP URL." -ForegroundColor Red
    exit 1
}

$withoutScheme = $prodSource.Substring(7)
$atIndex = $withoutScheme.IndexOf("@")
if ($atIndex -lt 1) {
    Write-Host "Could not parse production RTSP URL (credentials not printed)." -ForegroundColor Red
    exit 1
}

$credentialPart = $withoutScheme.Substring(0, $atIndex)
$hostAndPath = $withoutScheme.Substring($atIndex + 1)
$credSplit = $credentialPart.IndexOf(":")
if ($credSplit -lt 1) {
    Write-Host "Could not parse production RTSP credentials (not printed)." -ForegroundColor Red
    exit 1
}

$user = $credentialPart.Substring(0, $credSplit)
$pass = $credentialPart.Substring($credSplit + 1)
$camera90Source = "rtsp://${user}:${pass}@10.2.4.90${ProfilePath}?rtsp_transport=tcp"

$block92 = @"
  zahradky-horni-stanice-92:
    source: $prodSource
    rtspTransport: tcp
    sourceOnDemand: no
"@

$block90 = @"
  zahradky-horni-stanice-90:
    source: $camera90Source
    rtspTransport: tcp
    sourceOnDemand: no
"@

if ($content -match 'zahradky-horni-stanice-92:') {
    $content = [regex]::Replace(
        $content,
        '(?ms)^\s*zahradky-horni-stanice-92:.*?(?=^\S|\z)',
        ($block92 + "`r`n")
    )
} else {
    $content = $content.TrimEnd() + "`r`n" + $block92 + "`r`n"
}

if ($content -match 'zahradky-horni-stanice-90:') {
    $content = [regex]::Replace(
        $content,
        '(?ms)^\s*zahradky-horni-stanice-90:.*?(?=^\S|\z)',
        ($block90 + "`r`n")
    )
} else {
    $content = $content.TrimEnd() + "`r`n" + $block90 + "`r`n"
}

Set-Content -Path $Config -Value $content -Encoding UTF8
Write-Host "Updated comparison paths in mediamtx.local.yml (production path unchanged)." -ForegroundColor Green
Write-Host "  zahradky-horni-stanice      -> production (10.2.4.92)" -ForegroundColor Cyan
Write-Host "  zahradky-horni-stanice-92   -> comparison alias (10.2.4.92)" -ForegroundColor Cyan
Write-Host "  zahradky-horni-stanice-90   -> comparison (10.2.4.90, H.264 $ProfilePath)" -ForegroundColor Cyan
