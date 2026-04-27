namespace pulsenet;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Settings;
using UI;
using System.Windows;

public partial class App : Application
{
    private IHost? _host;
    private TrayIcon? _trayIcon;
    private OverlayWindow? _overlay;
    private MiniBannerWindow? _banner;
    private GlobalHotkeyListener? _listener;
    private AudioSessionRenamer? _audioRenamer;
    private NowPlayingState? _nowPlaying;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        await _host.StartAsync();

        var settings = _host.Services.GetRequiredService<SettingsManager>();
        _listener    = _host.Services.GetRequiredService<GlobalHotkeyListener>();
        _nowPlaying  = _host.Services.GetRequiredService<NowPlayingState>();
        var browserSourceServer = _host.Services.GetRequiredService<BrowserSourceServer>();
        var listener     = _listener;
        var nowPlaying   = _nowPlaying;
        var overlayLogger = _host.Services.GetRequiredService<ILogger<OverlayWindow>>();

        // OverlayWindow must be created on the STA (UI) thread.
        // Pre-size to the fixed frame canvas. Position off-screen so the WebView2
        // HwndHost initialises invisibly, then snap to centre on first show.
        _overlay = new OverlayWindow(settings, listener, browserSourceServer, nowPlaying, overlayLogger);
        var overlay = _overlay;
        overlay.Width  = Constants.FrameDisplayWidth;
        overlay.Height = Constants.FrameDisplayHeight;
        overlay.Left   = -(Constants.FrameDisplayWidth + 100);
        overlay.Top    = 0;
        overlay.Show(); // WebView2 begins initialising in the background

        // Mini banner — minimised "now playing" tile in the lower-right.
        // Created up-front so its WebView2 is ready by the time the user first minimises.
        // Window stays Visible at an off-screen position; ShowBanner() snaps it to the
        // bottom-right, HideBanner() parks it off-screen again. Avoids the WebView2
        // init race that occurs when the window is collapsed before its first render.
        var bannerLogger = _host.Services.GetRequiredService<ILogger<MiniBannerWindow>>();
        _banner = new MiniBannerWindow(settings, bannerLogger);
        _banner.SetHotkeyLabel(settings.Current.ToggleHotkey.ToString());
        _banner.Show();
        var banner = _banner;

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
        _trayIcon.ResetWindowRequested += (_, _) => overlay.ResetWindow();

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

        // Hotkey → tri-state cycle. Banner-visible → restore overlay. Overlay-visible →
        // hide overlay and (in Banner mode) raise the banner. Both hidden → show overlay.
        listener.HotkeyPressed += (_, _) => Dispatcher.InvokeAsync(() =>
        {
            if (banner.IsBannerVisible)
            {
                banner.HideBanner();
                overlay.EnsureVisible();
                return;
            }
            if (overlay.IsOverlayVisible)
            {
                overlay.Toggle();
                if (settings.Current.MinimizeMode == MinimizeMode.Banner)
                    banner.ShowBanner();
            }
            else
            {
                overlay.Toggle();
            }
        });

        overlay.BalloonTipRequested += (title, msg) => _trayIcon.ShowBalloon(title, msg);

        // NowPlayingState is the single source of truth — both the in-app mini
        // banner and the BrowserSourceServer's SSE stream subscribe to it.
        overlay.NowPlayingChanged += t => nowPlaying.SetTitle(t);
        overlay.StationChanged    += s => nowPlaying.SetStation(s);
        nowPlaying.Changed += snapshot => Dispatcher.Invoke(() =>
        {
            banner.SetTitle(snapshot.Title);
            banner.SetStation(snapshot.Station);
        });

        // Miniplayer Settings sub-panel → banner control
        overlay.BannerEditModeChanged += on => Dispatcher.Invoke(() => banner.SetEditMode(on));
        overlay.BannerLockChanged     += on => Dispatcher.Invoke(() => banner.SetLocked(on));
        overlay.BannerOpacityChanged  += v  => Dispatcher.Invoke(() => banner.SetOpacity(v));
        overlay.BannerScaleChanged    += pct => Dispatcher.Invoke(() => banner.SetScale(pct));
        overlay.BannerResetRequested  += () => Dispatcher.Invoke(banner.ResetPosition);

        // Whenever the overlay is hidden, ensure the banner exits edit mode so it
        // returns to click-through. Avoids a stale interactable banner if the user
        // hides the overlay while the Miniplayer Settings panel was open.
        overlay.OverlayHidden += (_, _) => Dispatcher.Invoke(() => banner.SetEditMode(false));

        // Keep the banner's hotkey label in sync if the user remaps the hotkey,
        // and pull the banner down immediately if the user switches to Tray mode
        // while the banner is showing.
        settings.SettingsChanged += (_, s) => Dispatcher.Invoke(() =>
        {
            banner.SetHotkeyLabel(s.ToggleHotkey.ToString());
            if (s.MinimizeMode == MinimizeMode.Tray && banner.IsBannerVisible)
                banner.HideBanner();
        });
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
        services.AddSingleton<NowPlayingState>();

        // Singletons exposed for direct resolution; also registered as hosted services so
        // the Generic Host starts and stops them automatically.
        services.AddSingleton<GlobalHotkeyListener>();
        services.AddHostedService(p => p.GetRequiredService<GlobalHotkeyListener>());

        services.AddSingleton<BrowserSourceServer>();
        services.AddHostedService(p => p.GetRequiredService<BrowserSourceServer>());
    }
}
