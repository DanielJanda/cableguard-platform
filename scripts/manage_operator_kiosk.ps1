#Requires -Version 5.1
<#
.SYNOPSIS
  Idempotent Google Chrome operator kiosk lifecycle for CableGuard.

.DESCRIPTION
  install | start | stop | restart | status | uninstall

  - Dedicated Chrome user-data-dir under runtime/kiosk/chrome-profile (gitignored)
  - Scheduled Task CableGuardOperatorKiosk (logon trigger)
  - AutoplayAllowlist policy for CABLEGUARD_PUBLIC_ORIGIN only
  - Never kills unrelated chrome.exe processes

.EXAMPLE
  scripts/manage_operator_kiosk.ps1 -Action install
  scripts/manage_operator_kiosk.ps1 -Action status
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('install', 'start', 'stop', 'restart', 'status', 'uninstall')]
    [string]$Action,

    [string]$PublicOrigin = $env:CABLEGUARD_PUBLIC_ORIGIN,
    [string]$KioskPath = $env:CABLEGUARD_KIOSK_PATH,
    [string]$WindowsUser = $env:CABLEGUARD_KIOSK_WINDOWS_USER,
    [switch]$Elevated
)

$ErrorActionPreference = "Stop"

$TaskName = "CableGuardOperatorKiosk"
$Root = Split-Path -Parent $PSScriptRoot
$KioskRuntime = Join-Path $Root "runtime\kiosk"
$ProfileDir = Join-Path $KioskRuntime "chrome-profile"
$LauncherPs1 = Join-Path $KioskRuntime "launch_chrome_kiosk.ps1"
$StateFile = Join-Path $KioskRuntime "kiosk-state.json"
$ChromeMarker = "CableGuardKioskProfile"

if (-not $PublicOrigin) { $PublicOrigin = "http://10.6.1.40:8080" }
$PublicOrigin = $PublicOrigin.TrimEnd('/')
if (-not $KioskPath) { $KioskPath = "/kiosk/zahradky/horni-stanice" }
if (-not $KioskPath.StartsWith('/')) { $KioskPath = "/$KioskPath" }
$KioskUrl = "$PublicOrigin$KioskPath"
if (-not $WindowsUser) { $WindowsUser = $env:USERNAME }

function Write-Step($msg) { Write-Host $msg -ForegroundColor Cyan }
function Write-Ok($msg) { Write-Host $msg -ForegroundColor Green }
function Write-Warn($msg) { Write-Host $msg -ForegroundColor Yellow }
function Write-Err($msg) { Write-Host $msg -ForegroundColor Red }

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltinRole]::Administrator)
}

