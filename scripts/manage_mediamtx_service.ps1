<#
.SYNOPSIS
    Manages MediaMTX as the permanent local Windows service CableGuardMediaMTX.

.DESCRIPTION
    One authoritative entrypoint for the MediaMTX service lifecycle. The service is a
    WinSW wrapper kept in gitignored runtime/mediamtx-service/; the wrapper binary is
    never committed and is downloaded on demand from a pinned release.

    Idempotent: install/start/stop/restart/uninstall can be repeated safely and never
    create a second service or a second mediamtx.exe process. The MediaMTX config
    (deploy/mediamtx/mediamtx.local.yml) is only read, never written, and RTSP
    credentials are never printed.

    install additionally grants the current user start/stop rights on the service so
    Admin Studio can drive the lifecycle without elevation.

.EXAMPLE
    scripts/manage_mediamtx_service.ps1 -Action install
    scripts/manage_mediamtx_service.ps1 -Action status
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('install', 'start', 'stop', 'restart', 'status', 'uninstall')]
    [string]$Action,

    # Internal: set when the script has already re-launched itself elevated.
    [switch]$Elevated
)

$ErrorActionPreference = "Stop"

$ServiceName = "CableGuardMediaMTX"
$ServiceDisplayName = "CableGuard MediaMTX"
$WinSwUrl = "https://github.com/winsw/winsw/releases/download/v2.12.0/WinSW-x64.exe"
$WinSwSha256 = "05B82D46AD331CC16BDC00DE5C6332C1EF818DF8CEEFCD49C726553209B3A0DA"

$Root = Split-Path -Parent $PSScriptRoot
$RuntimeDir = Join-Path $Root "runtime\mediamtx"
$ServiceDir = Join-Path $Root "runtime\mediamtx-service"
$MediaMtxExe = Join-Path $RuntimeDir "mediamtx.exe"
$ConfigFile = Join-Path $Root "deploy\mediamtx\mediamtx.local.yml"
$PidFile = Join-Path $RuntimeDir "mediamtx.pid"
$WrapperExe = Join-Path $ServiceDir "$ServiceName.exe"
$WrapperXml = Join-Path $ServiceDir "$ServiceName.xml"
$BackupDir = Join-Path $Root "runtime\test-results\office-fall-preparation"
$ApiBase = "http://127.0.0.1:9997"
$ExpectedPaths = @("office-test-camera", "zahradky-horni-stanice")

function Write-Step($msg) { Write-Host $msg -ForegroundColor Cyan }
function Write-Ok($msg) { Write-Host $msg -ForegroundColor Green }
function Write-Warn($msg) { Write-Host $msg -ForegroundColor Yellow }
function Write-Err($msg) { Write-Host $msg -ForegroundColor Red }

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltinRole]::Administrator)
}

