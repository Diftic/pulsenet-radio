# Registers the PulseNet Hotkey Helper as a Scheduled Task that fires at user
# logon with highest privileges (elevated, no UAC prompt). This pivots away
# from the Windows Service approach because SYSTEM services run in Session 0
# and their WH_KEYBOARD_LL hooks never see Session 1 user input. The task
# launches the same PulseNetHotkeyService.exe but inside the user's session,
# elevated, where the hook actually sees keystrokes.
#
# Run from an elevated PowerShell. Per-user task by default; pass -UserId to
# target a different account on multi-user machines.

#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string] $TaskName = 'PulseNetHotkey',
    [string] $BinPath  = (Join-Path $PSScriptRoot '..\src\PulseNetHotkeyService\bin\x64\Debug\net9.0-windows\win-x64\PulseNetHotkeyService.exe'),
    [string] $UserId   = "$env:USERDOMAIN\$env:USERNAME"
)

$ErrorActionPreference = 'Stop'

$BinPath = (Resolve-Path -LiteralPath $BinPath).Path
Write-Host "Registering scheduled task '$TaskName' -> $BinPath (user $UserId)"

# Replace any prior install in place so re-runs after a rebuild don't conflict.
$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Existing task found; removing"
    if ((Get-ScheduledTask -TaskName $TaskName).State -eq 'Running') {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    }
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

$action = New-ScheduledTaskAction -Execute $BinPath

# Trigger at logon for the target user. AtStartup is system-wide and runs before
# the user session exists - the helper needs the interactive session to install
# a hook that sees user input, so logon is the right boundary.
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $UserId

# Interactive + Highest = runs in the user's interactive session, elevated, no
# UAC prompt. The task itself was created with admin consent so subsequent
# logons just fire silently.
$principal = New-ScheduledTaskPrincipal -UserId $UserId -LogonType Interactive -RunLevel Highest

# Defaults are unfriendly: tasks stop after 72 hours, won't restart on battery,
# don't run if the trigger was missed. Override for an always-on helper.
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

Register-ScheduledTask -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
    -Description 'Forwards configured hotkey presses to PulseNet Player. Runs elevated so user-mode keyboard hooks reach game-IL windows on Windows 11 26200+.' `
    -Force | Out-Null

Write-Host "Starting task now so we can test without logging out + in"
Start-ScheduledTask -TaskName $TaskName

Start-Sleep -Seconds 1
$state = (Get-ScheduledTask -TaskName $TaskName).State
Write-Host "Task '$TaskName' state: $state"
Write-Host "Done. Inspect with: Get-ScheduledTask $TaskName | Get-ScheduledTaskInfo"
