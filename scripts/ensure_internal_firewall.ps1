#Requires -Version 5.1
<#
.SYNOPSIS
  Ensure Windows Firewall allows CableGuard internal LAN ports (Domain + Private only).
#>
$ErrorActionPreference = "Stop"

$Rules = @(
    @{ Name = "CableGuard Monitor (TCP 8080)"; Port = 8080; Protocol = "TCP" }
    @{ Name = "CableGuard Event Core (TCP 8000)"; Port = 8000; Protocol = "TCP" }
    @{ Name = "CableGuard MediaMTX WHEP (TCP 8889)"; Port = 8889; Protocol = "TCP" }
    @{ Name = "CableGuard WebRTC ICE (UDP 8189)"; Port = 8189; Protocol = "UDP" }
)

$Profiles = @("Domain", "Private")

Write-Host "=== CableGuard internal LAN firewall audit ===" -ForegroundColor Cyan

foreach ($rule in $Rules) {
    $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
    if ($existing) {
        $enabled = ($existing | Select-Object -First 1).Enabled
        $profile = ($existing | Select-Object -First 1).Profile
        Write-Host "[OK] $($rule.Name) exists (Enabled=$enabled, Profile=$profile)" -ForegroundColor Green
        continue
    }

    Write-Host "[ADD] $($rule.Name) for profiles: $($Profiles -join ', ')" -ForegroundColor Yellow
    try {
        New-NetFirewallRule `
            -DisplayName $rule.Name `
            -Direction Inbound `
            -Action Allow `
            -Protocol $rule.Protocol `
            -LocalPort $rule.Port `
            -Profile $Profiles -ErrorAction Stop | Out-Null
        Write-Host "[ADD] Created $($rule.Name)" -ForegroundColor Green
    } catch {
        Write-Host "[SKIP] $($rule.Name) - admin rights required ($($_.Exception.Message))" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Not exposed (by design): TCP 9997 API, TCP 8554 RTSP, TCP 8888 HLS" -ForegroundColor DarkGray