# Re-launches this script elevated for the operations the SCM only allows to admins.
function Invoke-Elevated([string]$elevatedAction) {
    if ($Elevated) {
        throw "Elevation was requested but the elevated run is still not administrator."
    }
    Write-Warn "'$elevatedAction' requires administrator rights - a UAC prompt will appear."
    $psi = @{
        FilePath     = "powershell.exe"
        ArgumentList = @(
            "-NoProfile", "-ExecutionPolicy", "Bypass",
            "-File", "`"$PSCommandPath`"",
            "-Action", $elevatedAction, "-Elevated"
        )
        Verb         = "RunAs"
        Wait         = $true
        PassThru     = $true
    }
    $proc = Start-Process @psi
    if ($proc.ExitCode -ne 0) {
        throw "Elevated '$elevatedAction' failed with exit code $($proc.ExitCode)."
    }
    Write-Ok "Elevated '$elevatedAction' finished."
}

function Get-ServiceOrNull {
    return Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
}

function Get-MediaMtxProcesses {
    return @(Get-Process -Name "mediamtx" -ErrorAction SilentlyContinue)
}

function Assert-Prerequisites {
    if (-not (Test-Path $MediaMtxExe)) {
        throw "Missing MediaMTX binary at $MediaMtxExe"
    }
    if (-not (Test-Path $ConfigFile)) {
        throw "Missing local config $ConfigFile (run scripts/setup_mediamtx_local_config.ps1 first)."
    }
}

function Initialize-Wrapper {
    New-Item -ItemType Directory -Force -Path $ServiceDir | Out-Null
    New-Item -ItemType Directory -Force -Path $RuntimeDir | Out-Null

    if (Test-Path $WrapperExe) {
        $actual = (Get-FileHash $WrapperExe -Algorithm SHA256).Hash
        if ($actual -ne $WinSwSha256) {
            Write-Warn "Service wrapper hash mismatch - re-downloading pinned WinSW."
            Remove-Item $WrapperExe -Force
        }
    }
    if (-not (Test-Path $WrapperExe)) {
        Write-Step "Downloading pinned service wrapper (WinSW) into runtime/mediamtx-service/ ..."
        Invoke-WebRequest -Uri $WinSwUrl -OutFile $WrapperExe -UseBasicParsing -TimeoutSec 300
        $actual = (Get-FileHash $WrapperExe -Algorithm SHA256).Hash
        if ($actual -ne $WinSwSha256) {
            Remove-Item $WrapperExe -Force
            throw "Downloaded wrapper SHA-256 $actual does not match pinned $WinSwSha256."
        }
        Write-Ok "Service wrapper ready."
    }

    # Wrapper definition is derived from the repo layout, so it is always regenerated.
    $xml = @"
<service>
  <id>$ServiceName</id>
  <name>$ServiceDisplayName</name>
  <description>CableGuard MediaMTX RTSP proxy / WHEP gateway (continuous local ingest).</description>
  <executable>$MediaMtxExe</executable>
  <arguments>"$ConfigFile"</arguments>
  <workingdirectory>$RuntimeDir</workingdirectory>
  <logpath>$RuntimeDir</logpath>
  <log mode="roll-by-size">
    <sizeThreshold>10240</sizeThreshold>
    <keepFiles>5</keepFiles>
  </log>
  <startmode>Automatic</startmode>
  <delayedAutoStart>true</delayedAutoStart>
  <onfailure action="restart" delay="5 sec" />
  <onfailure action="restart" delay="10 sec" />
  <onfailure action="restart" delay="20 sec" />
  <resetfailure>1 hour</resetfailure>
  <stoptimeout>15 sec</stoptimeout>
  <stopparentprocessfirst>true</stopparentprocessfirst>
</service>
"@
    Set-Content -Path $WrapperXml -Value $xml -Encoding UTF8
}

function Backup-Config {
    New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    Copy-Item $ConfigFile (Join-Path $BackupDir "mediamtx.local.yml.$stamp.bak") -Force
    Write-Ok "Config backed up into runtime/test-results/office-fall-preparation/."
}

# The service must be controllable from the non-elevated Admin Studio.
function Grant-ServiceControlToCurrentUser {
    $sid = ([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
    $sdRaw = (& sc.exe sdshow $ServiceName) -join ""
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdRaw)) {
        Write-Warn "Could not read the service security descriptor; skipping rights grant."
        return
    }
    if ($sdRaw -match [regex]::Escape($sid)) {
        Write-Ok "Current user already has explicit service control rights."
        return
    }
    $ace = "(A;;CCLCSWRPWPDTLOCRRC;;;$sid)"
    # SDDL sections are ordered O:G:D:S: — the new ACE must land at the end of D: only.
    if ($sdRaw -match '^(?<head>.*?D:(?:\((?:[^()]*)\))*)(?<tail>(?:S:.*)?)$') {
        $newSd = $Matches['head'] + $ace + $Matches['tail']
    }
    else {
        Write-Warn "Unexpected security descriptor format; skipping rights grant."
        return
    }
    $sdsetOutput = & sc.exe sdset $ServiceName $newSd 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Ok "Granted start/stop rights to the current user."
    }
    else {
        Write-Warn "sdset returned $LASTEXITCODE - service control will still need elevation."
        Write-Warn ($sdsetOutput -join " ")
    }
}

function Set-ScmRecovery {
    # Belt and braces: WinSW restarts the child, the SCM restarts WinSW itself.
    & sc.exe failure $ServiceName reset= 3600 actions= restart/5000/restart/10000/restart/20000 | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Ok "SCM failure recovery configured (restart x3)." }
    else { Write-Warn "sc failure returned $LASTEXITCODE." }
}

function Get-ApiPaths {
    try {
        $resp = Invoke-RestMethod -Uri "$ApiBase/v3/paths/list" -TimeoutSec 4 -ErrorAction Stop
        return $resp.items
    }
    catch { return $null }
}

function Write-PidFileFromService {
    $procs = Get-MediaMtxProcesses
    if ($procs.Count -eq 1) {
        Set-Content -Path $PidFile -Value $procs[0].Id
    }
}

function Show-Status {
    $svc = Get-ServiceOrNull
    if ($null -eq $svc) {
        Write-Host "service       : NOT INSTALLED"
        Write-Host "startup       : -"
        Write-Host "recovery      : -"
    }
    else {
        $wmi = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
        $startup = if ($wmi.DelayedAutoStart) { "Automatic (Delayed Start)" } else { $wmi.StartMode }
        $failure = (& sc.exe qfailure $ServiceName) -join " "
        $restarts = ([regex]::Matches($failure, "RESTART")).Count
        Write-Host ("service       : {0}" -f $svc.Status.ToString().ToUpperInvariant())
        Write-Host ("startup       : {0}" -f $startup)
        Write-Host ("recovery      : {0}" -f $(if ($restarts -gt 0) { "restart x$restarts" } else { "NONE" }))
    }

    $procs = Get-MediaMtxProcesses
    if ($procs.Count -eq 0) { Write-Host "process       : STOPPED" }
    elseif ($procs.Count -eq 1) { Write-Host ("process       : RUNNING pid={0}" -f $procs[0].Id) }
    else { Write-Err ("process       : FAULT - {0} mediamtx.exe instances (PIDs {1})" -f $procs.Count, (($procs | ForEach-Object { $_.Id }) -join ", ")) }

    $paths = Get-ApiPaths
    if ($null -eq $paths) {
        Write-Host "control api   : OFFLINE"
        Write-Host "stream paths  : NOT READY"
        return
    }
    Write-Host "control api   : READY"
    $ready = 0
    foreach ($expected in $ExpectedPaths) {
        $p = $paths | Where-Object { $_.name -eq $expected } | Select-Object -First 1
        if ($null -ne $p -and $p.ready) {
            $ready++
            Write-Host ("  path {0,-24} READY readers={1} tracks={2}" -f $expected, $p.readers.Count, ($p.tracks -join ","))
        }
        else {
            Write-Host ("  path {0,-24} NOT READY" -f $expected)
        }
    }
    if ($ready -eq $ExpectedPaths.Count) { Write-Host ("stream paths  : READY ({0}/{1})" -f $ready, $ExpectedPaths.Count) }
    else { Write-Host ("stream paths  : NOT READY ({0}/{1})" -f $ready, $ExpectedPaths.Count) }
}

function Wait-ForApi([int]$timeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($null -ne (Get-ApiPaths)) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Invoke-Install {
    Assert-Prerequisites
    if (-not (Test-IsAdmin)) {
        Initialize-Wrapper   # download without elevation so the UAC step stays short
        Backup-Config
        Invoke-Elevated "install"
        return
    }

    Initialize-Wrapper
    Backup-Config

    $svc = Get-ServiceOrNull
    if ($null -ne $svc) {
        Write-Warn "Service $ServiceName already exists - reconfiguring in place, not creating a second one."
        & $WrapperExe refresh $WrapperXml | Write-Host
    }
    else {
        # A manually started MediaMTX would collide with the service on every port.
        foreach ($p in Get-MediaMtxProcesses) {
            Write-Warn ("Stopping manually started MediaMTX (PID {0}) before installing the service." -f $p.Id)
            Stop-Process -Id $p.Id -Force
        }
        Start-Sleep -Seconds 1
        Write-Step "Installing service $ServiceName ..."
        & $WrapperExe install $WrapperXml | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "Service install failed with exit code $LASTEXITCODE." }
    }

    & sc.exe config $ServiceName start= delayed-auto | Out-Null
    Set-ScmRecovery
    Grant-ServiceControlToCurrentUser
    Write-Ok "Service $ServiceName installed."
    Show-Status
}

function Invoke-Start {
    $svc = Get-ServiceOrNull
    if ($null -eq $svc) {
        throw "Service $ServiceName is not installed. Run -Action install first."
    }
    if ($svc.Status -eq 'Running') {
        Write-Warn "Service already running - not starting a second instance."
    }
    else {
        $strays = Get-MediaMtxProcesses
        if ($strays.Count -gt 0) {
            throw ("Refusing to start the service: unmanaged mediamtx.exe already running (PIDs {0})." -f (($strays | ForEach-Object { $_.Id }) -join ", "))
        }
        Write-Step "Starting $ServiceName ..."
        Start-Service -Name $ServiceName
        (Get-Service $ServiceName).WaitForStatus('Running', '00:00:30')
    }
    if (-not (Wait-ForApi 40)) { Write-Warn "Control API did not answer within 40 s." }
    Write-PidFileFromService
    Show-Status
}

function Invoke-Stop {
    $svc = Get-ServiceOrNull
    if ($null -eq $svc) {
        Write-Warn "Service $ServiceName is not installed - nothing to stop."
        return
    }
    if ($svc.Status -eq 'Stopped') {
        Write-Warn "Service already stopped."
    }
    else {
        Write-Step "Stopping $ServiceName ..."
        Stop-Service -Name $ServiceName
        (Get-Service $ServiceName).WaitForStatus('Stopped', '00:00:30')
    }
    Remove-Item $PidFile -ErrorAction SilentlyContinue
    Show-Status
}

function Invoke-Restart {
    $svc = Get-ServiceOrNull
    if ($null -eq $svc) { throw "Service $ServiceName is not installed. Run -Action install first." }
    Write-Step "Restarting $ServiceName ..."
    Restart-Service -Name $ServiceName
    (Get-Service $ServiceName).WaitForStatus('Running', '00:00:30')
    if (-not (Wait-ForApi 40)) { Write-Warn "Control API did not answer within 40 s." }
    Write-PidFileFromService
    Show-Status
}

function Invoke-Uninstall {
    $svc = Get-ServiceOrNull
    if ($null -eq $svc) {
        Write-Warn "Service $ServiceName is not installed - nothing to uninstall."
        return
    }
    if (-not (Test-IsAdmin)) {
        Invoke-Elevated "uninstall"
        return
    }
    if ($svc.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName
        (Get-Service $ServiceName).WaitForStatus('Stopped', '00:00:30')
    }
    & $WrapperExe uninstall $WrapperXml | Write-Host
    Remove-Item $PidFile -ErrorAction SilentlyContinue
    Write-Ok "Service $ServiceName uninstalled (runtime logs kept)."
}

# An elevated run owns its own console window, so its output must survive on disk.
$transcript = $null
if ($Elevated) {
    New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
    $transcript = Join-Path $BackupDir ("service-{0}-{1}.log" -f $Action, (Get-Date -Format "yyyyMMdd-HHmmss"))
    Start-Transcript -Path $transcript -Force | Out-Null
}

try {
    switch ($Action) {
        'install' { Invoke-Install }
        'start' { Invoke-Start }
        'stop' { Invoke-Stop }
        'restart' { Invoke-Restart }
        'status' { Show-Status }
        'uninstall' { Invoke-Uninstall }
    }
}
finally {
    if ($transcript) { Stop-Transcript | Out-Null }
}
