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

/// <summary>
/// Standalone helper for PulseNet Player's hotkey delivery. Installs a global
/// low-level keyboard hook (WH_KEYBOARD_LL) and forwards configured hotkey
/// matches to the connected client over a named pipe. Run elevated so the hook
/// still fires when a game-IL window has focus on Windows 11 26200+, where
/// the user-mode hook policy was tightened.
///
/// G1 scope: console exe, single-client pipe, no service lifecycle yet.
/// Pipe path: \\.\pipe\PulseNetHotkey
/// Protocol (JSON, one message per line):
///   client -> server: {"type":"setKeys","vkCodes":[120]}
///   server -> client: {"type":"hotkey"}
/// </summary>
internal static class Program
{
    private const string PipeName = "PulseNetHotkey";

    private static readonly object _stateLock = new();
    private static volatile HashSet<int> _watchedKeys = new();
    private static StreamWriter? _currentWriter;
    private static readonly HashSet<int> _heldKeys = new();

    private static IntPtr _hookId = IntPtr.Zero;
    private static NativeMethods.LowLevelKeyboardProc? _hookProc;
    private static uint _hookThreadId;

    public static int Main(string[] _)
    {
        Console.WriteLine($"PulseNet hotkey helper starting (pid={Environment.ProcessId})");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("Ctrl+C received, shutting down");
            cts.Cancel();
        };

        // Hook needs its own thread with a message loop. Marshal must keep the
        // delegate alive for the hook's lifetime, so it lives in a static field.
        _hookProc = HookCallback;
        var hookThread = new Thread(RunHookThread) { IsBackground = true, Name = "LL keyboard hook" };
        hookThread.Start();

        // Pipe server runs on the main thread. Returns when ct is cancelled.
        try
        {
            RunPipeServerAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }

        // Wake the hook thread so it exits its GetMessage loop.
        if (_hookThreadId != 0)
            NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        hookThread.Join(TimeSpan.FromSeconds(2));

        Console.WriteLine("Helper exited");
        return 0;
    }

    // -------------------------------------------------------------------------
    // Keyboard hook
    // -------------------------------------------------------------------------

    private static void RunHookThread()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _hookProc!, IntPtr.Zero, 0);
        if (_hookId == IntPtr.Zero)
        {
            Console.Error.WriteLine(
                $"SetWindowsHookEx failed: 0x{Marshal.GetLastWin32Error():X8}");
            return;
        }
        Console.WriteLine("LL keyboard hook installed");

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            NativeMethods.TranslateMessage(in msg);
            NativeMethods.DispatchMessage(in msg);
        }

        NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        Console.WriteLine("LL keyboard hook removed");
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try { HandleKeyEvent(wParam, lParam); }
            catch (Exception ex) { Console.Error.WriteLine($"Hook callback threw: {ex}"); }
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static void HandleKeyEvent(IntPtr wParam, IntPtr lParam)
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
                // Prune stale held keys before adding the new down. WM_KEYUP
                // is silently dropped during UAC prompts / session switches, so
                // _heldKeys can otherwise accumulate phantom entries that block
                // SetEquals from ever matching again.
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
            // Match before removing the key being released - all watched keys
            // were still down at the moment the final key released.
            matched = watched.Count > 0 && _heldKeys.SetEquals(watched);
            _heldKeys.Remove(vk);
        }

        if (matched)
        {
            // ASFW_ANY (=0xFFFFFFFF) lets any window steal foreground from us
            // until our next input event. The player uses this when it wakes up
            // to ensure it can raise its overlay over the focused game.
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            SendHotkey();
        }
    }

    private static void SendHotkey()
    {
        StreamWriter? w;
        lock (_stateLock) { w = _currentWriter; }
        if (w is null) return;

        try
        {
            lock (w) // serialize writes from concurrent hook fires
            {
                w.WriteLine("{\"type\":\"hotkey\"}");
                w.Flush();
            }
            Console.WriteLine("Hotkey matched -> client");
        }
        catch (IOException)
        {
            // Client gone; the pipe accept loop will notice and clean up.
        }
    }

    // -------------------------------------------------------------------------
    // Named-pipe server
    // -------------------------------------------------------------------------

    private static async Task RunPipeServerAsync(CancellationToken ct)
    {
        // Default pipe ACL inherits the (elevated) server's High-IL token,
        // which blocks medium-IL clients. Grant authenticated users explicit
        // ReadWrite so the player running as the interactive user can connect.
        var pipeSec = new PipeSecurity();
        pipeSec.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        while (!ct.IsCancellationRequested)
        {
            // Single-instance pipe: only one player connects at a time. If the
            // player crashes, the pipe handle closes and we loop back to accept.
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
                Console.WriteLine($@"Waiting for client on \\.\pipe\{PipeName}");
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                Console.WriteLine("Client connected");

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
                finally
                {
                    lock (_stateLock)
                    {
                        _currentWriter = null;
                        _watchedKeys = new HashSet<int>();
                    }
                    Console.WriteLine("Client disconnected");
                }
            }
            finally
            {
                server.Dispose();
            }
        }
    }

    private static void HandleClientMessage(string line)
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
                        Console.WriteLine($"Watching vkCodes=[{string.Join(",", newSet)}]");
                    }
                    break;

                default:
                    Console.WriteLine($"Unknown message type: {t.GetString()}");
                    break;
            }
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Invalid JSON from client ({ex.Message}): {line}");
        }
    }
}

// -------------------------------------------------------------------------
// Win32 imports. Kept raw rather than via CsWin32 to keep the helper small.
// -------------------------------------------------------------------------

internal static class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN     = 0x0100;
    public const int WM_KEYUP       = 0x0101;
    public const int WM_SYSKEYDOWN  = 0x0104;
    public const int WM_SYSKEYUP    = 0x0105;
    public const uint WM_QUIT       = 0x0012;
    public const uint ASFW_ANY      = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint   message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint   time;
        public int    pt_x;
        public int    pt_y;
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(in MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(in MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);
}
