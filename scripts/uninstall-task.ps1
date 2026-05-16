# Stops and removes the PulseNet Hotkey Helper scheduled task. Pairs with
# install-task.ps1.

#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string] $TaskName = 'PulseNetHotkey'
)

$ErrorActionPreference = 'Stop'

$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Task '$TaskName' is not registered."
    return
}

if ($existing.State -eq 'Running') {
    Write-Host "Stopping running task"
    Stop-ScheduledTask -TaskName $TaskName
    # Stop-ScheduledTask returns before the process exits; give it a moment to
    # release its exe handle so a subsequent rebuild doesn't get locked out.
    Start-Sleep -Seconds 1
}

# In case the helper exe was started by the task but didn't exit when the task
# stopped (e.g., user pressed Ctrl+C inside the task's process scope), terminate
# any straggler so the exe handle releases.
Get-Process -Name 'PulseNetHotkeyService' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
Write-Host "Task '$TaskName' removed."