function Invoke-Elevated([string]$elevatedAction) {
    if ($Elevated) { throw "Elevation was requested but still not administrator." }
    Write-Warn "'$elevatedAction' requires administrator rights - UAC prompt will appear."
    $args = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-Action", $elevatedAction,
        "-PublicOrigin", "`"$PublicOrigin`"",
        "-KioskPath", "`"$KioskPath`"",
        "-WindowsUser", "`"$WindowsUser`"",
        "-Elevated"
    )
    $proc = Start-Process -FilePath "powershell.exe" -ArgumentList $args -Verb RunAs -Wait -PassThru
    if ($proc.ExitCode -ne 0) {
        throw "Elevated '$elevatedAction' failed with exit code $($proc.ExitCode)."
    }
}

function Find-ChromeExecutable {
    $candidates = @(
        "${env:ProgramFiles}\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
        "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return (Resolve-Path $c).Path }
    }
    $appPaths = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"
    )
    foreach ($key in $appPaths) {
        if (Test-Path $key) {
            $p = (Get-ItemProperty $key -ErrorAction SilentlyContinue).'(default)'
            if ($p -and (Test-Path $p)) { return $p }
        }
    }
    return $null
}

function Get-ChromeArgs([string]$chromeExe, [string]$profile, [string]$url) {
    return @(
        "--kiosk",
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-session-crashed-bubble",
        "--autoplay-policy=no-user-gesture-required",
        "--user-data-dir=$profile",
        $url
    )
}

function Write-LauncherScript([string]$chromeExe) {
    New-Item -ItemType Directory -Force -Path $KioskRuntime | Out-Null
    New-Item -ItemType Directory -Force -Path $ProfileDir | Out-Null
    Set-Content -Path (Join-Path $ProfileDir $ChromeMarker) -Value "CableGuard operator kiosk profile" -Encoding UTF8

    $lines = @(
        '#Requires -Version 5.1'
        '$ErrorActionPreference = ''Stop'''
        ('$Chrome = ''{0}''' -f $chromeExe)
        ('$Profile = ''{0}''' -f $ProfileDir)
        ('$Url = ''{0}''' -f $KioskUrl)
        '# Single-instance guard for CableGuard profile'
        '$existing = Get-CimInstance Win32_Process -Filter "Name = ''chrome.exe''" -ErrorAction SilentlyContinue |'
        '    Where-Object { $_.CommandLine -and $_.CommandLine -like ("*" + $Profile + "*") }'
        'if ($existing) {'
        '    Write-Host ''CableGuard kiosk Chrome already running.'''
        '    exit 0'
        '}'
        '$chromeArgs = @('
        '    ''--kiosk'','
        '    ''--no-first-run'','
        '    ''--no-default-browser-check'','
        '    ''--disable-session-crashed-bubble'','
        '    ''--autoplay-policy=no-user-gesture-required'','
        '    (''--user-data-dir='' + $Profile),'
        '    $Url'
        ')'
        'Start-Process -FilePath $Chrome -ArgumentList $chromeArgs | Out-Null'
    )
    Set-Content -Path $LauncherPs1 -Value ($lines -join "`r`n") -Encoding UTF8

    $state = @{
        chrome_exe     = $chromeExe
        profile_dir    = $ProfileDir
        kiosk_url      = $KioskUrl
        public_origin  = $PublicOrigin
        windows_user   = $WindowsUser
        task_name      = $TaskName
        updated_at     = (Get-Date).ToString("o")
    } | ConvertTo-Json
    Set-Content -Path $StateFile -Value $state -Encoding UTF8
}

function Install-AutoplayPolicy([string]$origin) {
    $paths = @(
        "HKLM:\Software\Policies\Google\Chrome\AutoplayAllowlist",
        "HKCU:\Software\Policies\Google\Chrome\AutoplayAllowlist"
    )
    $targetHive = if (Test-IsAdmin) { $paths[0] } else { $paths[1] }
    if ($targetHive.StartsWith("HKLM:") -and -not (Test-IsAdmin)) {
        Write-Warn "No admin rights - writing AutoplayAllowlist to HKCU (verify chrome://policy)."
        $targetHive = $paths[1]
    }
    New-Item -Path $targetHive -Force | Out-Null
    # Chrome AutoplayAllowlist is a list policy: numbered string values.
    # Keep only CableGuard origin - remove stale entries we previously wrote.
    $existing = Get-ItemProperty -Path $targetHive -ErrorAction SilentlyContinue
    if ($existing) {
        $existing.PSObject.Properties |
            Where-Object { $_.Name -match '^\d+$' } |
            ForEach-Object {
                Remove-ItemProperty -Path $targetHive -Name $_.Name -ErrorAction SilentlyContinue
            }
    }
    New-ItemProperty -Path $targetHive -Name "1" -Value $origin -PropertyType String -Force | Out-Null
    Write-Ok "AutoplayAllowlist set at $targetHive = $origin"
    return $targetHive
}

function Install-ScheduledTask {
    $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$LauncherPs1`""
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $WindowsUser
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew
    $principal = New-ScheduledTaskPrincipal -UserId $WindowsUser -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
    Write-Ok "Scheduled Task '$TaskName' registered for user '$WindowsUser' at logon."
}

function Uninstall-ScheduledTask {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Ok "Scheduled Task '$TaskName' removed."
    } else {
        Write-Warn "Scheduled Task '$TaskName' not found."
    }
}

