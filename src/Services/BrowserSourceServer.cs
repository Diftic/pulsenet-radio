namespace pulsenet.Services;

using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Settings;

/// <summary>
/// Localhost-only HTTP server exposing PulseNet's player as a URL that
/// streamers paste into OBS as a Browser Source. Sidesteps the
/// Window-Capture-vs-tool-window problem entirely (Browser Source supports
/// alpha and composites cleanly over a Game Capture source).
///
/// Routes:
///   GET /         → tiny landing page with the player URL
///   GET /player   → /Renderer/obs/player.html (channel ID substituted in)
///   GET /events   → text/event-stream pushing player state changes (play/pause)
///                   so the OBS player can mirror what the in-app player is doing
///   GET /assets/* → static files from /Renderer/obs/ and /Renderer/
///
/// Built on TcpListener (not HttpListener) to avoid the Windows URL ACL
/// elevation requirement — TcpListener binds any high port for the calling
/// user without an admin prompt.
/// </summary>
public sealed class BrowserSourceServer : IHostedService, IDisposable
{
    private readonly NowPlayingState _state;
    private readonly SettingsManager _settings;
    private readonly ILogger<BrowserSourceServer> _logger;

    private readonly object _sseLock = new();
    private readonly List<SseClient> _sseClients = [];

    private TcpListener? _listener;
    private CancellationTokenSource? _runCts;
    private Task? _acceptLoop;
    private int _boundPort;

    /// <summary>Currently bound port, or 0 when the listener is not running.</summary>
    public int BoundPort => _boundPort;

    /// <summary>True when the listener is accepting connections.</summary>
    public bool IsListening => _listener is not null;

    /// <summary>
    /// Raised whenever a (re)bind attempt completes. Args are the port that was
    /// attempted and whether the bind succeeded. Subscribers run on a thread pool
    /// thread; marshal to the UI thread before touching WPF state.
    /// </summary>
    public event Action<int, bool>? BindStateChanged;

