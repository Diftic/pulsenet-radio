# Removes the PulseNetHotkey Windows Service. Pairs with install-service.ps1.
# Stops the service first so the exe handle releases before the SCM deletes it.

#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string] $ServiceName = 'PulseNetHotkey'
)

$ErrorActionPreference = 'Stop'

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service '$ServiceName' is not installed."
    return
}

if ($existing.Status -ne 'Stopped') {
    Write-Host "Stopping service"
    Stop-Service -Name $ServiceName -Force
}

& sc.exe delete $ServiceName | Out-Host
Write-Host "Service '$ServiceName' removed."
