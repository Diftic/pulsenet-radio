# Builds the MSI installer locally. Mirrors the GitHub Actions workflow
# (.github/workflows/build.yml) so dev iteration produces an MSI identical to
# what a real release would. Run from project root in an elevated PowerShell.
#
# Output: PulseNet-Setup.msi at the project root.

[CmdletBinding()]
param(
    [string] $Version = '2.0.0',
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $root
try {
    Write-Host "==> Publishing player (v$Version)"
    dotnet publish src\pulsenet.csproj `
        -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=$Version `
        -o artifacts\player
    if ($LASTEXITCODE -ne 0) { throw "Player publish failed" }

    Write-Host "==> Publishing hotkey helper (v$Version)"
    dotnet publish src\PulseNetHotkeyService\PulseNetHotkeyService.csproj `
        -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=$Version `
        -o artifacts\helper
    if ($LASTEXITCODE -ne 0) { throw "Helper publish failed" }

    Write-Host "==> Staging payload"
    Remove-Item installer\stage -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path installer\stage -Force | Out-Null
    Copy-Item artifacts\player\*                              installer\stage\ -Recurse -Force
    Copy-Item artifacts\helper\PulseNetHotkeyService.exe      installer\stage\ -Force
    Copy-Item scripts\install-task.ps1                        installer\stage\ -Force
    Copy-Item scripts\uninstall-task.ps1                      installer\stage\ -Force
    Copy-Item src\Assets\icon.ico                             installer\ -Force

    Write-Host "==> Building MSI"
    Push-Location installer
    try {
        wix extension add WixToolset.UI.wixext/5.0.2 | Out-Null
        wix build installer.wxs `
            -ext WixToolset.UI.wixext `
            -o ..\PulseNet-Setup.msi `
            -arch x64 `
            -define ProductVersion=$Version
        if ($LASTEXITCODE -ne 0) { throw "WiX build failed" }
    }
    finally {
        Pop-Location
    }

    Write-Host ""
    Write-Host "Done. MSI: $root\PulseNet-Setup.msi"
    Get-Item PulseNet-Setup.msi | Format-List Name, Length, LastWriteTime
}
finally {
    Pop-Location
}