function Get-KioskChromeProcesses {
    return @(Get-CimInstance Win32_Process -Filter "Name = 'chrome.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -like "*$ProfileDir*" })
}

function Start-Kiosk {
    if (-not (Test-Path $LauncherPs1)) {
        throw "Launcher missing. Run -Action install first."
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $LauncherPs1
    Start-Sleep -Seconds 2
    $procs = Get-KioskChromeProcesses
    if ($procs.Count -eq 0) {
        throw "Chrome kiosk did not start."
    }
    Write-Ok "Chrome kiosk running ($($procs.Count) process(es) for CableGuard profile)."
}

function Stop-Kiosk {
    $procs = Get-KioskChromeProcesses
    if ($procs.Count -eq 0) {
        Write-Warn "No CableGuard kiosk Chrome processes found."
        return
    }
    foreach ($p in $procs) {
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }
    Write-Ok "Stopped $($procs.Count) CableGuard kiosk Chrome process(es). Other Chrome profiles untouched."
}

function Show-Status {
    $chrome = Find-ChromeExecutable
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    $procs = Get-KioskChromeProcesses
    $policyHkLm = Get-ItemProperty "HKLM:\Software\Policies\Google\Chrome\AutoplayAllowlist" -ErrorAction SilentlyContinue
    $policyHkCu = Get-ItemProperty "HKCU:\Software\Policies\Google\Chrome\AutoplayAllowlist" -ErrorAction SilentlyContinue
    $allow = $null
    if ($policyHkLm -and $policyHkLm.1) { $allow = "HKLM:" + $policyHkLm.1 }
    elseif ($policyHkCu -and $policyHkCu.1) { $allow = "HKCU:" + $policyHkCu.1 }

    Write-Host "=== CableGuard Operator Kiosk Status ===" -ForegroundColor Cyan
    Write-Host "Chrome exe:        $(if ($chrome) { $chrome } else { 'NOT FOUND' })"
    Write-Host "Public origin:     $PublicOrigin"
    Write-Host "Kiosk URL:         $KioskUrl"
    Write-Host "Profile dir:       $ProfileDir"
    Write-Host "Launcher:          $(if (Test-Path $LauncherPs1) { $LauncherPs1 } else { 'missing' })"
    Write-Host "Scheduled Task:    $(if ($task) { "$($task.State)" } else { 'NOT INSTALLED' })"
    Write-Host "Kiosk processes:   $($procs.Count)"
    Write-Host "AutoplayAllowlist: $(if ($allow) { $allow } else { 'NOT SET' })"
    Write-Host "Verify in Chrome:  chrome://policy (AutoplayAllowlist must list the origin)"
}

# --- dispatch ---
$chromeExe = Find-ChromeExecutable
if ($Action -ne 'status' -and $Action -ne 'uninstall' -and -not $chromeExe) {
    Write-Err "Google Chrome not found. Install Chrome manually, then re-run."
    Write-Err "Searched Program Files, (x86), LocalAppData, and App Paths registry."
    exit 2
}

switch ($Action) {
    'install' {
        Write-Step "Installing CableGuard Chrome operator kiosk..."
        Write-LauncherScript -chromeExe $chromeExe
        try {
            Install-AutoplayPolicy -origin $PublicOrigin | Out-Null
        } catch {
            Write-Warn "AutoplayAllowlist write failed: $($_.Exception.Message)"
            Write-Warn "Re-run elevated: scripts/manage_operator_kiosk.ps1 -Action install"
        }
        try {
            Install-ScheduledTask
        } catch {
            Write-Warn "Scheduled Task registration failed: $($_.Exception.Message)"
            Write-Warn "Re-run as Administrator to register CableGuardOperatorKiosk at logon."
        }
        Show-Status
        Write-Ok "Install finished (launcher written). Start with: -Action start"
    }
    'start' {
        if (-not (Test-Path $LauncherPs1)) {
            Write-LauncherScript -chromeExe $chromeExe
        }
        Start-Kiosk
    }
    'stop' { Stop-Kiosk }
    'restart' {
        Stop-Kiosk
        Start-Sleep -Seconds 1
        Start-Kiosk
    }
    'status' { Show-Status }
    'uninstall' {
        Stop-Kiosk
        Uninstall-ScheduledTask
        Write-Warn "Chrome profile left at $ProfileDir (gitignored). Delete manually if desired."
        Write-Ok "Uninstall complete (policy AutoplayAllowlist not removed automatically)."
    }
}
