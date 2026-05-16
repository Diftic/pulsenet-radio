namespace pulsenet.Services;

using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Models;
using PInvoke;
using Settings;

/// <summary>
/// Connects to PulseNetHotkeyService (the elevated helper) over a named pipe
/// and forwards hotkey events into the same <see cref="HotkeyPressed"/> event
/// the local <see cref="GlobalHotkeyListener"/> raises. Lets the player
/// receive hotkey events from game-IL windows on Windows 11 26200+, where
/// the player's own user-mode WH_KEYBOARD_LL is silently skipped.
///
/// Background reconnect loop: if the helper isn't running at startup or
/// disconnects later, this client retries every <see cref="RetryDelay"/>
/// until cancellation. Consumers subscribe to <see cref="ConnectionStateChanged"/>
/// and typically pause <see cref="GlobalHotkeyListener"/> while connected
/// so the same key press isn't dispatched twice.
///
/// Pipe protocol matches PulseNetHotkeyService.Program (line-delimited JSON):
///   client -> server: {"type":"setKeys","vkCodes":[120]}
///   server -> client: {"type":"hotkey"}
/// </summary>
public sealed class HotkeyClient : IHostedService, IDisposable
{
    private const string PipeName = "PulseNetHotkey";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly SettingsManager _settings;
    private readonly ILogger<HotkeyClient> _logger;
    private CancellationTokenSource? _cts;
    private Task? _runner;

    public event EventHandler? HotkeyPressed;
    public event EventHandler<bool>? ConnectionStateChanged;
    public bool IsConnected { get; private set; }

    public HotkeyClient(SettingsManager settings, ILogger<HotkeyClient> logger)
    {
        _settings = settings;
        _logger   = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts    = new CancellationTokenSource();
        _runner = Task.Run(() => RunLoop(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_runner is not null)
        {
            try { await _runner.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); }
            catch { /* swallow on shutdown */ }
        }
    }

    public void Dispose() => _cts?.Dispose();

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndPumpAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("HotkeyClient pump iteration failed: {Msg}", ex.Message);
            }

            SetConnected(false);
            try { await Task.Delay(RetryDelay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ConnectAndPumpAsync(CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(
            ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(timeout: 1000, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogTrace("Hotkey helper pipe not available (will retry)");
            return;
        }
        catch (IOException ex)
        {
            _logger.LogTrace("Hotkey helper connect failed: {Msg}", ex.Message);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            // Helper running unelevated would land here. Helper must be elevated
            // so its LL hook fires for game-IL windows. Surface a warning so the
            // user knows the setup is wrong, then retry (in case they fix it).
            _logger.LogWarning("Hotkey helper pipe access denied: {Msg}", ex.Message);
            return;
        }

        _logger.LogInformation("Connected to hotkey helper");
        SetConnected(true);

        using var reader = new StreamReader(pipe);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };

        await SendSetKeysAsync(writer, ct).ConfigureAwait(false);

        // Push an updated setKeys whenever the user remaps the hotkey, so the
        // helper starts matching the new combo without a service restart.
        EventHandler<PulsenetSettings> onChange = (_, _) =>
            _ = SendSetKeysAsync(writer, ct);
        _settings.SettingsChanged += onChange;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                HandleMessage(line);
            }
        }
        finally
        {
            _settings.SettingsChanged -= onChange;
        }
    }

    private async Task SendSetKeysAsync(StreamWriter writer, CancellationToken ct)
    {
        try
        {
            var vkCodes = _settings.Current.ToggleHotkey.PressedKeys
                .Select(WindowsKeyMap.ToCode)
                .Select(vk => (int)vk)
                .ToArray();
            var payload = JsonSerializer.Serialize(new { type = "setKeys", vkCodes });
            await writer.WriteLineAsync(payload.AsMemory(), ct).ConfigureAwait(false);
            _logger.LogDebug("Sent setKeys vkCodes=[{Codes}]", string.Join(",", vkCodes));
        }
        catch (IOException)
        {
            // Pipe died mid-write; reconnect loop will pick it up.
        }
    }

    private void HandleMessage(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var t)) return;

            switch (t.GetString())
            {
                case "hotkey":
                    _logger.LogDebug("Hotkey received from helper");
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug("Invalid JSON from helper: {Msg}", ex.Message);
        }
    }

    private void SetConnected(bool connected)
    {
        if (IsConnected == connected) return;
        IsConnected = connected;
        _logger.LogInformation("HotkeyClient state: {State}", connected ? "connected" : "disconnected");
        ConnectionStateChanged?.Invoke(this, connected);
    }
}