    public BrowserSourceServer(
        NowPlayingState state,
        SettingsManager settings,
        ILogger<BrowserSourceServer> logger)
    {
        _state = state;
        _settings = settings;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _state.Changed += OnNowPlayingChanged;
        _settings.SettingsChanged += OnSettingsChanged;
        TryBind(_settings.Current.BrowserSourcePort);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _state.Changed -= OnNowPlayingChanged;
        _settings.SettingsChanged -= OnSettingsChanged;
        await ShutdownListenerAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        try { ShutdownListenerAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
    }

    // -------------------------------------------------------------------------
    // Bind / rebind
    // -------------------------------------------------------------------------

    private void OnSettingsChanged(object? sender, Models.PulsenetSettings settings)
    {
        if (settings.BrowserSourcePort == _boundPort) return;
        _ = Task.Run(async () =>
        {
            await ShutdownListenerAsync().ConfigureAwait(false);
            TryBind(settings.BrowserSourcePort);
        });
    }

    private void TryBind(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Parse(Constants.BrowserSourceLoopback), port);
            listener.Start();
            _listener = listener;
            _boundPort = port;
            _runCts = new CancellationTokenSource();
            var token = _runCts.Token;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, token));
            _logger.LogInformation("Browser Source server listening on http://{IP}:{Port}/",
                Constants.BrowserSourceLoopback, port);
            BindStateChanged?.Invoke(port, true);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("Browser Source server could not bind to port {Port}: {Msg}",
                port, ex.Message);
            _listener = null;
            _boundPort = 0;
            BindStateChanged?.Invoke(port, false);
        }
    }

    private async Task ShutdownListenerAsync()
    {
        var listener = _listener;
        var cts = _runCts;
        _listener = null;
        _runCts = null;
        _boundPort = 0;

        cts?.Cancel();
        try { listener?.Stop(); } catch { /* ignore */ }

        SseClient[] clients;
        lock (_sseLock)
        {
            clients = _sseClients.ToArray();
            _sseClients.Clear();
        }
        foreach (var c in clients) c.Dispose();

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); }
            catch { /* expected on cancel */ }
            _acceptLoop = null;
        }
        cts?.Dispose();
    }

    // -------------------------------------------------------------------------
    // Accept loop
    // -------------------------------------------------------------------------

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException)    { break; }
            catch (SocketException)            { break; }

            _ = Task.Run(() => HandleConnectionAsync(client, token));
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(requestLine))
            {
                reader.Dispose(); stream.Dispose(); client.Close();
                return;
            }

            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(token).ConfigureAwait(false)))
            {
                // discard headers
            }

            var parts = requestLine.Split(' ');
            if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.Ordinal))
            {
                await WriteSimpleAsync(stream, 405, "text/plain", "Method Not Allowed", token);
                reader.Dispose(); stream.Dispose(); client.Close();
                return;
            }

            var path = parts[1];
            var qIdx = path.IndexOf('?');
            if (qIdx >= 0) path = path[..qIdx];

            // /events keeps the socket open for the SSE stream — RouteAsync hands
            // ownership to the SSE client list and we must NOT dispose here.
            if (string.Equals(path, "/events", StringComparison.Ordinal))
            {
                reader.Dispose();
                await HandleSseAsync(stream, client, token).ConfigureAwait(false);
                return;
            }

            await RouteAsync(stream, path, token).ConfigureAwait(false);
            reader.Dispose(); stream.Dispose(); client.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Browser Source connection error: {Ex}", ex.Message);
            try { client.Close(); } catch { /* ignore */ }
        }
    }

    // -------------------------------------------------------------------------
    // Routing
    // -------------------------------------------------------------------------

    private async Task RouteAsync(NetworkStream stream, string path, CancellationToken token)
    {
        switch (path)
        {
            case "/":
                await WriteHtmlAsync(stream, BuildLandingHtml(), token);
                return;

            case "/player":
                await ServePlayerHtmlAsync(stream, token);
                return;
        }

        if (TryResolveStaticAsset(path.TrimStart('/'), out var resolved, out var mime))
        {
            await ServeFileAsync(stream, resolved, mime, token);
            return;
        }

        await WriteSimpleAsync(stream, 404, "text/plain", "Not Found", token);
    }

    private bool TryResolveStaticAsset(string relative, out string resolvedPath, out string mime)
    {
        resolvedPath = string.Empty;
        mime = "application/octet-stream";

        if (string.IsNullOrEmpty(relative) || relative.Contains("..", StringComparison.Ordinal))
            return false;

        var normalised = relative.Replace('/', Path.DirectorySeparatorChar);

        var rendererRoot = Path.Combine(AppContext.BaseDirectory, Constants.PlayerRendererFolder);
        var obsFolder    = Path.Combine(rendererRoot, Constants.BrowserSourceObsFolder);

        foreach (var root in new[] { obsFolder, rendererRoot })
        {
            var candidate = Path.GetFullPath(Path.Combine(root, normalised));
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(candidate)) continue;

            resolvedPath = candidate;
            mime = MimeFor(candidate);
            return true;
        }
        return false;
    }

    private static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css"  => "text/css; charset=utf-8",
        ".js"   => "application/javascript; charset=utf-8",
        ".png"  => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".svg"  => "image/svg+xml",
        ".ico"  => "image/x-icon",
        ".webp" => "image/webp",
        _       => "application/octet-stream",
    };

    private async Task ServeFileAsync(NetworkStream stream, string filePath, string mime, CancellationToken token)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, token).ConfigureAwait(false);
        await WriteResponseAsync(stream, 200, "OK", mime, bytes, token);
    }

    private async Task ServePlayerHtmlAsync(NetworkStream stream, CancellationToken token)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            Constants.PlayerRendererFolder,
            Constants.BrowserSourceObsFolder,
            "player.html");

        if (!File.Exists(path))
        {
            await WriteSimpleAsync(stream, 404, "text/plain", "Missing obs/player.html", token);
            return;
        }

        var html = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
        var configured = _settings.Current.YoutubeChannelId;
        var channelId = string.IsNullOrWhiteSpace(configured)
            ? Constants.PulsenetBroadcastChannelId
            : configured;
        html = html.Replace("{{CHANNEL_ID}}", JsonEncodedString(channelId), StringComparison.Ordinal);

        var bytes = Encoding.UTF8.GetBytes(html);
        await WriteResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", bytes, token);
    }

    private static string JsonEncodedString(string raw)
    {
        return raw
            .Replace("\\", "\\\\")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private async Task WriteHtmlAsync(NetworkStream stream, string html, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        await WriteResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", bytes, token);
    }

    private static async Task WriteSimpleAsync(NetworkStream stream, int status, string mime, string body, CancellationToken token)
    {
        var reason = status switch { 404 => "Not Found", 405 => "Method Not Allowed", _ => "OK" };
        var bytes = Encoding.UTF8.GetBytes(body);
        await WriteResponseAsync(stream, status, reason, mime, bytes, token);
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream, int status, string reason, string mime, byte[] body, CancellationToken token)
    {
        var header =
            $"HTTP/1.1 {status} {reason}\r\n" +
            $"Content-Type: {mime}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n" +
            "\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, token).ConfigureAwait(false);
        if (body.Length > 0)
            await stream.WriteAsync(body, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // SSE — pushes player state changes to connected OBS Browser Source pages
    // so they can hide the YouTube iframe when the in-app player pauses, etc.
    // -------------------------------------------------------------------------

    private async Task HandleSseAsync(NetworkStream stream, TcpClient client, CancellationToken token)
    {
        var header =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/event-stream; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: keep-alive\r\n" +
            "X-Accel-Buffering: no\r\n" +
            "\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);

        var sse = new SseClient(client, stream);
        lock (_sseLock) _sseClients.Add(sse);

        // Push the current state immediately so a freshly-connected OBS page
        // doesn't sit at its default until the next state change.
        try { await sse.SendStateAsync(_state.Current, token).ConfigureAwait(false); }
        catch
        {
            lock (_sseLock) _sseClients.Remove(sse);
            sse.Dispose();
        }
        // Connection stays open — broadcast loop handles future events.
        // Client disconnect is detected on the next failed write.
    }

    private void OnNowPlayingChanged(NowPlayingSnapshot snapshot)
    {
        SseClient[] clients;
        lock (_sseLock) clients = _sseClients.ToArray();

        foreach (var c in clients)
        {
            _ = Task.Run(async () =>
            {
                try { await c.SendStateAsync(snapshot, _runCts?.Token ?? CancellationToken.None).ConfigureAwait(false); }
                catch
                {
                    lock (_sseLock) _sseClients.Remove(c);
                    c.Dispose();
                }
            });
        }
    }

    private sealed class SseClient : IDisposable
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;

        public SseClient(TcpClient client, NetworkStream stream)
        {
            _client = client;
            _stream = stream;
        }

        public async Task SendStateAsync(NowPlayingSnapshot snapshot, CancellationToken token)
        {
            // Single state event covers everything an OBS page might want — keeps
            // the receiver simple. Currently OBS only consumes IsPlaying.
            var payload = JsonSerializer.Serialize(new
            {
                type     = "state",
                isPlaying = snapshot.IsPlaying,
                station  = snapshot.Station,
                title    = snapshot.Title,
            });
            var bytes = Encoding.UTF8.GetBytes($"data: {payload}\n\n");
            await _writeLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await _stream.FlushAsync(token).ConfigureAwait(false);
            }
            finally { _writeLock.Release(); }
        }

        public void Dispose()
        {
            try { _stream.Dispose(); } catch { /* ignore */ }
            try { _client.Close(); }   catch { /* ignore */ }
            _writeLock.Dispose();
        }
    }

    private string BuildLandingHtml()
    {
        var port = _boundPort;
        return $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <title>PulseNet Player — OBS Browser Source</title>
            <style>
              body { font-family: 'Segoe UI', sans-serif; background:#0a1525; color:#cfeefa; padding:32px; }
              h1 { color:#22d3ee; margin-top:0; }
              code { background:#001020; padding:2px 8px; border-radius:4px; color:#22d3ee; }
              .row { margin: 16px 0; }
              p { line-height: 1.5; }
            </style></head><body>
            <h1>PulseNet Player — OBS URL</h1>
            <p>Add this as a <strong>Browser Source</strong> in OBS Studio.</p>
            <div class="row"><strong>Player:</strong><br><code>http://127.0.0.1:{{port}}/player</code></div>
            <p>Keep the player running for the URL to work. The server listens on the loopback address only — nothing is exposed to your network.</p>
            </body></html>
            """;
    }
}
