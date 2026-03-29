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
    private SettingsWindow? _settingsWindow;
    private OverlayWindow? _overlay;
    private GlobalHotkeyListener? _listener;

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
        // Pre-size to full primary screen so the WebView2 HwndHost initialises
        // at the correct size. Position off-screen so it doesn't flash until
        // the user toggles the overlay.
        var sw = SystemParameters.PrimaryScreenWidth;
        var sh = SystemParameters.PrimaryScreenHeight;
        _overlay = new OverlayWindow(settings, overlayLogger);
        var overlay = _overlay;
        overlay.Width  = sw;
        overlay.Height = sh;
        overlay.Left   = -sw;
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

        // First hotkey press closes the splash; subsequent presses just toggle.
        void OnFirstHotkey(object? s, EventArgs _)
        {
            // InvokeAsync — hook thread must not block waiting for the UI thread.
            Dispatcher.InvokeAsync(splash.Close);
            listener.HotkeyPressed -= OnFirstHotkey;
        }
        listener.HotkeyPressed += OnFirstHotkey;

        // Hotkey → toggle overlay; tray icon reflects overlay state
        listener.HotkeyPressed      += (_, _) => Dispatcher.InvokeAsync(overlay.Toggle);
        overlay.SettingsRequested   += (_, _) => Dispatcher.Invoke(() => OpenSettings(settings));
        overlay.OverlayShown        += (_, _) => _trayIcon.SetActive(true);
        overlay.OverlayHidden       += (_, _) => _trayIcon.SetActive(false);
        overlay.BalloonTipRequested += (title, msg) => _trayIcon.ShowBalloon(title, msg);

        // ESC closes the overlay.
        listener.EscapePressed += (_, _) => Dispatcher.InvokeAsync(overlay.EnsureHiddenRestoring);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();

        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private void OpenSettings(SettingsManager settings)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        // Pause hotkey listener so key captures in the settings hotkey field
        // don't accidentally toggle the overlay or fire ESC-close.
        if (_listener is not null)
            _listener.Paused = true;

        // Drop overlay out of topmost so the settings window sits above it.
        // WebView2's DirectComposition surface otherwise renders above all other windows.
        if (_overlay is not null)
            _overlay.Topmost = false;

        Action<byte>?    preview         = _overlay is not null ? _overlay.ApplyOpacity           : null;
        Action<byte>?    bgPreview       = _overlay is not null ? _overlay.ApplyBackgroundOpacity  : null;
        Action<int,int>? sizePreview     = _overlay is not null ? _overlay.ApplySize               : null;
        Action<int>?     zoomPreview     = _overlay is not null ? _overlay.ApplyZoom               : null;
        _settingsWindow = new SettingsWindow(settings, preview, bgPreview, sizePreview, zoomPreview);
        _settingsWindow.Closed += (_, _) =>
        {
            if (_listener is not null)
                _listener.Paused = false;
            if (_overlay is not null)
                _overlay.Topmost = true;
        };
        _settingsWindow.Show();
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

        services.AddHostedService<AutoStartManager>();
    }
}
