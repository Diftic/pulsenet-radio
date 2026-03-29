namespace pulsenet.UI;

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Models;
using Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

public partial class OverlayWindow : Window
{
    private readonly SettingsManager _settings;
    private readonly ILogger<OverlayWindow> _logger;

    private HWND _hwnd;
    private HWND _chromeHwnd;  // cached WebView2 Chrome child HWND for scroll forwarding
    private HWND _prevFgHwnd;  // foreground window captured just before we steal it

    // Global low-level mouse hook — intercepts WM_MOUSEWHEEL regardless of focus
    private UnhookWindowsHookExSafeHandle? _mouseHookHandle;
    private HHOOK _mouseHookId = HHOOK.Null;
    private HOOKPROC? _mouseHookProc;
    private Thread? _mouseHookThread;
    private uint _mouseHookThreadId;

    private CoreWebView2Environment? _env;
    private bool _webViewReady;
    private bool _isVisible;
    private bool _navigationErrorShown;
    private double _opacity  = 1.0;
    private int    _zoomPct  = 100;
    private string _loadedChannelId = string.Empty;

    public event EventHandler? OverlayShown;
    public event EventHandler? OverlayHidden;
    public event EventHandler? SettingsRequested;
    public event Action<string, string>? BalloonTipRequested;

    public OverlayWindow(
        SettingsManager settings,
        ILogger<OverlayWindow> logger)
    {
        _settings = settings;
        _logger = logger;

        InitializeComponent();

        _settings.SettingsChanged += OnSettingsChanged;
    }

    public void Toggle()
    {
        if (_isVisible)
            HideOverlay(restoreFocus: true);
        else
            ShowOverlay();
    }

    public bool IsOverlayVisible => _isVisible;

    public void EnsureVisible()
    {
        if (!_isVisible)
            ShowOverlay();
    }

    /// <summary>
    /// Hides without restoring foreground — used when an external app already took foreground.
    /// </summary>
    public void EnsureHidden()
    {
        if (_isVisible)
            HideOverlay(restoreFocus: false);
    }

    /// <summary>
    /// Hides and explicitly returns foreground to the previous window — used for ESC-close.
    /// </summary>
    public void EnsureHiddenRestoring()
    {
        if (_isVisible)
            HideOverlay(restoreFocus: true);
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new HWND(new WindowInteropHelper(this).Handle);

        // WndProc hook — catches WM_MOUSEWHEEL when the overlay window itself has focus.
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);

        // Global WH_MOUSE_LL hook — intercepts WM_MOUSEWHEEL regardless of which window
        // has focus, so scrolling works even when a game holds the foreground lock.
        _mouseHookProc = MouseHookProc;
        _mouseHookThread = new Thread(RunMouseHook) { IsBackground = true, Name = "MouseHook" };
        _mouseHookThread.Start();

        // Hide from Alt+Tab.  Activation prevention is handled in WndProc via
        // WM_MOUSEACTIVATE → MA_NOACTIVATE, and in XAML via ShowActivated="False".
        var exStyle = PInvoke.GetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLong(
            _hwnd,
            WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            exStyle | (int)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW);

        ApplyOpacity(_settings.Current.OverlayOpacity);
        ApplyBackgroundOpacity(_settings.Current.BackgroundOpacity);
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await InitializeWebViewAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _settings.SettingsChanged -= OnSettingsChanged;

        if (_mouseHookThreadId != 0)
            PInvoke.PostThreadMessage(_mouseHookThreadId, PInvoke.WM_QUIT, 0, 0);
        _mouseHookHandle?.Dispose();

