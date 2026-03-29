namespace pulsenet.Services;

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;


internal static class SelfUpdateService
{
    private static readonly HttpClient _http = new();

    public static async Task ApplyAsync(UpdateInfo info, Action quit, ILogger? log = null)
    {
        if (IsMsiInstall() && !string.IsNullOrEmpty(info.MsiUrl))
        {
            log?.LogInformation("MSI install detected — updating via msiexec");
            await ApplyMsiAsync(info.MsiUrl, quit, log);
        }
        else
        {
            log?.LogInformation("Portable install detected — updating via exe-swap");
            await ApplyPortableAsync(info.DownloadUrl, quit, log);
        }
    }

    private static bool IsMsiInstall()
    {
        var exe  = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        var pf   = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return exe.StartsWith(pf,   StringComparison.OrdinalIgnoreCase)
            || exe.StartsWith(pf86, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ApplyMsiAsync(string msiUrl, Action quit, ILogger? log)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "pulsenet.exe");

        var tempDir = Path.Combine(Path.GetTempPath(), "pulsenet_update");
        Directory.CreateDirectory(tempDir);
        var tempMsi = Path.Combine(tempDir, "pulsenet_update.msi");

        log?.LogInformation("Downloading MSI from {Url}", msiUrl);
        using var response = await _http.GetAsync(msiUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var src  = await response.Content.ReadAsStreamAsync();
        await using var dest = File.Create(tempMsi);
        await src.CopyToAsync(dest);
        log?.LogInformation("MSI download complete — saved to {Msi}", tempMsi);

        // PowerShell script: run msiexec and wait for it to finish, then relaunch.
        // Exit codes 0 (success) and 3010 (success, reboot suggested) both mean the
        // install completed — relaunch in both cases since we pass /norestart.
        var script = $$"""
            $msi = '{{tempMsi}}'
            $exe = '{{currentExe}}'
            $p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /passive /norestart" -Wait -PassThru
            if ($p.ExitCode -eq 0 -or $p.ExitCode -eq 3010) {
                Start-Sleep -Seconds 1
                Start-Process $exe
            }
            """;

        var scriptPath = Path.Combine(tempDir, "update.ps1");
        await File.WriteAllTextAsync(scriptPath, script);
        log?.LogInformation("MSI update script written to {Script} — launching and quitting", scriptPath);

        Process.Start(new ProcessStartInfo
        {
            FileName        = "powershell.exe",
            Arguments       = $"-NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = true,
            WindowStyle     = ProcessWindowStyle.Hidden,
        });

        quit();
    }

    private static async Task ApplyPortableAsync(string exeUrl, Action quit, ILogger? log)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "pulsenet.exe");

        log?.LogInformation("Self-update: current exe = {Exe}", currentExe);

        var tempDir = Path.Combine(Path.GetTempPath(), "pulsenet_update");
        Directory.CreateDirectory(tempDir);
        var tempExe = Path.Combine(tempDir, "pulsenet.exe");

        log?.LogInformation("Downloading update from {Url}", exeUrl);
        using var response = await _http.GetAsync(exeUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var src  = await response.Content.ReadAsStreamAsync();
        await using var dest = File.Create(tempExe);
        await src.CopyToAsync(dest);
        log?.LogInformation("Download complete — saved to {Temp}", tempExe);

        var script = $"""
            Start-Sleep -Seconds 2
            Copy-Item -Force '{tempExe}' '{currentExe}'
            Start-Process '{currentExe}'
            """;

        var scriptPath = Path.Combine(tempDir, "update.ps1");
        await File.WriteAllTextAsync(scriptPath, script);
        log?.LogInformation("Update script written to {Script} — launching and quitting", scriptPath);

        Process.Start(new ProcessStartInfo
        {
            FileName        = "powershell.exe",
            Arguments       = $"-NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = true,
            WindowStyle     = ProcessWindowStyle.Hidden,
        });

        quit();
    }
}
