namespace PulseNetHotkeyService;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hosted service that installs WH_KEYBOARD_LL and forwards configured hotkey
/// matches to a single connected pipe client. Runs identically as a Windows
/// Service (SCM-launched, SYSTEM identity) or as a console process (developer
/// run) - AddWindowsService in Program.cs handles the host adapter.
///
/// Pipe path: \\.\pipe\PulseNetHotkey
/// Protocol (JSON, one message per line):
///   client -> server: {"type":"setKeys","vkCodes":[120]}
///   server -> client: {"type":"hotkey"}
/// </summary>
public sealed class HookService : IHostedService, IDisposable
{
    private const string PipeName = "PulseNetHotkey";

    private readonly ILogger<HookService> _logger;
    private readonly object _stateLock = new();
    private readonly HashSet<int> _heldKeys = new();
    private volatile HashSet<int> _watchedKeys = new();
    private StreamWriter? _currentWriter;

    private CancellationTokenSource? _cts;
    private Thread? _hookThread;
    private Task? _pipeTask;
    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private uint _hookThreadId;

    public HookService(ILogger<HookService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PulseNet hotkey helper starting (pid={Pid})", Environment.ProcessId);
        _cts = new CancellationTokenSource();

        // Hook needs its own thread with a message loop. The Marshal interop
        // keeps the delegate alive for the hook's lifetime via this instance field.
        _hookProc   = HookCallback;
        _hookThread = new Thread(RunHookThread) { IsBackground = true, Name = "LL keyboard hook" };
        _hookThread.Start();

        _pipeTask = Task.Run(() => RunPipeServerAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Helper stopping");
        _cts?.Cancel();

        if (_hookThreadId != 0)
            NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _hookThread?.Join(TimeSpan.FromSeconds(2));

        if (_pipeTask is not null)
        {
            try { await _pipeTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); }
            catch { /* swallow on shutdown */ }
        }
        _logger.LogInformation("Helper stopped");
    }

    public void Dispose() => _cts?.Dispose();

    // -------------------------------------------------------------------------
    // Keyboard hook
    // -------------------------------------------------------------------------

    private void RunHookThread()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _hookProc!, IntPtr.Zero, 0);
        if (_hookId == IntPtr.Zero)
        {
            _logger.LogError("SetWindowsHookEx failed: 0x{Err:X8}", Marshal.GetLastWin32Error());
            return;
        }
        _logger.LogInformation("LL keyboard hook installed");

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            NativeMethods.TranslateMessage(in msg);
            NativeMethods.DispatchMessage(in msg);
        }

        NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        _logger.LogInformation("LL keyboard hook removed");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try { HandleKeyEvent(wParam, lParam); }
            catch (Exception ex) { _logger.LogError(ex, "Hook callback threw"); }
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void HandleKeyEvent(IntPtr wParam, IntPtr lParam)
    {
        var kbd = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        var vk  = (int)kbd.vkCode;
        var msg = wParam.ToInt32();

        var isDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
        var isUp   = msg == NativeMethods.WM_KEYUP   || msg == NativeMethods.WM_SYSKEYUP;

        if (isDown)
        {
            lock (_stateLock)
            {
                _heldKeys.RemoveWhere(k => (NativeMethods.GetAsyncKeyState(k) & 0x8000) == 0);
                _heldKeys.Add(vk);
            }
            return;
        }

        if (!isUp) return;

        bool matched;
        lock (_stateLock)
        {
            var watched = _watchedKeys;
            matched = watched.Count > 0 && _heldKeys.SetEquals(watched);
            _heldKeys.Remove(vk);
        }

        if (matched)
        {
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            SendHotkey();
        }
    }

    private void SendHotkey()
    {
        StreamWriter? w;
        lock (_stateLock) { w = _currentWriter; }
        if (w is null) return;

        try
        {
            lock (w)
            {
                w.WriteLine("{\"type\":\"hotkey\"}");
                w.Flush();
            }
            _logger.LogDebug("Hotkey matched -> client");
        }
        catch (IOException)
        {
            // Client gone; the pipe accept loop will notice and clean up.
        }
    }

    // -------------------------------------------------------------------------
    // Named-pipe server
    // -------------------------------------------------------------------------

    private async Task RunPipeServerAsync(CancellationToken ct)
    {
        // Default pipe ACL inherits the (elevated/SYSTEM) server's token, which
        // would block medium-IL clients. Grant authenticated users explicit
        // ReadWrite so the player running as the interactive user can connect.
        var pipeSec = new PipeSecurity();
        pipeSec.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        while (!ct.IsCancellationRequested)
        {
            var server = NamedPipeServerStreamAcl.Create(
                pipeName: PipeName,
                direction: PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                transmissionMode: PipeTransmissionMode.Byte,
                options: PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 0,
                pipeSecurity: pipeSec);

            try
            {
                _logger.LogInformation(@"Waiting for client on \\.\pipe\{Pipe}", PipeName);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _logger.LogInformation("Client connected");

                var reader = new StreamReader(server);
                var writer = new StreamWriter(server) { AutoFlush = false };

                lock (_stateLock) { _currentWriter = writer; }

                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                        if (line is null) break;
                        HandleClientMessage(line);
                    }
                }
                catch (IOException) { /* client disconnect */ }
                catch (OperationCanceledException) { break; }
                finally
                {
                    lock (_stateLock)
                    {
                        _currentWriter = null;
                        _watchedKeys   = new HashSet<int>();
                    }
                    _logger.LogInformation("Client disconnected");
                }
            }
            finally
            {
                server.Dispose();
            }
        }
    }

    private void HandleClientMessage(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var t)) return;

            switch (t.GetString())
            {
                case "setKeys":
                    if (root.TryGetProperty("vkCodes", out var keys) &&
                        keys.ValueKind == JsonValueKind.Array)
                    {
                        var newSet = new HashSet<int>();
                        foreach (var k in keys.EnumerateArray())
                            if (k.TryGetInt32(out var code))
                                newSet.Add(code);
                        _watchedKeys = newSet;
                        _logger.LogInformation("Watching vkCodes=[{Codes}]", string.Join(",", newSet));
                    }
                    break;

                default:
                    _logger.LogDebug("Unknown message type: {Type}", t.GetString());
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug("Invalid JSON from client: {Msg}", ex.Message);
        }
    }
}