        base.OnClosed(e);
    }

    // -------------------------------------------------------------------------
    // Show / Hide
    // -------------------------------------------------------------------------

    private void ShowOverlay()
    {
        if (!_webViewReady)
        {
            _logger.LogDebug("Overlay toggled before WebView2 was ready — ignoring");
            return;
        }

        CoverPrimaryScreen();

        _isVisible = true;
        Visibility = Visibility.Visible;
        UpdateLayout();

        StealForeground();

        // Re-inject styles every time the overlay is shown.
        _ = WebView.CoreWebView2.ExecuteScriptAsync(BuildOverlayStyleScript(_opacity, _zoomPct));
        _ = WebView.CoreWebView2.ExecuteScriptAsync(BuildSettingsButtonScript());

        OverlayShown?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("Overlay shown");
    }

    private void HideOverlay(bool restoreFocus)
    {
        _isVisible = false;
        Visibility = Visibility.Collapsed;

        if (restoreFocus && _prevFgHwnd != HWND.Null)
            PInvoke.SetForegroundWindow(_prevFgHwnd);

        OverlayHidden?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("Overlay hidden (restoreFocus={R})", restoreFocus);
    }

    // -------------------------------------------------------------------------
    // Settings hot-reload
    // -------------------------------------------------------------------------

    public void ApplyBackgroundOpacity(byte value)
    {
        OverlayBg.Opacity = Math.Max((byte)1, value) / 255.0;
        _logger.LogDebug("ApplyBackgroundOpacity: byte={Value}", value);
    }

    private void OnSettingsChanged(object? sender, PulsenetSettings settings)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyOpacity(settings.OverlayOpacity);
            ApplyBackgroundOpacity(settings.BackgroundOpacity);
            ApplyZoom(settings.WebViewZoomPct);
            ApplySize(settings.WebViewWidthPct, settings.WebViewHeightPct);

            if (_webViewReady && _loadedChannelId != settings.YoutubeChannelId)
                RestartNavigation(BuildPlayerUrl(settings.YoutubeChannelId));
        });
    }

    private void RestartNavigation(string url)
    {
        _webViewReady = false;
        _navigationErrorShown = false;
        _chromeHwnd = HWND.Null;
        WebView.CoreWebView2.Stop();
        WebView.CoreWebView2.Navigate(url);
        _logger.LogInformation("WebView2 restarted — navigating to {Url}", url);
    }

    private string BuildPlayerUrl(string channelId)
    {
        _loadedChannelId = channelId;
        if (string.IsNullOrEmpty(channelId))
            return $"https://{Constants.PlayerVirtualHost}/index.html";
        return $"https://{Constants.PlayerVirtualHost}/index.html?channelId={Uri.EscapeDataString(channelId)}";
    }

    // -------------------------------------------------------------------------
    // Focus helpers
    // -------------------------------------------------------------------------

    private unsafe void StealForeground()
    {
        var fg = PInvoke.GetForegroundWindow();

        if (fg != HWND.Null && !IsOurProcess(fg))
            _prevFgHwnd = fg;

        uint fgPid;
        var fgThread = PInvoke.GetWindowThreadProcessId(fg, &fgPid);
        var uiThread = PInvoke.GetCurrentThreadId();

        bool attached = fgThread != 0 && fgThread != uiThread
            && PInvoke.AttachThreadInput(fgThread, uiThread, true);

        PInvoke.BringWindowToTop(_hwnd);

        if (attached)
            PInvoke.AttachThreadInput(fgThread, uiThread, false);

        _logger.LogDebug("StealForeground: prevFg={Fg:X} attached={Att}", (nint)_prevFgHwnd.Value, attached);
    }

    private static unsafe bool IsOurProcess(HWND hwnd)
    {
        uint pid;
        PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        return pid == (uint)Environment.ProcessId;
    }

    private unsafe void ForceToForeground()
    {
        var fgHwnd   = PInvoke.GetForegroundWindow();
        uint pid;
        var fgThread = PInvoke.GetWindowThreadProcessId(fgHwnd, &pid);
        var uiThread = PInvoke.GetCurrentThreadId();

        _logger.LogDebug(
            "ForceToForeground: fgHwnd={Fg:X} fgThread={FgT} uiThread={UiT}",
            (nint)fgHwnd.Value, fgThread, uiThread);

        var attached = fgThread != uiThread
            && PInvoke.AttachThreadInput(fgThread, uiThread, true);

        var fgResult    = PInvoke.SetForegroundWindow(_hwnd);
        var topResult   = PInvoke.BringWindowToTop(_hwnd);
        var focusResult = PInvoke.SetFocus(_hwnd);

        _logger.LogDebug(
            "ForceToForeground: attached={Att} SetForeground={Fg} BringToTop={Top} SetFocus={Foc}",
            attached, (bool)fgResult, (bool)topResult, focusResult != HWND.Null);

        if (attached)
            PInvoke.AttachThreadInput(fgThread, uiThread, false);
    }

    // -------------------------------------------------------------------------
    // Opacity — inject CSS opacity on the html element.
    // Window.Opacity and SetLayeredWindowAttributes both fail for WebView2
    // because its DirectComposition surface bypasses the host window's alpha.
    // CSS opacity runs inside Chromium's GPU compositor and correctly blends
    // the page (including its background) against the transparent WPF window.
    // -------------------------------------------------------------------------

    public void ApplySize(int widthPct, int heightPct)
    {
        widthPct  = Math.Clamp(widthPct,  10, 100);
        heightPct = Math.Clamp(heightPct, 10, 100);

        if (widthPct == 100 && heightPct == 100)
        {
            WebView.ClearValue(FrameworkElement.WidthProperty);
            WebView.ClearValue(FrameworkElement.HeightProperty);
            WebView.HorizontalAlignment = HorizontalAlignment.Stretch;
            WebView.VerticalAlignment   = VerticalAlignment.Stretch;
            WebView.Margin = new Thickness(0);
        }
        else
        {
            WebView.HorizontalAlignment = HorizontalAlignment.Center;
            WebView.VerticalAlignment   = VerticalAlignment.Center;
            WebView.Width  = ActualWidth  * widthPct  / 100.0;
            WebView.Height = ActualHeight * heightPct / 100.0;
            WebView.Margin = new Thickness(0);
        }

        _logger.LogDebug("ApplySize: {W}% × {H}%", widthPct, heightPct);
    }

    public void ApplyOpacity(byte value)
    {
        _opacity = value / 255.0;
        _logger.LogDebug("ApplyOpacity: byte={Value} → {Opacity:F3}", value, _opacity);
        if (_webViewReady)
            _ = WebView.CoreWebView2.ExecuteScriptAsync(BuildOverlayStyleScript(_opacity, _zoomPct));
    }

    public void ApplyZoom(int zoomPct)
    {
        _zoomPct = Math.Clamp(zoomPct, 10, 200);
        _logger.LogDebug("ApplyZoom: {Zoom}%", _zoomPct);
        if (_webViewReady)
            _ = WebView.CoreWebView2.ExecuteScriptAsync(BuildOverlayStyleScript(_opacity, _zoomPct));
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (e.TryGetWebMessageAsString() == "open-settings")
            Dispatcher.Invoke(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
    }

    private static string BuildOverlayStyleScript(double opacity, int zoomPct) =>
        $"(function(){{" +
        $"var s=document.getElementById('__ol_style');" +
        $"if(!s){{s=document.createElement('style');s.id='__ol_style';" +
        $"(document.head||document.documentElement).appendChild(s);}}" +
        $"s.textContent='html,body{{background:transparent!important}}" +
        $"html{{opacity:{opacity:F3}!important;zoom:{zoomPct}%!important}}';" +
        $"}})();";

    private static string BuildSettingsButtonScript() =>
        "(function(){" +
        "if(document.getElementById('__ol_settings_btn'))return;" +
        "var b=document.createElement('div');" +
        "b.id='__ol_settings_btn';" +
        "b.textContent='\u2699';" +
        "b.style.cssText='position:fixed;bottom:14px;right:14px;width:108px;height:108px;" +
            "background:rgba(0,0,0,0.45);color:red;font-size:60px;" +
            "display:flex;align-items:center;justify-content:center;" +
            "border-radius:18px;cursor:pointer;z-index:2147483647;" +
            "opacity:0.6;transition:opacity 0.15s;user-select:none;';" +
        "b.addEventListener('mouseover',function(){b.style.opacity='1';});" +
        "b.addEventListener('mouseout',function(){b.style.opacity='0.4';});" +
        "b.addEventListener('click',function(){window.chrome.webview.postMessage('open-settings');});" +
        "document.body.appendChild(b);" +
        "})();";

    // -------------------------------------------------------------------------
    // Global mouse hook — WH_MOUSE_LL intercepts WM_MOUSEWHEEL system-wide.
    // -------------------------------------------------------------------------

    private void RunMouseHook()
    {
        _mouseHookThreadId = PInvoke.GetCurrentThreadId();

        var handle = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _mouseHookProc!, default, 0);
        if (handle.IsInvalid)
        {
            _logger.LogWarning("Failed to install mouse hook");
            return;
        }

        _mouseHookHandle = handle;
        _mouseHookId = new HHOOK(handle.DangerousGetHandle());
        _logger.LogInformation("Mouse hook installed");

        while (PInvoke.GetMessage(out var msg, HWND.Null, 0, 0))
        {
            PInvoke.TranslateMessage(in msg);
            PInvoke.DispatchMessage(in msg);
        }
    }

    private LRESULT MouseHookProc(int nCode, WPARAM wparam, LPARAM lparam)
    {
        const uint WM_MOUSEWHEEL = 0x020A;

        if (nCode >= 0 && (uint)wparam.Value == WM_MOUSEWHEEL && _isVisible && _webViewReady)
        {
            var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lparam);

            short delta = (short)(hs.mouseData >> 16);
            int scrollPx = -(delta / 120) * 100;

            int cx = hs.pt.X;
            int cy = hs.pt.Y;

            _logger.LogDebug("MouseHook scroll: delta={D} px={P} at ({X},{Y})", delta, scrollPx, cx, cy);

            Dispatcher.InvokeAsync(() =>
            {
                if (!_webViewReady) return;

                var script =
                    $"(function(){{" +
                    $"var el=document.elementFromPoint({cx},{cy});" +
                    $"while(el&&el!==document.documentElement){{" +
                    $"var s=window.getComputedStyle(el);" +
                    $"var ov=s.overflowY;" +
                    $"if((ov==='auto'||ov==='scroll')&&el.scrollHeight>el.clientHeight){{" +
                    $"el.scrollTop+={scrollPx};return;" +
                    $"}}" +
                    $"el=el.parentElement;}}" +
                    $"window.scrollBy(0,{scrollPx});" +
                    $"}})();";

                _ = WebView.CoreWebView2.ExecuteScriptAsync(script);
            });

            return (LRESULT)1;
        }

        return PInvoke.CallNextHookEx(_mouseHookId, nCode, wparam, lparam);
    }

    // -------------------------------------------------------------------------
    // Scroll forwarding via WndProc
    // -------------------------------------------------------------------------

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEWHEEL = 0x020A;

        if (msg == WM_MOUSEWHEEL && _webViewReady)
        {
            if (_chromeHwnd == HWND.Null)
                _chromeHwnd = FindChromeChildHwnd(_hwnd);

            if (_chromeHwnd != HWND.Null)
            {
                PInvoke.PostMessage(_chromeHwnd, (uint)WM_MOUSEWHEEL, (nuint)(nint)wParam, (nint)lParam);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private static unsafe HWND FindChromeChildHwnd(HWND parent)
    {
        HWND found = HWND.Null;
        var sb = new System.Text.StringBuilder(256);

        PInvoke.EnumChildWindows(parent, (child, _) =>
        {
            sb.Clear();
            GetWindowClassName((nint)child.Value, sb, sb.Capacity);
            if (sb.ToString().Contains("Chrome", StringComparison.OrdinalIgnoreCase))
            {
                found = child;
                return false;
            }
            return true;
        }, 0);

        return found;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetWindowClassName(nint hwnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    // -------------------------------------------------------------------------
    // Positioning
    // -------------------------------------------------------------------------

    private void CoverPrimaryScreen()
    {
        var sw = SystemParameters.PrimaryScreenWidth;
        var sh = SystemParameters.PrimaryScreenHeight;
        Width  = sw;
        Height = sh;
        Left   = 0;
        Top    = 0;
    }

    // -------------------------------------------------------------------------
    // WebView2
    // -------------------------------------------------------------------------

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var cacheFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Constants.AppDataFolderName,
                Constants.WebView2CacheFolderName);

            WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0, 0, 0, 0);

            _env = await CoreWebView2Environment.CreateAsync(userDataFolder: cacheFolder);
            await WebView.EnsureCoreWebView2Async(_env);
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            WebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            WebView.CoreWebView2.NavigationStarting  += OnNavigationStarting;
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.NewWindowRequested  += OnNewWindowRequested;
            WebView.CoreWebView2.WebMessageReceived  += OnWebMessageReceived;

            // Map the virtual hostname to the local Renderer folder so the player
            // page is served over https (required for YouTube IFrame API to work).
            var rendererFolder = Path.Combine(AppContext.BaseDirectory, Constants.PlayerRendererFolder);
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                Constants.PlayerVirtualHost,
                rendererFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            var initialUrl = BuildPlayerUrl(_settings.Current.YoutubeChannelId);
            WebView.Source = new Uri(initialUrl);

            _webViewReady = true;
            _logger.LogInformation("WebView2 ready — player URL: {Url}", initialUrl);

            ApplyOpacity(_settings.Current.OverlayOpacity);
            ApplyZoom(_settings.Current.WebViewZoomPct);
            ApplySize(_settings.Current.WebViewWidthPct, _settings.Current.WebViewHeightPct);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _logger.LogError("WebView2 runtime is not installed");
            BalloonTipRequested?.Invoke(
                "WebView2 not installed",
                "Pulsenet Radio requires the Microsoft Edge WebView2 Runtime. Please install it and restart.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebView2 initialization failed");
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Allow internal WebView2 URIs (data:, about:blank etc.)
        if (e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            return;

        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
        if (IsAllowedOrigin(uri)) return;

        // Non-allowed main-frame navigation (e.g. OAuth redirect or external link) —
        // cancel and open in a shared-session popup so auth cookies stay in this env.
        _logger.LogDebug("Opening external origin in popup: {Uri}", e.Uri);
        e.Cancel = true;
        OpenAsAuthPopup(e.Uri);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            _webViewReady = true;
            _navigationErrorShown = false;
            _ = WebView.CoreWebView2.ExecuteScriptAsync(BuildOverlayStyleScript(_opacity, _zoomPct));
            _ = WebView.CoreWebView2.ExecuteScriptAsync(BuildSettingsButtonScript());
            return;
        }

        // OperationCanceled is raised when we cancel the navigation ourselves — not a real error.
        if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;

        if (_navigationErrorShown) return;
        _navigationErrorShown = true;

        _logger.LogWarning("Navigation failed: {Error}", e.WebErrorStatus);

        WebView.NavigateToString(
            "<html><body style='background:#1a1a1a;color:#ccc;font-family:sans-serif;padding:24px'>" +
            "<h2 style='color:#e88'>Could not load Pulsenet Radio player</h2>" +
            "<p>The local player page failed to load.</p>" +
            "<p>Ensure the Renderer folder is present alongside the executable.</p>" +
            "</body></html>");
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (_env is null)
        {
            e.Handled = true;
            OpenInDefaultBrowser(e.Uri);
            return;
        }

        // All new-window requests (OAuth popups, YouTube sign-in, etc.) are opened
        // inside a shared WebView2 popup so auth cookies end up in the same environment.
        var deferral = e.GetDeferral();
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var wv = new Microsoft.Web.WebView2.Wpf.WebView2();
                var popup = new Window
                {
                    Title = "Sign In",
                    Width  = e.WindowFeatures.HasSize ? e.WindowFeatures.Width  : 500,
                    Height = e.WindowFeatures.HasSize ? e.WindowFeatures.Height : 700,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Content = wv,
                };

                popup.Show();
                await wv.EnsureCoreWebView2Async(_env);

                wv.CoreWebView2.WindowCloseRequested += (_, _) => Dispatcher.Invoke(popup.Close);
                wv.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

                e.NewWindow = wv.CoreWebView2;
                e.Handled = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create popup; falling back to browser");
                e.Handled = true;
                OpenInDefaultBrowser(e.Uri);
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private void OpenAsAuthPopup(string uri)
    {
        if (_env is null)
        {
            _logger.LogWarning("Auth popup requested but WebView2 env not ready; falling back to browser");
            OpenInDefaultBrowser(uri);
            return;
        }

        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var wv = new Microsoft.Web.WebView2.Wpf.WebView2();
                var popup = new Window
                {
                    Title = "Sign In",
                    Width  = 500,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Content = wv,
                };

                popup.Show();
                await wv.EnsureCoreWebView2Async(_env);

                wv.CoreWebView2.NavigationStarting += (_, args) =>
                {
                    if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var dest)) return;
                    if (!IsAllowedOrigin(dest)) return;

                    // Auth callback redirected back to our origin — complete in main window.
                    _logger.LogInformation("Auth popup returned to player origin; completing in main window");
                    args.Cancel = true;
                    Dispatcher.Invoke(() =>
                    {
                        popup.Close();
                        WebView.Source = dest;
                    });
                };

                wv.CoreWebView2.WindowCloseRequested += (_, _) => Dispatcher.Invoke(popup.Close);
                wv.CoreWebView2.NewWindowRequested  += OnNewWindowRequested;

                wv.Source = new Uri(uri);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open auth popup; falling back to browser");
                OpenInDefaultBrowser(uri);
            }
        });
    }

    private static void OpenInDefaultBrowser(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return;
        if (!uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;

        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
    }

    private bool IsAllowedOrigin(Uri uri)
    {
        // Only the local player virtual host is allowed in the main frame.
        // All other origins (YouTube, Google OAuth, etc.) become popups so that
        // auth cookies remain in the same WebView2 environment.
        return string.Equals(uri.Host, Constants.PlayerVirtualHost, StringComparison.OrdinalIgnoreCase);
    }
}
