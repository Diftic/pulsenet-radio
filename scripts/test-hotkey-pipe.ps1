# G1 smoke-test client for PulseNetHotkeyService.
# Run from a normal (non-elevated) PowerShell while the helper is running
# elevated in another window. Connects to the helper's pipe, registers F9
# (vk=120) as the watched hotkey, then prints every hotkey event the helper
# pushes. Ctrl+C to exit.

param(
    [int[]] $VkCodes = @(120)  # default: F9
)

$ErrorActionPreference = 'Stop'

$pipe = New-Object System.IO.Pipes.NamedPipeClientStream '.', 'PulseNetHotkey', 'InOut'
try {
    Write-Host "Connecting to \\.\pipe\PulseNetHotkey..."
    $pipe.Connect(5000)
    Write-Host "Connected."

    $writer = New-Object System.IO.StreamWriter $pipe
    $reader = New-Object System.IO.StreamReader $pipe

    $vkList = ($VkCodes -join ',')
    $payload = '{"type":"setKeys","vkCodes":[' + $vkList + ']}'
    $writer.WriteLine($payload)
    $writer.Flush()
    Write-Host "Sent setKeys with vkCodes=[$vkList]"
    Write-Host "Watching for hotkey events. Press the configured key from any window. Ctrl+C to exit."

    while ($true) {
        $line = $reader.ReadLine()
        if ($null -eq $line) {
            Write-Host "Pipe closed by server."
            break
        }
        Write-Host "<- $line"
    }
}
finally {
    $pipe.Dispose()
}
