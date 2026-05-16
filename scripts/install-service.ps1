# Installs PulseNetHotkey as a Windows Service for development testing.
# G4 will replace this with a WiX MSI ServiceInstall element for production.
# Run from an elevated PowerShell. Defaults to the Debug build's exe path;
# pass -BinPath to point at a Release or installed location instead.

#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string] $ServiceName = 'PulseNetHotkey',
    [string] $BinPath     = (Join-Path $PSScriptRoot '..\src\PulseNetHotkeyService\bin\x64\Debug\net9.0-windows\win-x64\PulseNetHotkeyService.exe')
)

$ErrorActionPreference = 'Stop'

$BinPath = (Resolve-Path -LiteralPath $BinPath).Path
Write-Host "Installing service '$ServiceName' -> $BinPath"

# Stop + delete any prior install before re-creating, so re-runs after a rebuild
# don't fail with "service already exists" or point at a stale path.
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Existing service found; stopping + deleting"
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    }
    & sc.exe delete $ServiceName | Out-Host
    Start-Sleep -Seconds 1
}

& sc.exe create $ServiceName binPath= "`"$BinPath`"" start= auto DisplayName= "PulseNet Hotkey Helper" | Out-Host
& sc.exe description $ServiceName "Forwards configured hotkey presses to PulseNet Player. Runs elevated so user-mode keyboard hooks reach game-IL windows on Windows 11 26200+." | Out-Host
& sc.exe failure $ServiceName reset= 60 actions= restart/5000/restart/5000/restart/5000 | Out-Host

Write-Host "Starting service"
& sc.exe start $ServiceName | Out-Host

Write-Host "Done. Verify with: Get-Service $ServiceName"
