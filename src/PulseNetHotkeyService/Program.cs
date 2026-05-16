namespace PulseNetHotkeyService;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;

/// <summary>
/// Entry point for PulseNet's hotkey helper. Hosts <see cref="HookService"/>
/// under .NET Generic Host. AddWindowsService auto-detects the host context:
/// when launched by the Windows Service Control Manager, registers as a
/// service (runs as SYSTEM, no console); when launched from a developer
/// console, runs as a normal console app with the same code path. Same
/// binary serves both modes, no separate dev/prod build.
/// </summary>
internal static class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "PulseNetHotkey";
        });

        builder.Services.AddHostedService<HookService>();

        // Logging: console sink stays available for developer console runs.
        // Event Log sink kicks in when running under the SCM (no console
        // attached); failure to write to the Event Log under console mode is
        // expected and the EventLogLoggerProvider already short-circuits.
        builder.Logging.AddEventLog(settings =>
        {
            settings.SourceName = "PulseNetHotkey";
        });

        builder.Build().Run();
    }
}
