namespace pulsenet;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Services;
using Settings;
using UI;
using System.Windows;

public partial class App : Application
{
    private IHost? _host;
    private TrayIcon? _trayIcon;
    private OverlayWindow? _overlay;
    private GlobalHotkeyListener? _listener;
    private AudioSessionRenamer? _audioRenamer;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        await _host.StartAsync();

        var settings = _host.Services.GetRequiredService<SettingsManager>();
        _listener    = _host.Services.GetRequiredService<GlobalHotkeyListener>();
        var listener = _listener;
        var overlayLogger = _host.Services.GetRequiredService<ILogger<OverlayWindow>>();

        // OverlayWindow must be created on the STA (UI) thread.
        // Pre-size to the fixed frame canvas. Position off-screen so the WebView2
        // HwndHost initialises invisibly, then snap to centre on first show.
        _overlay = new OverlayWindow(settings, listener, overlayLogger);
        var overlay = _overlay;
        overlay.Width  = Constants.FrameDisplayWidth;
        overlay.Height = Constants.FrameDisplayHeight;
        overlay.Left   = -(Constants.FrameDisplayWidth + 100);
        overlay.Top    = 0;
        overlay.Show(); // WebView2 begins initialising in the background

        // Check for updates while WebView2 loads; splash waits for the result.
        var updateLog  = _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("UpdateChecker");
        var splashLog  = _host.Services.GetRequiredService<ILogger<UI.SplashWindow>>();
        var updateInfo = await Services.UpdateChecker.CheckAsync(updateLog);

        var splash = new UI.SplashWindow(splashLog);
        splash.SetHotkeyLabel(settings.Current.ToggleHotkey.ToString());
        if (updateInfo.HasUpdate)
            splash.ShowUpdateBanner(updateInfo);
        splash.Show();

        // Tray icon
        _trayIcon = new TrayIcon();
        _trayIcon.ExitRequested += (_, _) => Dispatcher.Invoke(Shutdown);

        // Surface a previously-failed MSI update: if the last auto-update attempt
        // wrote a failure status, show a tray balloon and expose a manual retry.
        // Clearing the status prevents repeat warnings on later launches.
        var lastUpdate = Services.SelfUpdateService.ReadLastResult();
        if (lastUpdate is { Success: false })
        {
            _trayIcon.SetPendingUpdateRetry(lastUpdate.MsiPath);
            _trayIcon.ShowBalloon(
                "Update didn't install",
                $"msiexec returned {lastUpdate.ExitCode}. Right-click the tray icon → Retry failed update.",
                System.Windows.Forms.ToolTipIcon.Warning);
            Services.SelfUpdateService.ClearLastResult();
        }

        // Rename WebView2 audio sessions so they show as "PulseNet Player"
        // in Volume Mixer / Sonar / Wavelink instead of "msedgewebview2".
        var audioLog = _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AudioSessionRenamer");
        var exePath  = Environment.ProcessPath ?? string.Empty;
        _audioRenamer = new AudioSessionRenamer(audioLog, "PulseNet Player", $"{exePath},0");
        _audioRenamer.Start();

        // First hotkey press closes the splash; subsequent presses just toggle.
        void OnFirstHotkey(object? s, EventArgs _)
        {
            Dispatcher.InvokeAsync(splash.Close);
            listener.HotkeyPressed -= OnFirstHotkey;
        }
        listener.HotkeyPressed += OnFirstHotkey;

        // Hotkey → toggle overlay; tray icon reflects overlay state
        listener.HotkeyPressed      += (_, _) => Dispatcher.InvokeAsync(overlay.Toggle);
        overlay.OverlayShown        += (_, _) => _trayIcon.SetActive(true);
        overlay.OverlayHidden       += (_, _) => _trayIcon.SetActive(false);
        overlay.BalloonTipRequested += (title, msg) => _trayIcon.ShowBalloon(title, msg);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _audioRenamer?.Dispose();
        _trayIcon?.Dispose();

        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(b => b
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<SettingsManager>();

        // Singletons exposed for direct resolution; also registered as hosted services so
        // the Generic Host starts and stops them automatically.
        services.AddSingleton<GlobalHotkeyListener>();
        services.AddHostedService(p => p.GetRequiredService<GlobalHotkeyListener>());

    }
}
