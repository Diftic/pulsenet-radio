# Stops and removes the PulseNet Hotkey Helper scheduled task. Pairs with
# install-task.ps1.

#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string] $TaskName = 'PulseNetHotkey'
)

$ErrorActionPreference = 'Stop'

$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing -and $existing.State -eq 'Running') {
    Write-Host "Stopping running task"
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
}

# Kill the helper process(es) regardless of whether the task existed - the MSI
# uninstall path needs the exe handle released before RemoveFiles can succeed,
# and a manually-launched dev helper from an earlier session may still hold it.
$procs = Get-Process -Name 'PulseNetHotkeyService' -ErrorAction SilentlyContinue
if ($procs) {
    Write-Host "Terminating $($procs.Count) helper process(es)"
    $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    # Wait-Process blocks until the OS reaps the process. Without this the exe
    # file handle can linger briefly into the next MSI step and lock RemoveFiles.
    try { Wait-Process -Name 'PulseNetHotkeyService' -Timeout 5 -ErrorAction SilentlyContinue } catch {}
    Start-Sleep -Milliseconds 500
}

if ($existing) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Task '$TaskName' removed."
} else {
    Write-Host "Task '$TaskName' was not registered (helper process cleaned up only)."
}
